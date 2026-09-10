---
name: meshchat-e2e
description: "Run MeshChat Bluetooth mesh networking E2E and unit tests. Use this skill whenever the user asks to test, validate, or verify the MeshChat app, run tests, check if the mesh networking works, verify crypto, or wants to add new test scenarios. Also trigger when the user mentions 'run tests', 'E2E', 'integration test', or 'does it work' in the context of the MeshChat/BluetoothTextApp project."
---

# MeshChat E2E Testing Skill

This skill runs and manages the MeshChat test suite — both unit tests and end-to-end integration tests that simulate a full Bluetooth mesh network with real ECDH+AES-256-GCM encryption, message routing, deduplication, TTL enforcement, and transport fallback.

## Prerequisites

- .NET 8 SDK installed on the machine
- The MeshChat solution at the user's project path (look for `MeshChat.sln`)

## How to Run Tests

### Quick run (all tests)

```bash
cd <path-to-MeshChat.sln-directory>
dotnet test MeshChat.Tests/MeshChat.Tests.csproj -c Debug -v normal --logger "console;verbosity=detailed"
```

### Run only E2E tests

```bash
dotnet test MeshChat.Tests/MeshChat.Tests.csproj -c Debug --filter "FullyQualifiedName~EndToEndTests" -v normal --logger "console;verbosity=detailed"
```

### Run only unit tests

```bash
dotnet test MeshChat.Tests/MeshChat.Tests.csproj -c Debug --filter "FullyQualifiedName~Tests&FullyQualifiedName!~EndToEnd" -v normal --logger "console;verbosity=detailed"
```

## Test Architecture

The E2E tests simulate a real mesh network using **in-memory transports** that replace BLE/WiFi radios. Everything else is real:

- **Real CryptoService**: ECDH P-256 key exchange + AES-256-GCM encryption
- **Real MeshRouter**: Packet deduplication, TTL enforcement, relay logic
- **Real TransportOrchestrator**: BLE → WiFi → Relay fallback chain
- **In-memory stores**: Replace SQLite for test speed

### Key test files

| File | What it tests |
|------|---------------|
| `MeshChat.Tests/EndToEndTests.cs` | Full pipeline: crypto → routing → transport → storage |
| `MeshChat.Tests/MeshRouterTests.cs` | Router logic: dedup, TTL, relay, delivery |
| `MeshChat.Tests/CryptoServiceTests.cs` | ECDH key generation, encrypt/decrypt roundtrip |
| `MeshChat.Tests/TransportOrchestratorTests.cs` | Fallback chain, unified packet events |
| `MeshChat.Tests/Mocks/` | InMemoryTransport, MockBleService, InMemoryMessageStore, InMemoryPeerStore |

### E2E test scenarios

1. **TwoPeers_SendAndReceive_EncryptedMessage** — Alice sends encrypted message to Bob, Bob decrypts and stores it
2. **TwoPeers_BidirectionalConversation** — Back-and-forth conversation between two peers
3. **ThreePeers_MessageRelayedThroughMiddlePeer** — Message from Alice reaches Bob through Charlie (multi-hop relay)
4. **Deduplication_PreventsRelayLoop** — Same packet delivered twice, only processed once
5. **TtlExpiry_StopsRelay** — Packet with TTL=0 is not relayed further
6. **TransportFallback_BleFailsToWifi** — When BLE fails, message falls through to WiFi transport
7. **TamperedPayload_DecryptionFails_MessageDropped** — Corrupted ciphertext is rejected by AES-GCM auth
8. **MultipleMessages_AllDeliveredInOrder** — 10 sequential messages all arrive correctly

## Adding New Tests

When adding new E2E test scenarios, follow this pattern:

```csharp
[Fact]
public async Task NewScenario_DescriptiveNameHere()
{
    // 1. Create peers using CreatePeer("Name")
    var alice = CreatePeer("Alice");
    var bob = CreatePeer("Bob");

    // 2. Connect their transports
    alice.BleTransport.ConnectTo(bob.BleTransport);
    await alice.BleTransport.StartAsync();
    await bob.BleTransport.StartAsync();

    // 3. Register each other in peer stores
    await alice.PeerStore.Save(new Peer { Id = bob.Hash, DisplayName = "Bob", PublicKey = bob.PublicKey, ... });
    await bob.PeerStore.Save(new Peer { Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey, ... });

    // 4. Wire router to transport
    WireRouterToTransport(bob);

    // 5. Subscribe to events
    bob.Router.MessageReceived += msg => { ... };

    // 6. Create and send packet
    var packet = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "Message", bob.PublicKey);
    await alice.Orchestrator.SendPacket(packet);

    // 7. Assert
    // ...

    // 8. Cleanup
    alice.Router.Stop();
    bob.Router.Stop();
}
```

## Troubleshooting

- **"Private key not set"**: The CryptoService needs `GenerateIdentity()` called before decryption
- **Timeout on TaskCompletionSource**: Check that `WireRouterToTransport` is called for the receiving peer
- **Dedup blocking legitimate messages**: Each message needs a unique `Guid.NewGuid()` for `MessageId`
- **AES-GCM tag mismatch**: Usually means the shared secret derivation used different key pairs — check that the peer stores have the correct public keys registered

## Interpreting Results

A successful run looks like:

```
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13
```

If tests fail, the output includes the assertion that failed and a stack trace pointing to the exact line. The E2E tests use `ITestOutputHelper` so you can see debug logs with `--logger "console;verbosity=detailed"`.
