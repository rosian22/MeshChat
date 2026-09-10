namespace MeshChat.Core.Services;

using System.Collections.Concurrent;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// Routes mesh packets, handles deduplication, relaying, and message delivery.
/// </summary>
public class MeshRouter
{
    private readonly ICryptoService _cryptoService;
    private readonly IMessageStore _messageStore;
    private readonly IPeerStore _peerStore;
    private readonly string _myHash;

    private readonly ConcurrentDictionary<Guid, long> _seenPackets = new();
    private Timer? _deduplicationCleanupTimer;

    private const int PacketTtlMax = 5;
    private const int DuplicateWindowMs = 300000; // 5 minutes

    /// <summary>
    /// Event raised when a message is received for this peer.
    /// </summary>
    public event Action<Message>? MessageReceived;

    /// <summary>
    /// Event raised when a packet needs to be relayed to other peers.
    /// </summary>
    public event Action<MeshPacket>? RelayRequested;

    /// <summary>
    /// Initializes a new instance of the MeshRouter class.
    /// </summary>
    /// <param name="cryptoService">The cryptographic service.</param>
    /// <param name="messageStore">The message persistence store.</param>
    /// <param name="peerStore">The peer information store.</param>
    /// <param name="myHash">The hash of this peer's public key (its identity).</param>
    public MeshRouter(ICryptoService cryptoService, IMessageStore messageStore, IPeerStore peerStore, string myHash)
    {
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _messageStore = messageStore ?? throw new ArgumentNullException(nameof(messageStore));
        _peerStore = peerStore ?? throw new ArgumentNullException(nameof(peerStore));
        _myHash = myHash ?? throw new ArgumentException("My hash cannot be null or empty.", nameof(myHash));

        StartDeduplicationCleanup();
    }

    /// <summary>
    /// Processes a received packet for deduplication, delivery, and relaying.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    public async Task OnPacketReceived(MeshPacket packet)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));

        // Check for duplicate
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_seenPackets.ContainsKey(packet.MessageId))
        {
            // Already seen, drop it
            return;
        }

        // Record this packet as seen
        _seenPackets[packet.MessageId] = now;

        // Check if packet is for us
        if (packet.DestinationHash == _myHash)
        {
            // Packet is destined for us
            var senderPeer = await _peerStore.GetById(packet.SenderHash);
            if (senderPeer != null)
            {
                try
                {
                    var plaintext = _cryptoService.Decrypt(packet, senderPeer.PublicKey);
                    var message = new Message
                    {
                        Id = packet.MessageId,
                        SenderId = packet.SenderHash,
                        Content = plaintext,
                        SentAt = UnixTimeStampToDateTime(packet.Timestamp),
                        Status = MessageStatus.Delivered
                    };

                    await _messageStore.Save(message);
                    MessageReceived?.Invoke(message);
                }
                catch (Exception)
                {
                    // Decryption failed, ignore the packet
                }
            }
        }
        else
        {
            // Packet is not for us, relay it if TTL > 0
            if (packet.Ttl > 0)
            {
                var relayPacket = packet with { Ttl = packet.Ttl - 1 };
                RelayRequested?.Invoke(relayPacket);
            }
        }
    }

    /// <summary>
    /// Creates a mesh packet with encrypted payload.
    /// </summary>
    /// <param name="messageId">The message ID.</param>
    /// <param name="destinationHash">The hash of the destination peer.</param>
    /// <param name="plaintext">The message content to encrypt.</param>
    /// <param name="recipientPublicKey">The recipient's public key for encryption.</param>
    /// <returns>A ready-to-send MeshPacket.</returns>
    public MeshPacket CreatePacket(Guid messageId, string destinationHash, string plaintext, byte[] recipientPublicKey)
    {
        if (string.IsNullOrEmpty(destinationHash))
            throw new ArgumentException("Destination hash cannot be null or empty.", nameof(destinationHash));
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));
        if (recipientPublicKey == null || recipientPublicKey.Length == 0)
            throw new ArgumentException("Recipient public key cannot be null or empty.", nameof(recipientPublicKey));

        var encryptedPayload = _cryptoService.Encrypt(plaintext, recipientPublicKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return new MeshPacket
        {
            MessageId = messageId,
            Ttl = PacketTtlMax,
            DestinationHash = destinationHash,
            SenderHash = _myHash,
            EncryptedPayload = encryptedPayload,
            Timestamp = now
        };
    }

    /// <summary>
    /// Starts the deduplication cleanup timer.
    /// </summary>
    private void StartDeduplicationCleanup()
    {
        _deduplicationCleanupTimer = new Timer(CleanupSeenPackets, null, DuplicateWindowMs, DuplicateWindowMs);
    }

    /// <summary>
    /// Stops the deduplication cleanup timer.
    /// </summary>
    public void Stop()
    {
        _deduplicationCleanupTimer?.Dispose();
    }

    private void CleanupSeenPackets(object? state)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoff = now - DuplicateWindowMs;

        var oldPackets = _seenPackets.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var packetId in oldPackets)
        {
            _seenPackets.TryRemove(packetId, out _);
        }
    }

    private static DateTime UnixTimeStampToDateTime(long timestamp)
    {
        var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddMilliseconds(timestamp);
        return dateTime;
    }
}
