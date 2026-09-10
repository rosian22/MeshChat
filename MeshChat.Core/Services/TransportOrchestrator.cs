namespace MeshChat.Core.Services;

using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// Orchestrates network transport fallback chain: BLE -> WiFi -> Relay.
/// </summary>
public class TransportOrchestrator
{
    private readonly IBleService _bleService;
    private readonly INetworkTransport _localTransport;
    private readonly INetworkTransport _relayTransport;

    /// <summary>
    /// Event raised when a packet is received from any transport.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Initializes a new instance of the TransportOrchestrator class.
    /// </summary>
    /// <param name="bleService">The BLE service for short-range communication.</param>
    /// <param name="localTransport">The local network transport (e.g., WiFi Direct).</param>
    /// <param name="relayTransport">The relay/fallback transport (e.g., cloud relay).</param>
    public TransportOrchestrator(IBleService bleService, INetworkTransport localTransport, INetworkTransport relayTransport)
    {
        _bleService = bleService ?? throw new ArgumentNullException(nameof(bleService));
        _localTransport = localTransport ?? throw new ArgumentNullException(nameof(localTransport));
        _relayTransport = relayTransport ?? throw new ArgumentNullException(nameof(relayTransport));
    }

    /// <summary>
    /// Wires up packet received events from all transports to a unified event.
    /// </summary>
    public void WireUp()
    {
        _bleService.PacketReceived += OnTransportPacketReceived;
        _localTransport.PacketReceived += OnTransportPacketReceived;
        _relayTransport.PacketReceived += OnTransportPacketReceived;
    }

    /// <summary>
    /// Tears down packet received event handlers from all transports.
    /// </summary>
    public void Unwire()
    {
        _bleService.PacketReceived -= OnTransportPacketReceived;
        _localTransport.PacketReceived -= OnTransportPacketReceived;
        _relayTransport.PacketReceived -= OnTransportPacketReceived;
    }

    /// <summary>
    /// Sends a packet using the fallback chain: tries BLE, then local, then relay.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    public async Task SendPacket(MeshPacket packet)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));

        // Try BLE first
        if (_bleService.IsAvailable)
        {
            try
            {
                await _bleService.SendAsync(packet);
                return;
            }
            catch (Exception)
            {
                // BLE failed, fall through to next transport
            }
        }

        // Try local transport second
        if (_localTransport.IsAvailable)
        {
            try
            {
                await _localTransport.SendAsync(packet);
                return;
            }
            catch (Exception)
            {
                // Local transport failed, fall through to relay
            }
        }

        // Try relay transport last
        if (_relayTransport.IsAvailable)
        {
            try
            {
                await _relayTransport.SendAsync(packet);
                return;
            }
            catch (Exception)
            {
                // All transports failed
                throw new InvalidOperationException("All transport mechanisms failed to send packet.");
            }
        }

        // No transports available
        throw new InvalidOperationException("No transports are available.");
    }

    /// <summary>
    /// Starts all configured transports.
    /// </summary>
    public async Task StartAll()
    {
        var tasks = new List<Task>();

        if (_bleService.IsAvailable)
        {
            tasks.Add(_bleService.StartAsync());
        }

        if (_localTransport.IsAvailable)
        {
            tasks.Add(_localTransport.StartAsync());
        }

        if (_relayTransport.IsAvailable)
        {
            tasks.Add(_relayTransport.StartAsync());
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Stops all configured transports gracefully.
    /// </summary>
    public async Task StopAll()
    {
        var tasks = new List<Task>();

        if (_bleService.IsAvailable)
        {
            tasks.Add(_bleService.StopAsync());
        }

        if (_localTransport.IsAvailable)
        {
            tasks.Add(_localTransport.StopAsync());
        }

        if (_relayTransport.IsAvailable)
        {
            tasks.Add(_relayTransport.StopAsync());
        }

        await Task.WhenAll(tasks);
    }

    private void OnTransportPacketReceived(MeshPacket packet)
    {
        PacketReceived?.Invoke(packet);
    }
}
