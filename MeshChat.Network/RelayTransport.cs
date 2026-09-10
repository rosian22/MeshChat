namespace MeshChat.Network;

using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// Implements cloud relay transport using SignalR.
/// Connects to a relay server to route packets when direct peer connections are unavailable.
/// </summary>
public class RelayTransport : INetworkTransport
{
    private readonly string _relayUrl;
    private readonly string _userId;
    private HubConnection? _hubConnection;
    private bool _isRunning;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 5;

    /// <summary>
    /// Gets a value indicating whether this transport is available on the current platform.
    /// </summary>
    public bool IsAvailable => _hubConnection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Raised when a mesh packet is received via the relay.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayTransport"/> class.
    /// </summary>
    /// <param name="relayUrl">The URL of the relay server (e.g., https://relay.example.com).</param>
    /// <param name="userId">The user's unique identifier (typically a public key hash).</param>
    public RelayTransport(string relayUrl, string userId)
    {
        _relayUrl = relayUrl.TrimEnd('/');
        _userId = userId;
        _isRunning = false;
        _reconnectAttempts = 0;
    }

    /// <summary>
    /// Starts the relay connection with auto-reconnect support.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _reconnectAttempts = 0;

        // Build hub connection URL with userId query parameter
        var hubUrl = $"{_relayUrl}/mesh?userId={Uri.EscapeDataString(_userId)}";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(
                new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        // Register handlers
        _hubConnection.On<MeshPacket>("ReceivePacket", OnPacketReceived);
        _hubConnection.Reconnecting += OnReconnecting;
        _hubConnection.Reconnected += OnReconnected;
        _hubConnection.Closed += OnClosed;

        await ConnectAsync();
    }

    /// <summary>
    /// Stops the relay connection.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        if (_hubConnection != null)
        {
            _hubConnection.Reconnecting -= OnReconnecting;
            _hubConnection.Reconnected -= OnReconnected;
            _hubConnection.Closed -= OnClosed;

            if (_hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.StopAsync();
            }

            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    /// <summary>
    /// Sends a mesh packet via the relay server.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    public async Task SendAsync(MeshPacket packet)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Relay connection is not established");
        }

        try
        {
            await _hubConnection.InvokeAsync("SendPacket", packet);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending packet via relay: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Attempts to establish a connection to the relay server.
    /// </summary>
    private async Task ConnectAsync()
    {
        if (_hubConnection == null)
        {
            return;
        }

        try
        {
            await _hubConnection.StartAsync();
            _reconnectAttempts = 0;
            System.Diagnostics.Debug.WriteLine("Connected to relay server");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error connecting to relay: {ex.Message}");
            if (_isRunning)
            {
                // Retry with exponential backoff
                var delay = Math.Min(1000 * (int)Math.Pow(2, _reconnectAttempts), 30000);
                await Task.Delay(delay);
                await ConnectAsync();
            }
        }
    }

    /// <summary>
    /// Called when a packet is received from the relay server.
    /// </summary>
    private void OnPacketReceived(MeshPacket packet)
    {
        PacketReceived?.Invoke(packet);
    }

    /// <summary>
    /// Called when the connection is attempting to reconnect.
    /// </summary>
    private Task OnReconnecting(Exception? exception)
    {
        _reconnectAttempts++;
        System.Diagnostics.Debug.WriteLine($"Relay reconnecting (attempt {_reconnectAttempts}): {exception?.Message}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the connection has been re-established.
    /// </summary>
    private Task OnReconnected(string? connectionId)
    {
        _reconnectAttempts = 0;
        System.Diagnostics.Debug.WriteLine("Relay reconnected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the connection is closed.
    /// </summary>
    private Task OnClosed(Exception? exception)
    {
        System.Diagnostics.Debug.WriteLine($"Relay connection closed: {exception?.Message}");
        return Task.CompletedTask;
    }
}
