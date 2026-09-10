using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;

namespace MeshChat.Tests.Mocks;

/// <summary>
/// In-memory transport that simulates a network link between two peers.
/// When Alice sends a packet, it appears in Bob's receive queue and vice versa.
/// Wire two of these together via <see cref="ConnectTo"/> to simulate a BLE/WiFi link.
/// </summary>
public class InMemoryTransport : INetworkTransport
{
    private InMemoryTransport? _remoteEnd;
    private bool _isRunning;
    private bool _simulateFailure;

    /// <inheritdoc />
    public bool IsAvailable => _isRunning && _remoteEnd != null && !_simulateFailure;

    /// <inheritdoc />
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Connects this transport to a remote transport, creating a bidirectional link.
    /// </summary>
    public void ConnectTo(InMemoryTransport remote)
    {
        _remoteEnd = remote;
        remote._remoteEnd = this;
    }

    /// <summary>
    /// Simulates a transport failure (e.g., BLE goes out of range).
    /// </summary>
    public void SimulateFailure(bool fail = true)
    {
        _simulateFailure = fail;
    }

    /// <inheritdoc />
    public Task StartAsync()
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(MeshPacket packet)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Transport is not available");

        // Deliver to the remote end (simulates over-the-air transmission)
        _remoteEnd!.DeliverLocally(packet);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the remote end to deliver a packet to this side.
    /// </summary>
    internal void DeliverLocally(MeshPacket packet)
    {
        PacketReceived?.Invoke(packet);
    }
}

/// <summary>
/// A mock BLE service that wraps InMemoryTransport and implements IBleService.
/// </summary>
public class MockBleService : IBleService
{
    private readonly InMemoryTransport _inner;

    public MockBleService(InMemoryTransport inner)
    {
        _inner = inner;
        _inner.PacketReceived += p => PacketReceived?.Invoke(p);
    }

    public bool IsAvailable => _inner.IsAvailable;
    public event Action<MeshPacket>? PacketReceived;

    public Task SendAsync(MeshPacket packet) => _inner.SendAsync(packet);
    public Task StartAsync() => _inner.StartAsync();
    public Task StopAsync() => _inner.StopAsync();
    public Task StartAdvertising() => Task.CompletedTask;
    public Task StartScanning() => Task.CompletedTask;
    public Task StopAll() => _inner.StopAsync();

    public void SimulateFailure(bool fail = true) => _inner.SimulateFailure(fail);
}

/// <summary>
/// A transport that is never available — used as a placeholder for disabled transports.
/// </summary>
public class DisabledTransport : INetworkTransport
{
    public bool IsAvailable => false;
    public event Action<MeshPacket>? PacketReceived;
    public Task SendAsync(MeshPacket packet) => throw new InvalidOperationException("Disabled");
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}
