#if ANDROID

using System.Text.Json;
using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.Advertise;
using Android.Content;
using MeshChat.Core.Models;

namespace MeshChat.BLE.Android;

/// <summary>
/// Android BLE peripheral (advertiser) service using BluetoothGattServer.
/// Advertises a custom GATT service and handles incoming write requests from central devices.
/// </summary>
public class BlePeripheralService : BluetoothGattServerCallback
{
    // Nordic UART service and characteristic UUIDs
    private static readonly Java.Util.UUID ServiceUuid = Java.Util.UUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly Java.Util.UUID CharacteristicUuid = Java.Util.UUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    private readonly Context _context;
    private readonly BluetoothManager _bluetoothManager;
    private BluetoothGattServer? _gattServer;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothGattCharacteristic? _meshCharacteristic;

    /// <summary>
    /// Event raised when a mesh packet is received from a central device.
    /// </summary>
    public event Action<MeshPacket>? PacketReceived;

    /// <summary>
    /// Initializes a new instance of the BlePeripheralService class.
    /// </summary>
    /// <param name="context">The Android application context.</param>
    public BlePeripheralService(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _bluetoothManager = context.GetSystemService(Context.BluetoothService) as BluetoothManager
            ?? throw new InvalidOperationException("BluetoothManager not available");
    }

    /// <summary>
    /// Starts advertising the mesh service.
    /// </summary>
    public void StartAdvertising()
    {
        var bluetoothAdapter = _bluetoothManager.Adapter;
        if (bluetoothAdapter == null || !bluetoothAdapter.IsEnabled)
        {
            System.Diagnostics.Debug.WriteLine("Bluetooth is not enabled");
            return;
        }

        SetupGattServer();
        StartAdvertise();
    }

    /// <summary>
    /// Stops advertising.
    /// </summary>
    public void StopAdvertising()
    {
        _advertiser?.StopAdvertising(new AdvertiseCallback());
        _gattServer?.Close();
        _gattServer = null;
    }

    /// <summary>
    /// Gets a value indicating whether the GATT server is running.
    /// </summary>
    public bool IsAdvertising => _gattServer != null;

    // BluetoothGattServerCallback overrides

    /// <summary>
    /// Called when the GATT server is opened.
    /// </summary>
    public override void OnServiceAdded(int status, BluetoothGattService service)
    {
        base.OnServiceAdded(status, service);
        if (status == 0)
        {
            System.Diagnostics.Debug.WriteLine("GATT service added successfully");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Failed to add GATT service: {status}");
        }
    }

    /// <summary>
    /// Called when a central connects to the peripheral.
    /// </summary>
    public override void OnConnectionStateChange(BluetoothDevice device, ProfileState status, int newState)
    {
        base.OnConnectionStateChange(device, status, newState);
        System.Diagnostics.Debug.WriteLine(
            $"Connection state changed for {device.Address}: {newState}"
        );
    }

    /// <summary>
    /// Called when the GATT server receives a write request.
    /// </summary>
    public override void OnCharacteristicWriteRequest(
        BluetoothDevice device,
        int requestId,
        BluetoothGattCharacteristic characteristic,
        bool preparedWrite,
        bool responseNeeded,
        int offset,
        byte[] value)
    {
        base.OnCharacteristicWriteRequest(device, requestId, characteristic, preparedWrite, responseNeeded, offset, value);

        if (characteristic.Uuid != CharacteristicUuid || value == null || value.Length == 0)
        {
            if (responseNeeded)
            {
                _gattServer?.SendResponse(device, requestId, 0x02, offset, null);
            }
            return;
        }

        try
        {
            var packet = JsonSerializer.Deserialize<MeshPacket>(value);
            if (packet != null)
            {
                PacketReceived?.Invoke(packet);
            }

            if (responseNeeded)
            {
                _gattServer?.SendResponse(device, requestId, 0x00, offset, null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deserializing packet: {ex.Message}");
            if (responseNeeded)
            {
                _gattServer?.SendResponse(device, requestId, 0x02, offset, null);
            }
        }
    }

    /// <summary>
    /// Called when the GATT server receives a read request.
    /// </summary>
    public override void OnCharacteristicReadRequest(
        BluetoothDevice device,
        int requestId,
        int offset,
        BluetoothGattCharacteristic characteristic)
    {
        base.OnCharacteristicReadRequest(device, requestId, offset, characteristic);
        _gattServer?.SendResponse(device, requestId, 0x00, offset, characteristic.GetValue());
    }

    /// <summary>
    /// Called when a central subscribes to notifications.
    /// </summary>
    public override void OnNotificationSent(BluetoothDevice device, int status)
    {
        base.OnNotificationSent(device, status);
        System.Diagnostics.Debug.WriteLine($"Notification sent to {device.Address}");
    }

    private void SetupGattServer()
    {
        var bluetoothAdapter = _bluetoothManager.Adapter;
        if (bluetoothAdapter == null)
            return;

        _gattServer = _bluetoothManager.OpenGattServer(_context, this);
        if (_gattServer == null)
            return;

        // Create the characteristic
        _meshCharacteristic = new BluetoothGattCharacteristic(
            CharacteristicUuid,
            GattProperty.Write | GattProperty.Notify,
            GattPermission.Write | GattPermission.Read
        );

        // Create the service
        var gattService = new BluetoothGattService(ServiceUuid, GattServiceType.Primary);
        gattService.AddCharacteristic(_meshCharacteristic);

        _gattServer.AddService(gattService);
    }

    private void StartAdvertise()
    {
        var bluetoothAdapter = _bluetoothManager.Adapter;
        if (bluetoothAdapter == null)
            return;

        _advertiser = bluetoothAdapter.BluetoothLeAdvertiser;
        if (_advertiser == null)
        {
            System.Diagnostics.Debug.WriteLine("BLE advertising not supported");
            return;
        }

        var settings = new AdvertiseSettings.Builder()!
            .SetAdvertiseMode(AdvertiseMode.Balanced)
            .SetTxPowerLevel(AdvertiseTx.PowerMedium)
            .SetConnectable(true)
            .Build();

        var data = new AdvertiseData.Builder()!
            .AddServiceUuid(new ParcelUuid(ServiceUuid))
            .SetIncludeDeviceName(true)
            .Build();

        _advertiser.StartAdvertising(settings, data, new AdvertiseCallback());
        System.Diagnostics.Debug.WriteLine("Started advertising mesh service");
    }
}

/// <summary>
/// Callback for BLE advertisement operations.
/// </summary>
internal class AdvertiseCallback : AdvertiseCallback
{
    /// <summary>
    /// Called when advertising starts successfully.
    /// </summary>
    public override void OnStartSuccess(AdvertiseSettings settingsInEffect)
    {
        base.OnStartSuccess(settingsInEffect);
        System.Diagnostics.Debug.WriteLine("Advertising started successfully");
    }

    /// <summary>
    /// Called when advertising fails to start.
    /// </summary>
    public override void OnStartFailure(AdvertiseFailureCode errorCode)
    {
        base.OnStartFailure(errorCode);
        System.Diagnostics.Debug.WriteLine($"Advertising failed: {errorCode}");
    }
}

#endif
