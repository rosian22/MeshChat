namespace MeshChat.Core.Models;

/// <summary>
/// Represents the transport type used for a peer connection.
/// </summary>
public enum TransportType
{
    /// <summary>Bluetooth Low Energy transport.</summary>
    BLE,

    /// <summary>WiFi Direct transport.</summary>
    WiFi,

    /// <summary>Relay server transport.</summary>
    Relay
}

/// <summary>
/// Represents a peer in the mesh network.
/// </summary>
public record Peer
{
    /// <summary>
    /// Hash of the peer's public key (serves as unique identifier).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable display name for the peer.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The peer's public key bytes.
    /// </summary>
    public byte[] PublicKey { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Timestamp of the last time this peer was seen.
    /// </summary>
    public DateTime LastSeen { get; init; }

    /// <summary>
    /// The transport type used for the last contact with this peer.
    /// </summary>
    public TransportType LastSeenVia { get; init; }
}
