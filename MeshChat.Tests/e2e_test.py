#!/usr/bin/env python3
"""
MeshChat E2E Integration Tests — Python Port
=============================================
Mirrors every C# E2E test scenario using the same crypto (ECDH P-256 + AES-256-GCM),
routing logic, dedup, TTL enforcement, and transport fallback.

This validates the MeshChat mesh networking design end-to-end without needing
.NET SDK or Bluetooth hardware.
"""

import os
import sys
import uuid
import time
import struct
import hashlib
import base64
import traceback
from dataclasses import dataclass, field
from typing import Optional, Callable
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.kdf.hkdf import HKDF
from cryptography.hazmat.primitives.ciphers.aead import AESGCM


# ─── Crypto Service ──────────────────────────────────────────────────

class CryptoService:
    """ECDH P-256 key exchange + AES-256-GCM encryption. Mirrors C# CryptoService."""

    def __init__(self):
        self._private_key: Optional[ec.EllipticCurvePrivateKey] = None
        self._public_key_bytes: Optional[bytes] = None

    def generate_identity(self) -> tuple[bytes, bytes]:
        """Generate ECDH P-256 keypair. Returns (public_key_spki, private_key_pkcs8)."""
        self._private_key = ec.generate_private_key(ec.SECP256R1())
        pub_bytes = self._private_key.public_key().public_bytes(
            serialization.Encoding.DER,
            serialization.PublicFormat.SubjectPublicKeyInfo
        )
        priv_bytes = self._private_key.private_bytes(
            serialization.Encoding.DER,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption()
        )
        self._public_key_bytes = pub_bytes
        return pub_bytes, priv_bytes

    def encrypt(self, plaintext: str, recipient_public_key: bytes) -> bytes:
        """
        Encrypt with ephemeral ECDH + AES-256-GCM.
        Output: [2-byte epk_len][ephemeral_public_key][12-byte nonce][16-byte tag][ciphertext]
        """
        # Generate ephemeral key pair
        ephemeral_key = ec.generate_private_key(ec.SECP256R1())
        ephemeral_pub_bytes = ephemeral_key.public_key().public_bytes(
            serialization.Encoding.DER,
            serialization.PublicFormat.SubjectPublicKeyInfo
        )

        # Load recipient public key
        recipient_key = serialization.load_der_public_key(recipient_public_key)

        # Derive shared secret
        shared_key = ephemeral_key.exchange(ec.ECDH(), recipient_key)
        aes_key = HKDF(
            algorithm=hashes.SHA256(),
            length=32,
            salt=None,
            info=b"meshchat-ecdh",
        ).derive(shared_key)

        # AES-256-GCM encrypt
        nonce = os.urandom(12)
        aesgcm = AESGCM(aes_key)
        ct_and_tag = aesgcm.encrypt(nonce, plaintext.encode("utf-8"), None)
        # cryptography lib appends tag to ciphertext
        ciphertext = ct_and_tag[:-16]
        tag = ct_and_tag[-16:]

        # Pack: [2-byte epk len][epk][nonce][tag][ciphertext]
        epk_len = len(ephemeral_pub_bytes)
        result = struct.pack("<H", epk_len)
        result += ephemeral_pub_bytes + nonce + tag + ciphertext
        return result

    def decrypt(self, encrypted_payload: bytes) -> str:
        """Decrypt a payload encrypted by encrypt()."""
        if self._private_key is None:
            raise RuntimeError("Private key not set. Call generate_identity first.")

        # Unpack ephemeral public key
        epk_len = struct.unpack("<H", encrypted_payload[:2])[0]
        offset = 2
        epk_bytes = encrypted_payload[offset:offset + epk_len]
        offset += epk_len

        nonce = encrypted_payload[offset:offset + 12]
        offset += 12
        tag = encrypted_payload[offset:offset + 16]
        offset += 16
        ciphertext = encrypted_payload[offset:]

        # Load ephemeral public key and derive shared secret
        ephemeral_pub = serialization.load_der_public_key(epk_bytes)
        shared_key = self._private_key.exchange(ec.ECDH(), ephemeral_pub)
        aes_key = HKDF(
            algorithm=hashes.SHA256(),
            length=32,
            salt=None,
            info=b"meshchat-ecdh",
        ).derive(shared_key)

        # AES-256-GCM decrypt (cryptography lib expects tag appended)
        aesgcm = AESGCM(aes_key)
        plaintext = aesgcm.decrypt(nonce, ciphertext + tag, None)
        return plaintext.decode("utf-8")

    @staticmethod
    def get_public_key_hash(public_key: bytes) -> str:
        """SHA-256 hash of public key, base64url encoded."""
        h = hashlib.sha256(public_key).digest()
        return base64.urlsafe_b64encode(h).rstrip(b"=").decode("ascii")


