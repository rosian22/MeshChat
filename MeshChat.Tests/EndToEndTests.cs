using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;
using MeshChat.Core.Services;
using MeshChat.Tests.Mocks;

namespace MeshChat.Tests;

/// <summary>
/// End-to-end integration tests that simulate a full mesh network with
/// real crypto, real routing, and in-memory transport (replacing BLE/WiFi).
///
/// Each test creates actual CryptoService, MeshRouter, and TransportOrchestrator
/// instances wired together through mock transports — the only mock is the radio.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() { }

    /// <summary>
    /// Creates a fully wired peer with real crypto, router, orchestrator, and stores.
    /// </summary>
    private static PeerNode CreatePeer(string displayName)
    {
        var crypto = new CryptoService();
        var (publicKey, _) = crypto.GenerateIdentity();
        var myHash = crypto.GetPublicKeyHash(publicKey);

        var messageStore = new InMemoryMessageStore();
        var peerStore = new InMemoryPeerStore();
        var router = new MeshRouter(crypto, messageStore, peerStore, myHash);

        var bleTransport = new InMemoryTransport();
        var bleMock = new MockBleService(bleTransport);
        var disabledWifi = new DisabledTransport();
        var disabledRelay = new DisabledTransport();

        var orchestrator = new TransportOrchestrator(bleMock, disabledWifi, disabledRelay);
        orchestrator.WireUp();

        return new PeerNode
        {
            Name = displayName,
            Hash = myHash,
            PublicKey = publicKey,
            Crypto = crypto,
            Router = router,
            Orchestrator = orchestrator,
            MessageStore = messageStore,
            PeerStore = peerStore,
            BleTransport = bleTransport,
            BleMock = bleMock
        };
    }

    /// <summary>
    /// Wires BLE transport received packets into the mesh router for a peer.
    /// </summary>
    private static void WireRouterToTransport(PeerNode peer)
    {
        // When the BLE transport receives a packet, feed it to the router
        peer.BleMock.PacketReceived += async packet =>
        {
            await peer.Router.OnPacketReceived(packet);
        };

        // When the router wants to relay, send via orchestrator
        peer.Router.RelayRequested += async packet =>
        {
            try { await peer.Orchestrator.SendPacket(packet); }
            catch { /* transport may not be available */ }
        };
    }

    // ─── TEST 1: Direct message between two peers ─────────────────────

    [Fact]
    public async Task TwoPeers_SendAndReceive_EncryptedMessage()
    {
        // Arrange — create Alice and Bob
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        // Connect their BLE transports (simulates being in BLE range)
        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        // Register each other as known peers
        await alice.PeerStore.Save(new Peer
        {
            Id = bob.Hash,
            DisplayName = "Bob",
            PublicKey = bob.PublicKey,
            LastSeen = DateTime.UtcNow,
            LastSeenVia = TransportType.BLE
        });

        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash,
            DisplayName = "Alice",
            PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow,
            LastSeenVia = TransportType.BLE
        });

        // Wire routers
        WireRouterToTransport(bob);

        // Capture Bob's received message
        Message? bobReceivedMessage = null;
        var receivedEvent = new TaskCompletionSource<Message>();
        bob.Router.MessageReceived += msg =>
        {
            bobReceivedMessage = msg;
            receivedEvent.TrySetResult(msg);
        };

        // Act — Alice sends an encrypted message to Bob
        var messageId = Guid.NewGuid();
        var packet = alice.Router.CreatePacket(
            messageId, bob.Hash, "Hello Bob, this is encrypted!", bob.PublicKey);

        _output.WriteLine($"Alice ({alice.Hash[..8]}...) sending to Bob ({bob.Hash[..8]}...)");
        _output.WriteLine($"Packet TTL={packet.Ttl}, Payload={packet.EncryptedPayload.Length} bytes");

        await alice.Orchestrator.SendPacket(packet);

        // Assert — Bob received and decrypted the message
        var received = await receivedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        received.Should().NotBeNull();
        received.Content.Should().Be("Hello Bob, this is encrypted!");
        received.SenderId.Should().Be(alice.Hash);
        received.Status.Should().Be(MessageStatus.Delivered);
        received.Id.Should().Be(messageId);

        // Verify Bob's message store persisted it
        var stored = await bob.MessageStore.GetById(messageId);
        stored.Should().NotBeNull();
        stored!.Content.Should().Be("Hello Bob, this is encrypted!");

        _output.WriteLine($"SUCCESS: Bob decrypted message: \"{received.Content}\"");

        // Cleanup
        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 2: Bidirectional conversation ───────────────────────────

    [Fact]
    public async Task TwoPeers_BidirectionalConversation()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        // Register each other
        await alice.PeerStore.Save(new Peer
        {
            Id = bob.Hash, DisplayName = "Bob", PublicKey = bob.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });
        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });

        // Wire both routers
        WireRouterToTransport(alice);
        WireRouterToTransport(bob);

        var aliceMessages = new List<Message>();
        var bobMessages = new List<Message>();
        alice.Router.MessageReceived += msg => aliceMessages.Add(msg);
        bob.Router.MessageReceived += msg => bobMessages.Add(msg);

        // Alice → Bob
        var p1 = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "Hey Bob!", bob.PublicKey);
        await alice.Orchestrator.SendPacket(p1);

        // Bob → Alice
        var p2 = bob.Router.CreatePacket(Guid.NewGuid(), alice.Hash, "Hi Alice!", alice.PublicKey);
        await bob.Orchestrator.SendPacket(p2);

        // Alice → Bob again
        var p3 = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "How's the mesh?", bob.PublicKey);
        await alice.Orchestrator.SendPacket(p3);

        await Task.Delay(100); // Let events propagate

        bobMessages.Should().HaveCount(2);
        bobMessages[0].Content.Should().Be("Hey Bob!");
        bobMessages[1].Content.Should().Be("How's the mesh?");

        aliceMessages.Should().HaveCount(1);
        aliceMessages[0].Content.Should().Be("Hi Alice!");

        _output.WriteLine($"Bidirectional conversation: {bobMessages.Count} msgs to Bob, {aliceMessages.Count} msg to Alice");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 3: Multi-hop relay through intermediate peer ────────────

    [Fact]
    public async Task ThreePeers_MessageRelayedThroughMiddlePeer()
    {
        // Topology: Alice ↔ Charlie ↔ Bob (Alice and Bob NOT directly connected)
        var alice = CreatePeer("Alice");
        var charlie = CreatePeer("Charlie");
        var bob = CreatePeer("Bob");

        // Alice↔Charlie link
        var aliceCharlieTransportA = new InMemoryTransport();
        var aliceCharlieTransportC = new InMemoryTransport();
        aliceCharlieTransportA.ConnectTo(aliceCharlieTransportC);
        await aliceCharlieTransportA.StartAsync();
        await aliceCharlieTransportC.StartAsync();

        // Charlie↔Bob link
        var charlieBobTransportC = new InMemoryTransport();
        var charlieBobTransportB = new InMemoryTransport();
        charlieBobTransportC.ConnectTo(charlieBobTransportB);
        await charlieBobTransportC.StartAsync();
        await charlieBobTransportB.StartAsync();

        // Replace Alice's BLE with the Alice↔Charlie link
        var aliceBle = new MockBleService(aliceCharlieTransportA);
        var aliceOrch = new TransportOrchestrator(aliceBle, new DisabledTransport(), new DisabledTransport());
        aliceOrch.WireUp();

        // Charlie has TWO links — receives from Alice side, sends to Bob side
        // We need Charlie to relay: receive on Alice-link, forward on Bob-link
        var charlieBleFromAlice = new MockBleService(aliceCharlieTransportC);
        var charlieBleFromBob = new MockBleService(charlieBobTransportC);

        // Wire Charlie's router to receive from both directions
        charlieBleFromAlice.PacketReceived += async packet =>
        {
            await charlie.Router.OnPacketReceived(packet);
        };
        charlieBleFromBob.PacketReceived += async packet =>
        {
            await charlie.Router.OnPacketReceived(packet);
        };

        // When Charlie relays, broadcast to both links
        charlie.Router.RelayRequested += async packet =>
        {
            try { await charlieBleFromBob.SendAsync(packet); } catch { }
            try { await charlieBleFromAlice.SendAsync(packet); } catch { }
        };

        // Bob's BLE is the Charlie↔Bob link
        var bobBle = new MockBleService(charlieBobTransportB);
        bobBle.PacketReceived += async packet =>
        {
            await bob.Router.OnPacketReceived(packet);
        };

        // Register Bob in Alice's peer store (Alice knows Bob's public key even if not directly connected)
        await alice.PeerStore.Save(new Peer
        {
            Id = bob.Hash, DisplayName = "Bob", PublicKey = bob.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });
        // Bob needs Alice's key to decrypt
        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });

        // Capture Bob's received message
        var bobReceived = new TaskCompletionSource<Message>();
        bob.Router.MessageReceived += msg => bobReceived.TrySetResult(msg);

        // Act — Alice sends to Bob (must relay through Charlie)
        var messageId = Guid.NewGuid();
        var packet = alice.Router.CreatePacket(messageId, bob.Hash, "Relayed through Charlie!", bob.PublicKey);

        _output.WriteLine($"Alice → [Charlie relay] → Bob");
        _output.WriteLine($"Original TTL: {packet.Ttl}");

        await aliceOrch.SendPacket(packet);

        // Assert
        var received = await bobReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Content.Should().Be("Relayed through Charlie!");
        received.SenderId.Should().Be(alice.Hash);

        // Charlie should NOT have stored the message (it wasn't for him)
        charlie.MessageStore.All.Should().BeEmpty();

        // Bob should have it stored
        var bobStored = await bob.MessageStore.GetById(messageId);
        bobStored.Should().NotBeNull();

        _output.WriteLine($"SUCCESS: Message relayed through Charlie and decrypted by Bob");

        alice.Router.Stop();
        charlie.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 4: Deduplication prevents infinite relay loops ──────────

    [Fact]
    public async Task Deduplication_PreventsRelayLoop()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        WireRouterToTransport(bob);

        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });

        var receiveCount = 0;
        bob.Router.MessageReceived += _ => Interlocked.Increment(ref receiveCount);

        // Send same packet twice (simulates a relay loop)
        var packet = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "No dupes!", bob.PublicKey);
        await alice.Orchestrator.SendPacket(packet);

        // Manually deliver the same packet again (as if it looped back)
        bob.BleTransport.DeliverLocally(packet);

        await Task.Delay(100);

        receiveCount.Should().Be(1, "duplicate packet should be dropped by dedup");
        _output.WriteLine($"SUCCESS: Dedup blocked duplicate delivery (received {receiveCount} time)");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 5: TTL expiry stops infinite propagation ───────────────

    [Fact]
    public async Task TtlExpiry_StopsRelay()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        var relayCount = 0;
        bob.Router.RelayRequested += _ => Interlocked.Increment(ref relayCount);

        // Create a packet NOT for Bob with TTL=0 (should NOT be relayed)
        var packet = new MeshPacket
        {
            MessageId = Guid.NewGuid(),
            Ttl = 0,
            DestinationHash = "unknown-peer",
            SenderHash = alice.Hash,
            EncryptedPayload = new byte[] { 1, 2, 3 },
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Wire Bob's transport to router
        WireRouterToTransport(bob);

        await alice.Orchestrator.SendPacket(packet);
        await Task.Delay(100);

        relayCount.Should().Be(0, "TTL=0 packet should not be relayed");
        _output.WriteLine($"SUCCESS: TTL=0 packet dropped, relay count = {relayCount}");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 6: Transport fallback from BLE to WiFi ─────────────────

    [Fact]
    public async Task TransportFallback_BleFailsToWifi()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        // Create separate BLE and WiFi transports
        var aliceBle = new InMemoryTransport();
        var bobBle = new InMemoryTransport();
        aliceBle.ConnectTo(bobBle);
        await aliceBle.StartAsync();
        await bobBle.StartAsync();

        var aliceWifi = new InMemoryTransport();
        var bobWifi = new InMemoryTransport();
        aliceWifi.ConnectTo(bobWifi);
        await aliceWifi.StartAsync();
        await bobWifi.StartAsync();

        var aliceBleMock = new MockBleService(aliceBle);

        // Alice's orchestrator has both BLE and WiFi
        var aliceOrch = new TransportOrchestrator(aliceBleMock, aliceWifi, new DisabledTransport());
        aliceOrch.WireUp();

        // Wire Bob's WiFi transport to his router
        bobWifi.PacketReceived += async packet => await bob.Router.OnPacketReceived(packet);

        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.WiFi
        });

        var bobReceived = new TaskCompletionSource<Message>();
        bob.Router.MessageReceived += msg => bobReceived.TrySetResult(msg);

        // Simulate BLE failure (out of range)
        aliceBleMock.SimulateFailure(true);
        _output.WriteLine("BLE simulated as failed — should fall back to WiFi");

        // Alice sends — should fall through BLE to WiFi
        var packet = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "Via WiFi fallback!", bob.PublicKey);
        await aliceOrch.SendPacket(packet);

        var received = await bobReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Content.Should().Be("Via WiFi fallback!");

        _output.WriteLine($"SUCCESS: Message arrived via WiFi fallback");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 7: Crypto integrity — tampered payload rejected ────────

    [Fact]
    public async Task TamperedPayload_DecryptionFails_MessageDropped()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });

        WireRouterToTransport(bob);

        var receiveCount = 0;
        bob.Router.MessageReceived += _ => Interlocked.Increment(ref receiveCount);

        // Create legitimate packet, then tamper with it
        var packet = alice.Router.CreatePacket(Guid.NewGuid(), bob.Hash, "Secret", bob.PublicKey);

        // Flip some bytes in the encrypted payload
        var tampered = packet with
        {
            EncryptedPayload = packet.EncryptedPayload.ToArray() // copy
        };
        tampered.EncryptedPayload[tampered.EncryptedPayload.Length - 1] ^= 0xFF;
        tampered.EncryptedPayload[tampered.EncryptedPayload.Length - 2] ^= 0xAA;

        await alice.Orchestrator.SendPacket(tampered);
        await Task.Delay(200);

        receiveCount.Should().Be(0, "tampered payload should fail AES-GCM authentication");
        _output.WriteLine("SUCCESS: Tampered message was rejected (AES-GCM auth tag failed)");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── TEST 8: Multiple messages in sequence ───────────────────────

    [Fact]
    public async Task MultiplMessages_AllDeliveredInOrder()
    {
        var alice = CreatePeer("Alice");
        var bob = CreatePeer("Bob");

        alice.BleTransport.ConnectTo(bob.BleTransport);
        await alice.BleTransport.StartAsync();
        await bob.BleTransport.StartAsync();

        await alice.PeerStore.Save(new Peer
        {
            Id = bob.Hash, DisplayName = "Bob", PublicKey = bob.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });
        await bob.PeerStore.Save(new Peer
        {
            Id = alice.Hash, DisplayName = "Alice", PublicKey = alice.PublicKey,
            LastSeen = DateTime.UtcNow, LastSeenVia = TransportType.BLE
        });

        WireRouterToTransport(bob);

        var bobMessages = new List<string>();
        bob.Router.MessageReceived += msg => bobMessages.Add(msg.Content);

        // Send 10 messages
        for (int i = 0; i < 10; i++)
        {
            var p = alice.Router.CreatePacket(
                Guid.NewGuid(), bob.Hash, $"Message #{i}", bob.PublicKey);
            await alice.Orchestrator.SendPacket(p);
        }

        await Task.Delay(200);

        bobMessages.Should().HaveCount(10);
        for (int i = 0; i < 10; i++)
        {
            bobMessages[i].Should().Be($"Message #{i}");
        }

        bob.MessageStore.All.Should().HaveCount(10);

        _output.WriteLine($"SUCCESS: All 10 messages delivered and stored");

        alice.Router.Stop();
        bob.Router.Stop();
    }

    // ─── Helper: Peer node container ─────────────────────────────────

    private class PeerNode
    {
        public required string Name { get; init; }
        public required string Hash { get; init; }
        public required byte[] PublicKey { get; init; }
        public required CryptoService Crypto { get; init; }
        public required MeshRouter Router { get; init; }
        public required TransportOrchestrator Orchestrator { get; init; }
        public required InMemoryMessageStore MessageStore { get; init; }
        public required InMemoryPeerStore PeerStore { get; init; }
        public required InMemoryTransport BleTransport { get; init; }
        public required MockBleService BleMock { get; init; }
    }
}
