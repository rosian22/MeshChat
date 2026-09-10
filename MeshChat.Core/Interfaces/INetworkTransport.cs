namespace MeshChat.Core.Interfaces;

using MeshChat.Core.Models;

/// <summary>
/// Common interface for all network transport types (BLE, WiFi, Relay).
/// </summary>
public interface INetworkTransport
{
    /// <summary>
    /// Gets a value indicating whether this transport is available on the current platform.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Sends a mesh packet over this transport.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    Task SendAsync(MeshPacket packet);

    /// <summary>
    /// Event raised when a mesh packet is received on this transport.
    /// </summary>
    event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Starts this transport (e.g., enables scanning, opens connections).
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops this transport gracefully.
    /// </summary>
    Task StopAsync();
}