# ─── Models ───────────────────────────────────────────────────────────

@dataclass
class MeshPacket:
    message_id: str
    ttl: int
    destination_hash: str
    sender_hash: str
    encrypted_payload: bytes
    timestamp: int

@dataclass
class Message:
    id: str
    sender_id: str
    content: str
    sent_at: float
    status: str  # "Sent", "Relayed", "Delivered", "Read"

@dataclass
class Peer:
    id: str
    display_name: str
    public_key: bytes
    last_seen: float = 0.0
    last_seen_via: str = "BLE"


# ─── In-Memory Stores ────────────────────────────────────────────────

class MessageStore:
    def __init__(self):
        self.messages: dict[str, Message] = {}

    def save(self, msg: Message):
        self.messages[msg.id] = msg

    def get_by_id(self, msg_id: str) -> Optional[Message]:
        return self.messages.get(msg_id)

    @property
    def all(self):
        return list(self.messages.values())


class PeerStore:
    def __init__(self):
        self.peers: dict[str, Peer] = {}

    def save(self, peer: Peer):
        self.peers[peer.id] = peer

    def get_by_id(self, peer_id: str) -> Optional[Peer]:
        return self.peers.get(peer_id)

    @property
    def all(self):
        return list(self.peers.values())


# ─── Mesh Router ──────────────────────────────────────────────────────

class MeshRouter:
    """Packet routing with deduplication and TTL. Mirrors C# MeshRouter."""

    MAX_TTL = 5

    def __init__(self, crypto: CryptoService, message_store: MessageStore,
                 peer_store: PeerStore, my_hash: str):
        self.crypto = crypto
        self.message_store = message_store
        self.peer_store = peer_store
        self.my_hash = my_hash
        self._seen_packets: set[str] = set()
        self.on_message_received: Optional[Callable[[Message], None]] = None
        self.on_relay_requested: Optional[Callable[[MeshPacket], None]] = None

    def on_packet_received(self, packet: MeshPacket):
        """Process received packet: dedup, deliver or relay."""
        # Deduplication
        if packet.message_id in self._seen_packets:
            return
        self._seen_packets.add(packet.message_id)

        # Is this for me?
        if packet.destination_hash == self.my_hash:
            sender_peer = self.peer_store.get_by_id(packet.sender_hash)
            if sender_peer is not None:
                try:
                    plaintext = self.crypto.decrypt(packet.encrypted_payload)
                    msg = Message(
                        id=packet.message_id,
                        sender_id=packet.sender_hash,
                        content=plaintext,
                        sent_at=packet.timestamp / 1000.0,
                        status="Delivered"
                    )
                    self.message_store.save(msg)
                    if self.on_message_received:
                        self.on_message_received(msg)
                except Exception:
                    pass  # Decryption failed — drop packet
        else:
            # Relay if TTL > 0
            if packet.ttl > 0:
                relayed = MeshPacket(
                    message_id=packet.message_id,
                    ttl=packet.ttl - 1,
                    destination_hash=packet.destination_hash,
                    sender_hash=packet.sender_hash,
                    encrypted_payload=packet.encrypted_payload,
                    timestamp=packet.timestamp
                )
                if self.on_relay_requested:
                    self.on_relay_requested(relayed)

    def create_packet(self, message_id: str, dest_hash: str,
                      plaintext: str, recipient_public_key: bytes) -> MeshPacket:
        """Create an encrypted mesh packet."""
        encrypted = self.crypto.encrypt(plaintext, recipient_public_key)
        return MeshPacket(
            message_id=message_id,
            ttl=self.MAX_TTL,
            destination_hash=dest_hash,
            sender_hash=self.my_hash,
            encrypted_payload=encrypted,
            timestamp=int(time.time() * 1000)
        )


# ─── In-Memory Transport ─────────────────────────────────────────────

