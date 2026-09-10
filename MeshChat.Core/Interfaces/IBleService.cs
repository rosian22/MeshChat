namespace MeshChat.Core.Interfaces;

using MeshChat.Core.Models;

/// <summary>
/// Service for Bluetooth Low Energy (BLE) transport layer operations.
/// </summary>
public interface IBleService : INetworkTransport
{
    /// <summary>
    /// Starts advertising this device as a BLE peripheral.
    /// </summary>
    Task StartAdvertising();

    /// <summary>
    /// Starts scanning for nearby BLE peripherals.
    /// </summary>
    Task StartScanning();

    /// <summary>
    /// Stops all BLE operations (advertising, scanning, and connections).
    /// </summary>
    Task StopAll();
}
