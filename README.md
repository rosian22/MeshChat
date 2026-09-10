# MeshChat 📡

**Off-grid, end-to-end encrypted chat over a Bluetooth + local-network mesh.**

MeshChat lets nearby devices exchange messages with no internet, no server and no account —
messages hop peer-to-peer across the mesh until they reach their recipient, encrypted end-to-end
so relay nodes can never read them.

Built with **.NET MAUI** (Android / iOS / Windows) on a transport-agnostic mesh core.

## How it works

```
┌─────────────┐     BLE / TCP      ┌─────────────┐     BLE / TCP      ┌─────────────┐
│   Alice     │ ──────────────────▶│   Relay      │ ──────────────────▶│   Bob       │
│  (sender)   │   encrypted packet │  (can't read │   encrypted packet │ (recipient) │
└─────────────┘                    │   payload)   │                    └─────────────┘
                                   └─────────────┘
```

- **End-to-end encryption** — ephemeral **ECDH** key agreement + **AES-GCM** authenticated
  encryption per message; relay nodes forward opaque packets and can never decrypt payloads.
- **Mesh routing** — packets carry a TTL and hop across intermediate peers
  (store-and-forward), so two devices out of direct range can still communicate.
- **Multi-transport** — Bluetooth LE for off-grid proximity plus TCP over the local network,
  with **mDNS** peer discovery; the transport orchestrator picks whatever path is available.
- **No infrastructure** — no server, no account, no phone number. Peers are identified by the
  hash of their public key.

## Solution layout

| Project | What it does |
|---|---|
| `MeshChat.Core` | Transport-agnostic domain: `MeshRouter` (routing, TTL, dedup), `CryptoService` (ECDH + AES-GCM), `PeerStore` / `MessageStore` (SQLite), `TransportOrchestrator` |
| `MeshChat.BLE` | Bluetooth LE transport — Android and iOS platform implementations |
| `MeshChat.Network` | Local-network transport — TCP framing + mDNS peer discovery |
| `MeshChat.Relay` | Headless relay node (console) for extending the mesh |
| `MeshChat.MAUI` | Cross-platform UI — chat and peer management |
| `MeshChat.Tests` | xUnit suite: crypto round-trips, routing, store-and-forward, end-to-end scenarios |

## Tests

```bash
dotnet test MeshChat.Tests
```

An additional Python end-to-end harness (`MeshChat.Tests/e2e_test.py`) exercises full
multi-peer scenarios — encryption interop, multi-hop relay delivery and TTL expiry — against
the protocol implementation. `run-tests.sh` / `run-tests.bat` run everything.

The repo also ships a [Claude Code skill](meshchat-e2e/SKILL.md) that automates running and
extending the E2E suite.

## Status

Working prototype. BLE transport is implemented for Android and iOS; the local-network
transport and relay are functional on desktop. Contributions and issue reports are welcome.

## License

[MIT](LICENSE)