class InMemoryTransport:
    """Simulates a BLE/WiFi link between two peers."""

    def __init__(self):
        self.remote: Optional["InMemoryTransport"] = None
        self.is_running = False
        self.simulate_failure = False
        self.on_packet_received: Optional[Callable[[MeshPacket], None]] = None

    @property
    def is_available(self) -> bool:
        return self.is_running and self.remote is not None and not self.simulate_failure

    def connect_to(self, remote: "InMemoryTransport"):
        self.remote = remote
        remote.remote = self

    def start(self):
        self.is_running = True

    def stop(self):
        self.is_running = False

    def send(self, packet: MeshPacket):
        if not self.is_available:
            raise RuntimeError("Transport not available")
        self.remote._deliver_locally(packet)

    def _deliver_locally(self, packet: MeshPacket):
        if self.on_packet_received:
            self.on_packet_received(packet)


# ─── Transport Orchestrator ──────────────────────────────────────────

class TransportOrchestrator:
    """BLE -> WiFi -> Relay fallback chain. Mirrors C# TransportOrchestrator."""

    def __init__(self, ble: InMemoryTransport,
                 wifi: Optional[InMemoryTransport] = None,
                 relay: Optional[InMemoryTransport] = None):
        self.ble = ble
        self.wifi = wifi
        self.relay = relay

    def send_packet(self, packet: MeshPacket):
        """Try BLE, then WiFi, then relay."""
        # Try BLE
        if self.ble.is_available:
            try:
                self.ble.send(packet)
                return
            except Exception:
                pass

        # Try WiFi
        if self.wifi and self.wifi.is_available:
            try:
                self.wifi.send(packet)
                return
            except Exception:
                pass

        # Try relay
        if self.relay and self.relay.is_available:
            try:
                self.relay.send(packet)
                return
            except Exception:
                pass

        raise RuntimeError("No transports available")


# ─── Peer Node (test helper) ─────────────────────────────────────────

@dataclass
class PeerNode:
    name: str
    hash: str
    public_key: bytes
    crypto: CryptoService
    router: MeshRouter
    orchestrator: TransportOrchestrator
    message_store: MessageStore
    peer_store: PeerStore
    ble_transport: InMemoryTransport


def create_peer(name: str) -> PeerNode:
    """Creates a fully wired peer with real crypto and in-memory transport."""
    crypto = CryptoService()
    pub_key, _ = crypto.generate_identity()
    my_hash = CryptoService.get_public_key_hash(pub_key)

    msg_store = MessageStore()
    peer_store = PeerStore()
    router = MeshRouter(crypto, msg_store, peer_store, my_hash)

    ble = InMemoryTransport()
    orchestrator = TransportOrchestrator(ble)

    return PeerNode(
        name=name, hash=my_hash, public_key=pub_key,
        crypto=crypto, router=router, orchestrator=orchestrator,
        message_store=msg_store, peer_store=peer_store,
        ble_transport=ble
    )


def wire_router(peer: PeerNode):
    """Wire transport -> router -> relay."""
    peer.ble_transport.on_packet_received = lambda pkt: peer.router.on_packet_received(pkt)
    peer.router.on_relay_requested = lambda pkt: (
        peer.orchestrator.send_packet(pkt) if peer.ble_transport.is_available else None
    )


def register_peers(a: PeerNode, b: PeerNode):
    """Register two peers with each other."""
    a.peer_store.save(Peer(id=b.hash, display_name=b.name, public_key=b.public_key,
                           last_seen=time.time(), last_seen_via="BLE"))
    b.peer_store.save(Peer(id=a.hash, display_name=a.name, public_key=a.public_key,
                           last_seen=time.time(), last_seen_via="BLE"))


# ═══════════════════════════════════════════════════════════════════════
#  TEST SUITE
# ═══════════════════════════════════════════════════════════════════════

class TestResults:
    def __init__(self):
        self.passed = 0
        self.failed = 0
        self.errors: list[str] = []

    def record(self, name: str, success: bool, error: str = ""):
        if success:
            self.passed += 1
            print(f"  \033[92mPASS\033[0m  {name}")
        else:
            self.failed += 1
            self.errors.append(f"{name}: {error}")
            print(f"  \033[91mFAIL\033[0m  {name}")
            print(f"         {error}")


results = TestResults()


def assert_eq(actual, expected, msg=""):
    if actual != expected:
        raise AssertionError(f"{msg}: expected {expected!r}, got {actual!r}")

def assert_true(val, msg=""):
    if not val:
        raise AssertionError(f"{msg}: expected True, got {val!r}")


# ─── TEST 1: Two peers send and receive encrypted message ────────────

