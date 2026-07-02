using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// BLE logic shared by all platforms, independent from the native plugin used.
///
/// Carries everything that does NOT depend on the OS: the INebulaTransport contract,
/// the high-level state machine (Idle -> Connecting -> Connected), the "?" handshake,
/// the connection timeout, and the outgoing-queue drain once connected.
///
/// Subclasses provide only the plugin-specific "how" (scan, discover, subscribe, write, close).
///
/// Unlike serial, each BLE write is a complete message: no '\n' terminator is added.
/// The device identity is guaranteed by discovery (advertised name + service 180C +
/// characteristic 2A56), so the "?" handshake is only used to obtain an initial status.
/// </summary>
public abstract class NebulaBleTransportBase : INebulaTransport
{
    private enum State { Idle, Connecting, Connected }

    protected const float ConnectTimeoutSeconds = 15f;

    public bool IsConnected => _state == State.Connected;
    public bool IsScanning => _state == State.Connecting;

    public event Action<NebulaDevice> OnConnected;
    public event Action<NebulaDevice> OnDisconnected;
    public event Action<string> OnMessageReceived;

    private State _state = State.Idle;
    private NebulaDevice _device;
    private float _connectStartTime;
    private readonly ConcurrentQueue<string> _outgoingCommands = new ConcurrentQueue<string>();

    /// <summary>Advertised name searched during scanning (e.g. "Nebula"). Available after Connect().</summary>
    protected string DeviceName => _device != null ? _device.deviceName : null;

    // =========================================================
    // INebulaTransport
    // =========================================================

    public void Connect(NebulaDevice device)
    {
        if (_state != State.Idle) return;

        _device = device ?? throw new ArgumentNullException(nameof(device));
        _device.connected = false;

        _state = State.Connecting;
        _connectStartTime = Time.unscaledTime;

        while (_outgoingCommands.TryDequeue(out _)) { } // start from an empty queue

        try
        {
            StartNativeConnect();
        }
        catch (Exception e)
        {
            ReportConnectionLost($"failed to start BLE connection: {e.Message}");
        }
    }

    /// <summary>The subclass discovered the native device id (BLE address). Store it on the model.</summary>
    protected void SetDiscoveredDeviceId(string id)
    {
        if (_device != null) _device.deviceId = id;
    }

    public void Tick()
    {
        if (_state == State.Idle) return;

        try
        {
            PumpNative();
        }
        catch (Exception e)
        {
            ReportConnectionLost($"BLE error during polling: {e.Message}");
            return;
        }

        if (_state == State.Connecting && Time.unscaledTime - _connectStartTime > ConnectTimeoutSeconds)
        {
            ReportConnectionLost("BLE connection timeout");
            return;
        }

        if (_state == State.Connected)
        {
            while (_outgoingCommands.TryDequeue(out var command))
                WriteNative(command);
        }
    }

    public void Send(string command)
    {
        if (_state != State.Connected)
        {
            Debug.LogWarning($"[Nebula][BLE] Command ignored (not connected): {command}");
            return;
        }
        _outgoingCommands.Enqueue(command);
    }

    public void Disconnect()
    {
        if (_state == State.Idle) return;

        bool wasConnected = _state == State.Connected;
        _state = State.Idle;

        // Graceful stop, non-blocking: queue "S", then pump the DLL a bit so it actually
        // goes out before we tear down the stack. Never block the main thread here.
        if (wasConnected)
        {
            try
            {
                WriteNative(NebulaProtocol.StopAll);
                for (int i = 0; i < 20; i++) // ~200 ms max
                {
                    PumpNative();
                    System.Threading.Thread.Sleep(10);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] Final stop not sent: {e.Message}"); }
        }

        try { StopNative(); } catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] StopNative: {e.Message}"); }
        while (_outgoingCommands.TryDequeue(out _)) { }

        if (_device != null) _device.connected = false;
        if (wasConnected) OnDisconnected?.Invoke(_device);
    }

    public void Dispose() => Disconnect();

    // =========================================================
    // Called by subclasses to report progress
    // =========================================================

    /// <summary>
    /// The subclass finished scan + discovery + subscribe: the link is usable.
    /// The base moves to Connected, raises OnConnected, and sends the "?" handshake.
    /// </summary>
    protected void ReportLinkReady()
    {
        if (_state != State.Connecting) return;

        _state = State.Connected;
        if (_device != null) _device.connected = true;
        OnConnected?.Invoke(_device);

        try { WriteNative(NebulaProtocol.QueryState); }
        catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] Handshake not sent: {e.Message}"); }
    }

    /// <summary>The subclass received a firmware message.</summary>
    protected void ReportMessage(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        string message = raw.TrimEnd('\r', '\n', '\0');
        if (message.Length > 0) OnMessageReceived?.Invoke(message);
    }

    /// <summary>
    /// The subclass signals a link loss / scan failure / native error. The base cleans up,
    /// raises OnDisconnected if we were connected, and returns to Idle so the manager can retry.
    /// </summary>
    protected void ReportConnectionLost(string reason)
    {
        if (_state == State.Idle) return;

        Debug.LogWarning($"[Nebula][BLE] {reason}");

        bool wasConnected = _state == State.Connected;
        _state = State.Idle;

        try { StopNative(); } catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] StopNative: {e.Message}"); }
        while (_outgoingCommands.TryDequeue(out _)) { }

        if (_device != null) _device.connected = false;
        if (wasConnected) OnDisconnected?.Invoke(_device);
    }

    // =========================================================
    // Implemented per platform
    // =========================================================

    /// <summary>Starts the native scan/connection non-blocking (real work happens in PumpNative).</summary>
    protected abstract void StartNativeConnect();

    /// <summary>Advances the native link: poll steps, read incoming data. Called on each Tick().</summary>
    protected abstract void PumpNative();

    /// <summary>Writes a command to the Nebula characteristic (no terminator).</summary>
    protected abstract void WriteNative(string command);

    /// <summary>Cleanly releases native resources (close the link).</summary>
    protected abstract void StopNative();
}
