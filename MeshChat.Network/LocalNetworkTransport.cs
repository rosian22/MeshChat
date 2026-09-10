namespace MeshChat.Network;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

/// <summary>
/// Implements TCP socket messaging over a local area network (LAN).
/// Uses mDNS to discover peers and maintains TCP connections to send/receive packets.
/// </summary>
public class LocalNetworkTransport : INetworkTransport
{
    private readonly int _port;
    private readonly MdnsDiscovery _mdnsDiscovery;
    private readonly ConcurrentDictionary<string, TcpClient> _peerConnections;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _listenerCts;
    private bool _isRunning;

    /// <summary>
    /// Gets a value indicating whether this transport is available on the current platform.
    /// </summary>
    public bool IsAvailable => _peerConnections.Count > 0;

    /// <summary>
    /// Raised when a mesh packet is received on this transport.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalNetworkTransport"/> class.
    /// </summary>
    /// <param name="port">The TCP port to listen on for incoming connections.</param>
    /// <param name="peerId">The unique identifier for this peer.</param>
    public LocalNetworkTransport(int port, string peerId)
    {
        _port = port;
        _mdnsDiscovery = new MdnsDiscovery(port, peerId);
        _peerConnections = new ConcurrentDictionary<string, TcpClient>();
        _isRunning = false;

        // Subscribe to mDNS events
        _mdnsDiscovery.PeerDiscovered += OnPeerDiscovered;
        _mdnsDiscovery.PeerLost += OnPeerLost;
    }

    /// <summary>
    /// Starts the TCP listener and mDNS discovery.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _listenerCts = new CancellationTokenSource();

        // Start TCP listener
        _tcpListener = new TcpListener(IPAddress.Any, _port);
        _tcpListener.Start();

        // Start mDNS discovery
        await _mdnsDiscovery.StartAsync();

        // Start accepting connections
        _ = AcceptConnectionsAsync(_listenerCts.Token);
    }

    /// <summary>
    /// Stops the TCP listener and mDNS discovery, closing all peer connections.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        _listenerCts?.Cancel();
        _tcpListener?.Stop();

        // Close all peer connections
        foreach (var connection in _peerConnections.Values)
        {
            connection?.Dispose();
        }
        _peerConnections.Clear();

        await _mdnsDiscovery.StopAsync();
    }

    /// <summary>
    /// Sends a mesh packet to a peer over TCP.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    public async Task SendAsync(MeshPacket packet)
    {
        if (!_peerConnections.TryGetValue(packet.DestinationHash, out var client))
        {
            throw new InvalidOperationException($"No connection to peer {packet.DestinationHash}");
        }

        try
        {
            var json = JsonSerializer.Serialize(packet);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            var stream = client.GetStream();

            // Write 4-byte length prefix
            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            await stream.WriteAsync(lengthBytes, 0, 4);

            // Write JSON payload
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending packet to {packet.DestinationHash}: {ex.Message}");
            _peerConnections.TryRemove(packet.DestinationHash, out _);
            client?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Accepts incoming TCP connections from peers.
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _tcpListener != null)
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error accepting connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles communication with a connected peer.
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[4];

            while (!cancellationToken.IsCancellationRequested)
            {
                // Read 4-byte length prefix
                int bytesRead = await stream.ReadAsync(buffer, 0, 4, cancellationToken);
                if (bytesRead == 0)
                {
                    break; // Connection closed
                }

                int length = BitConverter.ToInt32(buffer, 0);
                var payloadBuffer = new byte[length];

                // Read payload
                bytesRead = 0;
                while (bytesRead < length)
                {
                    int remaining = length - bytesRead;
                    int read = await stream.ReadAsync(payloadBuffer, bytesRead, remaining, cancellationToken);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Connection closed before reading full payload");
                    }
                    bytesRead += read;
                }

                // Deserialize and raise event
                var json = System.Text.Encoding.UTF8.GetString(payloadBuffer);
                var packet = JsonSerializer.Deserialize<MeshPacket>(json);

                if (packet != null)
                {
                    PacketReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling client: {ex.Message}");
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>
    /// Called when mDNS discovers a new peer.
    /// </summary>
    private void OnPeerDiscovered(string host, int port, string peerId)
    {
        // Store the peer info (actual connection happens when needed)
        System.Diagnostics.Debug.WriteLine($"Peer discovered: {peerId} at {host}:{port}");
    }

    /// <summary>
    /// Called when a previously discovered peer is lost.
    /// </summary>
    private void OnPeerLost(string peerId)
    {
        if (_peerConnections.TryRemove(peerId, out var client))
        {
            client?.Dispose();
            System.Diagnostics.Debug.WriteLine($"Peer lost: {peerId}");
        }
    }
}