def test_two_peers_send_receive():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()
    register_peers(alice, bob)
    wire_router(bob)

    received = []
    bob.router.on_message_received = lambda msg: received.append(msg)

    msg_id = str(uuid.uuid4())
    packet = alice.router.create_packet(msg_id, bob.hash, "Hello Bob, this is encrypted!", bob.public_key)
    alice.orchestrator.send_packet(packet)

    assert_eq(len(received), 1, "Bob should receive 1 message")
    assert_eq(received[0].content, "Hello Bob, this is encrypted!", "Decrypted content")
    assert_eq(received[0].sender_id, alice.hash, "Sender ID")
    assert_eq(received[0].status, "Delivered", "Status")
    assert_eq(received[0].id, msg_id, "Message ID")

    stored = bob.message_store.get_by_id(msg_id)
    assert_true(stored is not None, "Message stored in Bob's store")
    assert_eq(stored.content, "Hello Bob, this is encrypted!", "Stored content")


# ─── TEST 2: Bidirectional conversation ──────────────────────────────

def test_bidirectional_conversation():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()
    register_peers(alice, bob)
    wire_router(alice)
    wire_router(bob)

    alice_msgs, bob_msgs = [], []
    alice.router.on_message_received = lambda m: alice_msgs.append(m)
    bob.router.on_message_received = lambda m: bob_msgs.append(m)

    # Alice -> Bob
    p1 = alice.router.create_packet(str(uuid.uuid4()), bob.hash, "Hey Bob!", bob.public_key)
    alice.orchestrator.send_packet(p1)

    # Bob -> Alice
    p2 = bob.router.create_packet(str(uuid.uuid4()), alice.hash, "Hi Alice!", alice.public_key)
    bob.orchestrator.send_packet(p2)

    # Alice -> Bob again
    p3 = alice.router.create_packet(str(uuid.uuid4()), bob.hash, "How's the mesh?", bob.public_key)
    alice.orchestrator.send_packet(p3)

    assert_eq(len(bob_msgs), 2, "Bob received 2 messages")
    assert_eq(bob_msgs[0].content, "Hey Bob!", "Bob msg 1")
    assert_eq(bob_msgs[1].content, "How's the mesh?", "Bob msg 2")

    assert_eq(len(alice_msgs), 1, "Alice received 1 message")
    assert_eq(alice_msgs[0].content, "Hi Alice!", "Alice msg 1")


# ─── TEST 3: Multi-hop relay through middle peer ─────────────────────

def test_three_peer_relay():
    # Topology: Alice <-> Charlie <-> Bob (Alice and Bob NOT directly connected)
    alice = create_peer("Alice")
    charlie = create_peer("Charlie")
    bob = create_peer("Bob")

    # Alice <-> Charlie link
    ac_alice = InMemoryTransport()
    ac_charlie = InMemoryTransport()
    ac_alice.connect_to(ac_charlie)
    ac_alice.start()
    ac_charlie.start()

    # Charlie <-> Bob link
    cb_charlie = InMemoryTransport()
    cb_bob = InMemoryTransport()
    cb_charlie.connect_to(cb_bob)
    cb_charlie.start()
    cb_bob.start()

    # Alice's orchestrator uses Alice<->Charlie link
    alice.orchestrator = TransportOrchestrator(ac_alice)

    # Charlie: receive from Alice side, relay on Bob side (and vice versa)
    def charlie_receive(pkt):
        charlie.router.on_packet_received(pkt)

    ac_charlie.on_packet_received = charlie_receive
    cb_bob.on_packet_received = lambda pkt: bob.router.on_packet_received(pkt)

    # Charlie relays to both links
    def charlie_relay(pkt):
        try:
            cb_charlie.send(pkt)
        except Exception:
            pass
        try:
            ac_charlie.remote.send(pkt) if ac_charlie.remote else None
        except Exception:
            pass

    charlie.router.on_relay_requested = charlie_relay

    # Alice knows Bob's public key; Bob knows Alice's
    alice.peer_store.save(Peer(id=bob.hash, display_name="Bob", public_key=bob.public_key))
    bob.peer_store.save(Peer(id=alice.hash, display_name="Alice", public_key=alice.public_key))

    received = []
    bob.router.on_message_received = lambda m: received.append(m)

    msg_id = str(uuid.uuid4())
    packet = alice.router.create_packet(msg_id, bob.hash, "Relayed through Charlie!", bob.public_key)
    alice.orchestrator.send_packet(packet)

    assert_eq(len(received), 1, "Bob received relayed message")
    assert_eq(received[0].content, "Relayed through Charlie!", "Relayed content")
    assert_eq(received[0].sender_id, alice.hash, "Original sender preserved")

    # Charlie should NOT have stored it (not for him)
    assert_eq(len(charlie.message_store.all), 0, "Charlie didn't store message")

    # Bob should have it stored
    assert_true(bob.message_store.get_by_id(msg_id) is not None, "Bob stored message")


