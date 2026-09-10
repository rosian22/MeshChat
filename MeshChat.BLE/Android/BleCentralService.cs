#if ANDROID

using System.Collections.Concurrent;
using System.Text.Json;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using MeshChat.Core.Models;

namespace MeshChat.BLE.Android;

/// <summary>
/// Android BLE central (scanner) service using BluetoothLeScanner.
/// Scans for peripherals advertising the mesh service and manages connections.
/// </summary>
public class BleCentralService : BluetoothGattCallback
{
    // Nordic UART service and characteristic UUIDs
    private static readonly Java.Util.UUID ServiceUuid = Java.Util.UUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly Java.Util.UUID CharacteristicUuid = Java.Util.UUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    private readonly Context _context;
    private readonly BluetoothManager _bluetoothManager;
    private BluetoothLeScanner? _scanner;
    private readonly ConcurrentDictionary<string, BluetoothGatt> _connectedGatts;
    private readonly ConcurrentDictionary<string, BluetoothGattCharacteristic> _meshCharacteristics;

    /// <summary>
    /// Initializes a new instance of the BleCentralService class.
    /// </summary>
    /// <param name="context">The Android application context.</param>
    public BleCentralService(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _bluetoothManager = context.GetSystemService(Context.BluetoothService) as BluetoothManager
            ?? throw new InvalidOperationException("BluetoothManager not available");

        _connectedGatts = new ConcurrentDictionary<string, BluetoothGatt>();
        _meshCharacteristics = new ConcurrentDictionary<string, BluetoothGattCharacteristic>();
    }

    /// <summary>
    /// Starts scanning for mesh service peripherals.
    /// </summary>
    public void StartScanning()
    {
        var bluetoothAdapter = _bluetoothManager.Adapter;
        if (bluetoothAdapter == null || !bluetoothAdapter.IsEnabled)
        {
            System.Diagnostics.Debug.WriteLine("Bluetooth is not enabled");
            return;
        }

        _scanner = bluetoothAdapter.BluetoothLeScanner;
        if (_scanner == null)
        {
            System.Diagnostics.Debug.WriteLine("BLE scanning not supported");
            return;
        }

        var scanFilter = new ScanFilter.Builder()!
            .SetServiceUuid(new ParcelUuid(ServiceUuid))
            .Build();

        var scanSettings = new ScanSettings.Builder()!
            .SetScanMode(ScanMode.Balanced)
            .Build();

        _scanner.StartScan(new[] { scanFilter }, scanSettings, new ScanCallback(this));
        System.Diagnostics.Debug.WriteLine("Started scanning for mesh service");
    }

    /// <summary>
    /// Stops scanning.
    /// </summary>
    public void StopScanning()
    {
        if (_scanner != null)
        {
            _scanner.StopScan(new ScanCallback(this));
        }
    }

