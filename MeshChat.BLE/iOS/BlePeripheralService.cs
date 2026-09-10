#if IOS

using System.Text.Json;
using CoreBluetooth;
using MeshChat.Core.Models;
using ObjCRuntime;

namespace MeshChat.BLE.iOS;

/// <summary>
/// iOS BLE peripheral (advertiser) service using CoreBluetooth.
/// Advertises a custom GATT service and handles incoming write requests from central devices.
/// </summary>
public class BlePeripheralService : NSObject, ICBPeripheralManagerDelegate
{
    // Nordic UART service and characteristic UUIDs
    private static readonly CBUUID ServiceUuid = CBUUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly CBUUID CharacteristicUuid = CBUUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    private CBPeripheralManager? _peripheralManager;
    private CBMutableService? _gattService;
    private CBMutableCharacteristic? _meshCharacteristic;

    private const string StateRestorationKey = "MeshChatBlePeripheralState";

    /// <summary>
    /// Event raised when a mesh packet is received from a central device.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Initializes a new instance of the BlePeripheralService class.
    /// </summary>
    public BlePeripheralService()
    {
        var options = new NSDictionary(
            CBPeripheralManager.OptionRestoreIdentifierKey,
            (NSString)StateRestorationKey
        );

        _peripheralManager = new CBPeripheralManager(this, null, options);
    }

    /// <summary>
    /// Starts advertising the mesh service.
    /// </summary>
    public void StartAdvertising()
    {
        if (_peripheralManager == null)
            return;

        // Wait for peripheral manager to be powered on
        if (_peripheralManager.State != CBPeripheralManagerState.PoweredOn)
        {
            return;
        }

        SetupGattService();
        StartAdvertise();
    }

    /// <summary>
    /// Stops advertising.
    /// </summary>
    public void StopAdvertising()
    {
        _peripheralManager?.StopAdvertising();
    }

    /// <summary>
    /// Gets the current state of the peripheral manager.
    /// </summary>
    public CBPeripheralManagerState State => _peripheralManager?.State ?? CBPeripheralManagerState.Unknown;

    // CBPeripheralManagerDelegate methods

    /// <summary>
    /// Called when the peripheral manager's state is updated.
    /// </summary>
    [Export("peripheralManagerDidUpdateState:")]
    public void DidUpdateState(CBPeripheralManager peripheral)
    {
        switch (peripheral.State)
        {
            case CBPeripheralManagerState.PoweredOn:
                SetupGattService();
                StartAdvertise();
                break;
            case CBPeripheralManagerState.PoweredOff:
            case CBPeripheralManagerState.Resetting:
                StopAdvertising();
                break;
            case CBPeripheralManagerState.Unsupported:
            case CBPeripheralManagerState.Unauthorized:
                System.Diagnostics.Debug.WriteLine("BLE is not available on this device");
                break;
        }
    }

    /// <summary>
    /// Called when the peripheral manager receives a write request from a central.
    /// </summary>
    [Export("peripheralManager:didReceiveWriteRequests:")]
    public void DidReceiveWriteRequests(CBPeripheralManager peripheral, CBATTRequest[] requests)
    {
        foreach (var request in requests)
        {
            if (request.Value == null || request.Value.Length == 0)
                continue;

            try
            {
                var data = request.Value.ToArray();
                var packet = JsonSerializer.Deserialize<MeshPacket>(data);
                if (packet != null)
                {
                    PacketReceived?.Invoke(packet);
                }

                peripheral.RespondToRequest(request, CBATTError.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deserializing packet: {ex.Message}");
                peripheral.RespondToRequest(request, CBATTError.InvalidRequest);
            }
        }
    }

    /// <summary>
    /// Called when the peripheral manager adds a service.
    /// </summary>
    [Export("peripheralManager:didAddService:error:")]
    public void DidAddService(CBPeripheralManager peripheral, CBService service, NSError? error)
    {
        if (error != null)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding service: {error.LocalizedDescription}");
            return;
        }

        System.Diagnostics.Debug.WriteLine("GATT service added successfully");
    }

    /// <summary>
    /// Called when a central subscribes to the characteristic.
    /// </summary>
    [Export("peripheralManager:central:didSubscribeToCharacteristic:")]
    public void DidSubscribeToCharacteristic(CBPeripheralManager peripheral, CBCentral central, CBCharacteristic characteristic)
    {
        System.Diagnostics.Debug.WriteLine($"Central subscribed: {central.Identifier}");
    }

    /// <summary>
    /// Called when a central unsubscribes from the characteristic.
    /// </summary>
    [Export("peripheralManager:central:didUnsubscribeFromCharacteristic:")]
    public void DidUnsubscribeFromCharacteristic(CBPeripheralManager peripheral, CBCentral central, CBCharacteristic characteristic)
    {
        System.Diagnostics.Debug.WriteLine($"Central unsubscribed: {central.Identifier}");
    }

    /// <summary>
    /// Called when the peripheral manager is ready to send data.
    /// </summary>
    [Export("peripheralManagerIsReadyToUpdateSubscribers:")]
    public void IsReadyToUpdateSubscribers(CBPeripheralManager peripheral)
    {
        System.Diagnostics.Debug.WriteLine("Peripheral is ready to update subscribers");
    }

    private void SetupGattService()
    {
        if (_peripheralManager?.State != CBPeripheralManagerState.PoweredOn)
            return;

        // Create the characteristic
        _meshCharacteristic = new CBMutableCharacteristic(
            CharacteristicUuid,
            CBCharacteristicProperties.Write | CBCharacteristicProperties.Notify,
            CBAttributePermissions.Writeable | CBAttributePermissions.Readable
        );

        // Create the service
        _gattService = new CBMutableService(ServiceUuid, isPrimary: true);
        _gattService.Characteristics = new[] { _meshCharacteristic };

        // Add the service to the peripheral manager
        _peripheralManager?.AddService(_gattService);
    }

    private void StartAdvertise()
    {
        if (_peripheralManager == null || _gattService == null)
            return;

        var advertisingData = new NSDictionary(
            CBAdvertisement.DataServiceUUIDsKey,
            new[] { ServiceUuid }
        );

        _peripheralManager.StartAdvertising(advertisingData);
        System.Diagnostics.Debug.WriteLine("Started advertising mesh service");
    }
}

#endif