# ─── TEST 4: Deduplication prevents relay loop ───────────────────────

def test_deduplication():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()
    register_peers(alice, bob)
    wire_router(bob)

    receive_count = [0]
    bob.router.on_message_received = lambda _: receive_count.__setitem__(0, receive_count[0] + 1)

    packet = alice.router.create_packet(str(uuid.uuid4()), bob.hash, "No dupes!", bob.public_key)
    alice.orchestrator.send_packet(packet)

    # Deliver same packet again (simulating loop)
    bob.ble_transport._deliver_locally(packet)

    assert_eq(receive_count[0], 1, "Duplicate dropped by dedup")


# ─── TEST 5: TTL expiry stops relay ──────────────────────────────────

def test_ttl_expiry():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()

    relay_count = [0]
    bob.router.on_relay_requested = lambda _: relay_count.__setitem__(0, relay_count[0] + 1)
    wire_router(bob)
    # Override relay handler after wire_router
    bob.router.on_relay_requested = lambda _: relay_count.__setitem__(0, relay_count[0] + 1)

    packet = MeshPacket(
        message_id=str(uuid.uuid4()),
        ttl=0,  # Expired!
        destination_hash="unknown-peer",
        sender_hash=alice.hash,
        encrypted_payload=b"\x01\x02\x03",
        timestamp=int(time.time() * 1000)
    )

    alice.orchestrator.send_packet(packet)

    assert_eq(relay_count[0], 0, "TTL=0 packet not relayed")


# ─── TEST 6: Transport fallback BLE -> WiFi ──────────────────────────

def test_transport_fallback():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    # BLE link (will be disabled)
    ble_a = InMemoryTransport()
    ble_b = InMemoryTransport()
    ble_a.connect_to(ble_b)
    ble_a.start()
    ble_b.start()

    # WiFi link (will be used as fallback)
    wifi_a = InMemoryTransport()
    wifi_b = InMemoryTransport()
    wifi_a.connect_to(wifi_b)
    wifi_a.start()
    wifi_b.start()

    # Alice's orchestrator with both transports
    alice.orchestrator = TransportOrchestrator(ble_a, wifi_a)

    # Wire Bob's WiFi to his router
    wifi_b.on_packet_received = lambda pkt: bob.router.on_packet_received(pkt)
    bob.peer_store.save(Peer(id=alice.hash, display_name="Alice", public_key=alice.public_key))

    received = []
    bob.router.on_message_received = lambda m: received.append(m)

    # Simulate BLE failure
    ble_a.simulate_failure = True

    packet = alice.router.create_packet(str(uuid.uuid4()), bob.hash, "Via WiFi fallback!", bob.public_key)
    alice.orchestrator.send_packet(packet)

    assert_eq(len(received), 1, "Message received via WiFi fallback")
    assert_eq(received[0].content, "Via WiFi fallback!", "Fallback content")


# ─── TEST 7: Tampered payload rejected ───────────────────────────────

def test_tampered_payload_rejected():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()
    register_peers(alice, bob)
    wire_router(bob)

    receive_count = [0]
    bob.router.on_message_received = lambda _: receive_count.__setitem__(0, receive_count[0] + 1)

    packet = alice.router.create_packet(str(uuid.uuid4()), bob.hash, "Secret", bob.public_key)

    # Tamper with payload
    payload = bytearray(packet.encrypted_payload)
    payload[-1] ^= 0xFF
    payload[-2] ^= 0xAA
    tampered = MeshPacket(
        message_id=packet.message_id,
        ttl=packet.ttl,
        destination_hash=packet.destination_hash,
        sender_hash=packet.sender_hash,
        encrypted_payload=bytes(payload),
        timestamp=packet.timestamp
    )

    alice.orchestrator.send_packet(tampered)

    assert_eq(receive_count[0], 0, "Tampered payload rejected by AES-GCM auth")


# ─── TEST 8: Multiple messages all delivered in order ─────────────────