    /// <summary>
    /// Broadcasts a mesh packet to all connected peripherals.
    /// </summary>
    /// <param name="packet">The packet to broadcast.</param>
    public async Task BroadcastToAllPeers(MeshPacket packet)
    {
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(packet);

            foreach (var gatt in _connectedGatts.Values)
            {
                var deviceAddress = gatt.Device.Address;
                if (_meshCharacteristics.TryGetValue(deviceAddress, out var characteristic))
                {
                    characteristic.SetValue(data);
                    gatt.WriteCharacteristic(characteristic);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error broadcasting to peers: {ex.Message}");
        }
    }

    /// <summary>
    /// Pings all connected peripherals to maintain the connection.
    /// </summary>
    public async Task PingAllConnectedPeers()
    {
        try
        {
            var pingPacket = new MeshPacket
            {
                MessageId = Guid.NewGuid(),
                Ttl = 1,
                DestinationHash = "PING",
                SenderHash = "PING",
                EncryptedPayload = Array.Empty<byte>(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await BroadcastToAllPeers(pingPacket);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error pinging peers: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the number of connected peripherals.
    /// </summary>
    public int ConnectedPeripheralCount => _connectedGatts.Count;

    // BluetoothGattCallback overrides

    /// <summary>
    /// Called when the GATT connection state changes.
    /// </summary>
    public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
    {
        base.OnConnectionStateChange(gatt, status, newState);

        var deviceAddress = gatt.Device.Address;
        if (newState == ProfileState.Connected)
        {
            System.Diagnostics.Debug.WriteLine($"Connected to {deviceAddress}");
            _connectedGatts.TryAdd(deviceAddress, gatt);
            gatt.DiscoverServices();
        }
        else if (newState == ProfileState.Disconnected)
        {
            System.Diagnostics.Debug.WriteLine($"Disconnected from {deviceAddress}");
            _connectedGatts.TryRemove(deviceAddress, out _);
            _meshCharacteristics.TryRemove(deviceAddress, out _);
            gatt.Close();
        }
    }

    /// <summary>
    /// Called when the GATT services are discovered.
    /// </summary>
    public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
    {
        base.OnServicesDiscovered(gatt, status);

        if (status != GattStatus.Success)
        {
            System.Diagnostics.Debug.WriteLine($"Service discovery failed: {status}");
            return;
        }

        var service = gatt.GetService(ServiceUuid);
        if (service == null)
        {
            System.Diagnostics.Debug.WriteLine("Mesh service not found");
            return;
        }

        var characteristic = service.GetCharacteristic(CharacteristicUuid);
        if (characteristic != null)
        {
            _meshCharacteristics.TryAdd(gatt.Device.Address, characteristic);
            System.Diagnostics.Debug.WriteLine("Discovered mesh characteristic");
        }
    }

    /// <summary>
    /// Called when a characteristic write completes.
    /// </summary>
    public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
    {
        base.OnCharacteristicWrite(gatt, characteristic, status);

        if (status == GattStatus.Success)
        {
            System.Diagnostics.Debug.WriteLine($"Write successful to {gatt.Device.Address}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Write failed to {gatt.Device.Address}: {status}");
        }
    }

    /// <summary>
    /// Called when a characteristic value is read.
    /// </summary>
    public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
    {
        base.OnCharacteristicRead(gatt, characteristic, status);
    }

    internal void OnDeviceDiscovered(BluetoothDevice device)
    {
        var bluetoothAdapter = _bluetoothManager.Adapter;
        if (bluetoothAdapter != null)
        {
            System.Diagnostics.Debug.WriteLine($"Discovered device: {device.Name ?? "Unknown"}");
            device.ConnectGatt(_context, false, this);
        }
    }
}

/// <summary>
/// Callback for BLE scan operations.
/// </summary>
internal class ScanCallback : ScanCallback
{
    private readonly BleCentralService _centralService;

    /// <summary>
    /// Initializes a new instance of the ScanCallback class.
    /// </summary>
    /// <param name="centralService">The parent central service.</param>
    public ScanCallback(BleCentralService centralService)
    {
        _centralService = centralService;
    }

    /// <summary>
    /// Called when a device is discovered during a scan.
    /// </summary>
    public override void OnScanResult(ScanCallbackType callbackType, ScanResult result)
    {
        base.OnScanResult(callbackType, result);

        if (result.Device != null)
        {
            _centralService.OnDeviceDiscovered(result.Device);
        }
    }

    /// <summary>
    /// Called when a batch of scan results is ready.
    /// </summary>
    public override void OnBatchScanResults(IList<ScanResult> results)
    {
        base.OnBatchScanResults(results);

        foreach (var result in results)
        {
            if (result.Device != null)
            {
                _centralService.OnDeviceDiscovered(result.Device);
            }
        }
    }

    /// <summary>
    /// Called when scan fails.
    /// </summary>
    public override void OnScanFailed(ScanFailureMode errorCode)
    {
        base.OnScanFailed(errorCode);
        System.Diagnostics.Debug.WriteLine($"Scan failed: {errorCode}");
    }
}

#endif
