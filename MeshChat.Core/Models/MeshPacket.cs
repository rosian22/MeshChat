namespace MeshChat.Core.Models;

/// <summary>
/// Represents a packet transmitted across the mesh network.
/// </summary>
public record MeshPacket
{
    /// <summary>
    /// Unique identifier for the original message.
    /// </summary>
    public Guid MessageId { get; init; }

    /// <summary>
    /// Time-to-live counter. Decremented at each hop.
    /// </summary>
    public int Ttl { get; init; }

    /// <summary>
    /// Hash of the destination peer's public key.
    /// </summary>
    public string DestinationHash { get; init; } = string.Empty;

    /// <summary>
    /// Hash of the sender's public key.
    /// </summary>
    public string SenderHash { get; init; } = string.Empty;

    /// <summary>
    /// Encrypted payload (encrypted with recipient's public key).
    /// </summary>
    public byte[] EncryptedPayload { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Unix timestamp in milliseconds when the packet was created.
    /// </summary>
    public long Timestamp { get; init; }
}