def test_multiple_messages_in_order():
    alice = create_peer("Alice")
    bob = create_peer("Bob")

    alice.ble_transport.connect_to(bob.ble_transport)
    alice.ble_transport.start()
    bob.ble_transport.start()
    register_peers(alice, bob)
    wire_router(bob)

    bob_msgs = []
    bob.router.on_message_received = lambda m: bob_msgs.append(m.content)

    for i in range(10):
        p = alice.router.create_packet(str(uuid.uuid4()), bob.hash, f"Message #{i}", bob.public_key)
        alice.orchestrator.send_packet(p)

    assert_eq(len(bob_msgs), 10, "All 10 messages received")
    for i in range(10):
        assert_eq(bob_msgs[i], f"Message #{i}", f"Message #{i} content")

    assert_eq(len(bob.message_store.all), 10, "All 10 messages stored")


# ─── BONUS TEST 9: Crypto key generation uniqueness ──────────────────

def test_crypto_key_uniqueness():
    c1 = CryptoService()
    pub1, priv1 = c1.generate_identity()

    c2 = CryptoService()
    pub2, priv2 = c2.generate_identity()

    assert_true(pub1 != pub2, "Public keys are unique")
    assert_true(priv1 != priv2, "Private keys are unique")

    hash1 = CryptoService.get_public_key_hash(pub1)
    hash2 = CryptoService.get_public_key_hash(pub2)
    assert_true(hash1 != hash2, "Key hashes are unique")

    # Base64url: no +, /, or = characters
    assert_true("+" not in hash1, "No + in base64url")
    assert_true("/" not in hash1, "No / in base64url")
    assert_true("=" not in hash1, "No = in base64url")


# ─── BONUS TEST 10: Encrypt produces different ciphertext each time ──

def test_encrypt_nonce_uniqueness():
    alice = CryptoService()
    alice.generate_identity()

    bob = CryptoService()
    bob_pub, _ = bob.generate_identity()

    ct1 = alice.encrypt("Same message", bob_pub)
    ct2 = alice.encrypt("Same message", bob_pub)

    assert_true(ct1 != ct2, "Different ciphertexts due to ephemeral keys + nonces")


# ─── BONUS TEST 11: Unicode/emoji roundtrip ──────────────────────────

def test_unicode_roundtrip():
    alice = CryptoService()
    alice.generate_identity()

    bob = CryptoService()
    bob_pub, _ = bob.generate_identity()

    message = "Hello 世界 مرحبا мир 🌍🚀"
    encrypted = alice.encrypt(message, bob_pub)
    decrypted = bob.decrypt(encrypted)

    assert_eq(decrypted, message, "Unicode roundtrip")


# ═══════════════════════════════════════════════════════════════════════
#  RUNNER
# ═══════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    tests = [
        ("E2E: Two peers send/receive encrypted message", test_two_peers_send_receive),
        ("E2E: Bidirectional conversation", test_bidirectional_conversation),
        ("E2E: Three-peer relay through middle node", test_three_peer_relay),
        ("E2E: Deduplication prevents relay loop", test_deduplication),
        ("E2E: TTL expiry stops relay", test_ttl_expiry),
        ("E2E: Transport fallback BLE -> WiFi", test_transport_fallback),
        ("E2E: Tampered payload rejected by AES-GCM", test_tampered_payload_rejected),
        ("E2E: Multiple messages delivered in order", test_multiple_messages_in_order),
        ("Unit: Crypto key generation uniqueness", test_crypto_key_uniqueness),
        ("Unit: Encrypt nonce uniqueness", test_encrypt_nonce_uniqueness),
        ("Unit: Unicode/emoji encrypt-decrypt roundtrip", test_unicode_roundtrip),
    ]

    print()
    print("=" * 60)
    print("  MeshChat E2E Integration Tests (Python)")
    print("=" * 60)
    print()

    for name, test_fn in tests:
        try:
            test_fn()
            results.record(name, True)
        except AssertionError as e:
            results.record(name, False, str(e))
        except Exception as e:
            results.record(name, False, f"EXCEPTION: {e}")
            traceback.print_exc()

    print()
    print("=" * 60)
    total = results.passed + results.failed
    if results.failed == 0:
        print(f"  \033[92mALL {total} TESTS PASSED\033[0m")
    else:
        print(f"  \033[91m{results.failed} FAILED\033[0m, {results.passed} passed, {total} total")
        print()
        for err in results.errors:
            print(f"  > {err}")
    print("=" * 60)
    print()

    sys.exit(1 if results.failed > 0 else 0)
