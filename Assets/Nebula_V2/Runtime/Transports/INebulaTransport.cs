using System;

/// <summary>
/// Common abstraction for every way of talking to a Nebula device (serial, BLE...).
/// Implementations may raise their events from background threads; the NebulaManager
/// is responsible for marshalling them back to the Unity main thread.
/// </summary>
public interface INebulaTransport : IDisposable
{
    bool IsConnected { get; }
    bool IsScanning { get; }

    event Action<NebulaDevice> OnConnected;
    event Action<NebulaDevice> OnDisconnected;
    event Action<string> OnMessageReceived;

    /// <summary>Starts discovery then connection, non-blocking. Updates the given device's state.</summary>
    void Connect(NebulaDevice device);

    void Disconnect();

    /// <summary>Queues a command (non-blocking, thread-safe). Serial appends '\n', BLE does not.</summary>
    void Send(string command);

    /// <summary>
    /// Called on the main thread from the manager's Update().
    /// Used by polling transports (BLE Windows). No-op for thread-driven ones (serial).
    /// </summary>
    void Tick();
}
