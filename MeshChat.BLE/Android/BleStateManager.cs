#if ANDROID

using Android.Content;
using MeshChat.Core.Interfaces;
using MeshChat.Core.Models;
using MeshChat.Core.Services;

namespace MeshChat.BLE.Android;

/// <summary>
/// Orchestrates Android BLE peripheral and central services with keep-alive support.
/// Implements the IBleService interface for the MeshChat framework.
/// </summary>
public class BleStateManager : IBleService
{
    private readonly BlePeripheralService _peripheralService;
    private readonly BleCentralService _centralService;
    private readonly MeshRouter _meshRouter;
    private Timer? _keepAliveTimer;

    private const int KeepAliveIntervalSeconds = 25;
    private bool _isRunning;

    /// <summary>
    /// Initializes a new instance of the BleStateManager class.
    /// </summary>
    /// <param name="context">The Android application context.</param>
    /// <param name="meshRouter">The mesh router for packet routing and relay.</param>
    public BleStateManager(Context context, MeshRouter meshRouter)
    {
        _meshRouter = meshRouter ?? throw new ArgumentNullException(nameof(meshRouter));

        var contextArg = context ?? throw new ArgumentNullException(nameof(context));
        _peripheralService = new BlePeripheralService(contextArg);
        _centralService = new BleCentralService(contextArg);

        // Wire up packet received from peripheral to the router
        _peripheralService.PacketReceived += async packet =>
        {
            await _meshRouter.OnPacketReceived(packet);
        };

        // Wire up relay requests from router to central broadcast
        _meshRouter.RelayRequested += async packet =>
        {
            await _centralService.BroadcastToAllPeers(packet);
        };
    }

    /// <summary>
    /// Gets a value indicating whether BLE is available on this platform.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Event raised when a mesh packet is received.
    /// Not directly used as packets are routed through MeshRouter.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Starts the BLE service (advertising and scanning).
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _isRunning = true;

        // Start peripheral (advertiser)
        _peripheralService.StartAdvertising();

        // Start central (scanner)
        _centralService.StartScanning();

        // Start keep-alive timer
        _keepAliveTimer = new Timer(
            async _ => await _centralService.PingAllConnectedPeers(),
            null,
            TimeSpan.FromSeconds(KeepAliveIntervalSeconds),
            TimeSpan.FromSeconds(KeepAliveIntervalSeconds)
        );

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the BLE service.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        // Stop keep-alive timer
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        // Stop peripheral
        _peripheralService.StopAdvertising();

        // Stop central
        _centralService.StopScanning();

        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends a mesh packet over BLE by broadcasting to all connected peripherals.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    public async Task SendAsync(MeshPacket packet)
    {
        if (!_isRunning)
            throw new InvalidOperationException("BLE service is not running");

        await _centralService.BroadcastToAllPeers(packet);
    }

    /// <summary>
    /// Starts the BLE peripheral service (advertiser).
    /// </summary>
    public async Task StartAdvertising()
    {
        _peripheralService.StartAdvertising();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts the BLE central service (scanner).
    /// </summary>
    public async Task StartScanning()
    {
        _centralService.StartScanning();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops all BLE operations.
    /// </summary>
    public async Task StopAll()
    {
        await StopAsync();
    }

    /// <summary>
    /// Gets the number of currently connected peripherals.
    /// </summary>
    public int ConnectedPeripheralCount => _centralService.ConnectedPeripheralCount;
}

#endif
