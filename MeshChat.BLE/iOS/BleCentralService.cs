#if IOS

using System.Collections.Concurrent;
using System.Text.Json;
using CoreBluetooth;
using MeshChat.Core.Models;
using ObjCRuntime;

namespace MeshChat.BLE.iOS;

/// <summary>
/// iOS BLE central (scanner) service using CoreBluetooth.
/// Scans for peripherals advertising the mesh service and manages connections.
/// </summary>
public class BleCentralService : NSObject, ICBCentralManagerDelegate, ICBPeripheralDelegate
{
    // Nordic UART service and characteristic UUIDs
    private static readonly CBUUID ServiceUuid = CBUUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly CBUUID CharacteristicUuid = CBUUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    private CBCentralManager? _centralManager;
    private readonly ConcurrentDictionary<string, CBPeripheral> _connectedPeripherals;
    private readonly ConcurrentDictionary<string, CBCharacteristic> _meshCharacteristics;

    private const string StateRestorationKey = "MeshChatBleCentralState";

    /// <summary>
    /// Initializes a new instance of the BleCentralService class.
    /// </summary>
    public BleCentralService()
    {
        _connectedPeripherals = new ConcurrentDictionary<string, CBPeripheral>();
        _meshCharacteristics = new ConcurrentDictionary<string, CBCharacteristic>();

        var options = new NSDictionary(
            CBCentralManager.OptionRestoreIdentifierKey,
            (NSString)StateRestorationKey,
            CBCentralManager.OptionShowPowerAlertKey,
            NSNumber.FromBoolean(true)
        );

        _centralManager = new CBCentralManager(this, null, options);
    }

    /// <summary>
    /// Starts scanning for mesh service peripherals.
    /// </summary>
    public void StartScanning()
    {
        if (_centralManager?.State != CBCentralManagerState.PoweredOn)
            return;

        var scanOptions = new NSDictionary(
            CBCentralManager.ScanOptionAllowDuplicatesKey,
            NSNumber.FromBoolean(false)
        );

        _centralManager?.ScanForPeripherals(new[] { ServiceUuid }, scanOptions);
        System.Diagnostics.Debug.WriteLine("Started scanning for mesh service");
    }

    /// <summary>
    /// Stops scanning.
    /// </summary>
    public void StopScanning()
    {
        _centralManager?.StopScan();
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
            var nsData = NSData.FromArray(data);

            foreach (var peripheral in _connectedPeripherals.Values)
            {
                if (_meshCharacteristics.TryGetValue(peripheral.Identifier.ToString(), out var characteristic))
                {
                    peripheral.WriteValue(nsData, characteristic, CBCharacteristicWriteType.WithoutResponse);
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
    public int ConnectedPeripheralCount => _connectedPeripherals.Count;

    // CBCentralManagerDelegate methods

    /// <summary>
    /// Called when the central manager's state is updated.
    /// </summary>
    [Export("centralManagerDidUpdateState:")]
    public void DidUpdateState(CBCentralManager central)
    {
        System.Diagnostics.Debug.WriteLine($"Central manager state: {central.State}");

        if (central.State == CBCentralManagerState.PoweredOn)
        {
            StartScanning();
        }
    }

    /// <summary>
    /// Called when the central discovers a peripheral.
    /// </summary>
    [Export("centralManager:didDiscoverPeripheral:advertisementData:RSSI:")]
    public void DidDiscoverPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
    {
        System.Diagnostics.Debug.WriteLine($"Discovered peripheral: {peripheral.Name ?? "Unknown"}");

        // Connect to the peripheral
        central.ConnectPeripheral(peripheral);
    }

    /// <summary>
    /// Called when the central successfully connects to a peripheral.
    /// </summary>
    [Export("centralManager:didConnectPeripheral:")]
    public void DidConnectPeripheral(CBCentralManager central, CBPeripheral peripheral)
    {
        System.Diagnostics.Debug.WriteLine($"Connected to peripheral: {peripheral.Identifier}");

        _connectedPeripherals.TryAdd(peripheral.Identifier.ToString(), peripheral);
        peripheral.Delegate = this;

        // Discover the mesh service
        peripheral.DiscoverServices(new[] { ServiceUuid });
    }

    /// <summary>
    /// Called when the central disconnects from a peripheral.
    /// </summary>
    [Export("centralManager:didDisconnectPeripheral:error:")]
    public void DidDisconnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
    {
        System.Diagnostics.Debug.WriteLine($"Disconnected from peripheral: {peripheral.Identifier}");

        _connectedPeripherals.TryRemove(peripheral.Identifier.ToString(), out _);
        _meshCharacteristics.TryRemove(peripheral.Identifier.ToString(), out _);
    }

    /// <summary>
    /// Called when the central fails to connect to a peripheral.
    /// </summary>
    [Export("centralManager:didFailToConnectPeripheral:error:")]
    public void DidFailToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to connect to peripheral: {error?.LocalizedDescription}");

        _connectedPeripherals.TryRemove(peripheral.Identifier.ToString(), out _);
    }

    // CBPeripheralDelegate methods

    /// <summary>
    /// Called when the peripheral discovers services.
    /// </summary>
    [Export("peripheral:didDiscoverServices:")]
    public void DidDiscoverServices(CBPeripheral peripheral, NSError? error)
    {
        if (error != null)
        {
            System.Diagnostics.Debug.WriteLine($"Error discovering services: {error.LocalizedDescription}");
            return;
        }

        if (peripheral.Services == null)
            return;

        foreach (var service in peripheral.Services)
        {
            if (service.UUID.Equals(ServiceUuid))
            {
                peripheral.DiscoverCharacteristics(new[] { CharacteristicUuid }, service);
            }
        }
    }

    /// <summary>
    /// Called when the peripheral discovers characteristics for a service.
    /// </summary>
    [Export("peripheral:didDiscoverCharacteristicsForService:error:")]
    public void DidDiscoverCharacteristicsForService(CBPeripheral peripheral, CBService service, NSError? error)
    {
        if (error != null)
        {
            System.Diagnostics.Debug.WriteLine($"Error discovering characteristics: {error.LocalizedDescription}");
            return;
        }

        if (service.Characteristics == null)
            return;

        foreach (var characteristic in service.Characteristics)
        {
            if (characteristic.UUID.Equals(CharacteristicUuid))
            {
                _meshCharacteristics.TryAdd(peripheral.Identifier.ToString(), characteristic);
                System.Diagnostics.Debug.WriteLine("Discovered mesh characteristic");
            }
        }
    }

    /// <summary>
    /// Called when a characteristic value is updated.
    /// </summary>
    [Export("peripheral:didUpdateValueForCharacteristic:error:")]
    public void DidUpdateValueForCharacteristic(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
    {
        if (error != null)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating characteristic: {error.LocalizedDescription}");
            return;
        }

        System.Diagnostics.Debug.WriteLine("Characteristic value updated");
    }

    /// <summary>
    /// Called when a write request completes.
    /// </summary>
    [Export("peripheral:didWriteValueForCharacteristic:error:")]
    public void DidWriteValueForCharacteristic(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
    {
        if (error != null)
        {
            System.Diagnostics.Debug.WriteLine($"Error writing characteristic: {error.LocalizedDescription}");
        }
    }
}

#endif
