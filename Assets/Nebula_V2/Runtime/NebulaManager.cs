using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Which link to use. Chosen in the Inspector; only relevant on Windows/Editor.</summary>
public enum NebulaTransportKind { Serial, Ble }

// Concrete UnityEvent types so they serialize and show up in the Inspector on every Unity version.
[Serializable] public class NebulaDeviceEvent : UnityEvent<NebulaDevice> { }
[Serializable] public class NebulaMessageEvent : UnityEvent<string> { }

/// <summary>
/// Single entry point to drive a Nebula device from Unity.
/// Creates one transport (serial or BLE), marshals its background-thread events onto the
/// main thread, auto-reconnects, and exposes a typed API to control the atomizers.
///
///   NebulaManager.Instance.StartDiffusion(NebulaAtomizer.L1);
///   NebulaManager.Instance.Configure(NebulaAtomizer.L1, periodMs: 100, dutyCyclePercent: 30);
///   NebulaManager.Instance.StopDiffusion(NebulaAtomizer.L1);
/// </summary>
public class NebulaManager : MonoBehaviour
{
    public static NebulaManager Instance { get; private set; }

    [Header("Connection")]
    [Tooltip("Serial and BLE are available on Windows/Editor. BLE requires the NEBULA_BLE_WINRT define.")]
    public NebulaTransportKind transport = NebulaTransportKind.Serial;
    [Tooltip("Serial handshake / BLE advertised name.")]
    public string deviceName = "Nebula";
    public bool autoReconnect = true;
    [Tooltip("Delay between reconnection attempts (seconds).")]
    public float reconnectDelaySeconds = 3f;

    [Header("Debug")]
    [Tooltip("Log every message received from the firmware.")]
    public bool logIncomingMessages = false;

    [Header("Events (main thread)")]
    public NebulaDeviceEvent onDeviceConnected;
    public NebulaDeviceEvent onDeviceDisconnected;
    public NebulaMessageEvent onMessageReceived;

    /// <summary>The single device this manager talks to.</summary>
    public NebulaDevice Device { get; private set; }
    public bool IsConnected => _transport != null && _transport.IsConnected;

    private INebulaTransport _transport;

    // Events come from background threads; we queue them and run them in Update().
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private float _nextReconnectAttemptTime;

    // =========================================================
    // Unity lifecycle
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        CreateTransport();
    }

    private void Start() => ConnectTransport();

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        if (_transport == null) return;

        try { _transport.Tick(); }
        catch (Exception e) { Debug.LogException(e); }

        if (autoReconnect && Time.unscaledTime >= _nextReconnectAttemptTime)
        {
            _nextReconnectAttemptTime = Time.unscaledTime + reconnectDelaySeconds;
            if (!_transport.IsConnected && !_transport.IsScanning) _transport.Connect(Device);
        }
    }

    private void OnApplicationQuit() => Shutdown();
    private void OnDestroy() { if (Instance == this) Shutdown(); }

    // =========================================================
    // Setup
    // =========================================================

    private void CreateTransport()
    {
        Device = new NebulaDevice { deviceName = deviceName };
        _transport = BuildTransport();
        if (_transport == null) return;

        // Handlers only enqueue: the actual work runs on the main thread in Update().
        _transport.OnConnected += d => _mainThreadActions.Enqueue(() =>
        {
            Debug.Log($"[Nebula] Connected: {d}");
            onDeviceConnected?.Invoke(d);
        });
        _transport.OnDisconnected += d => _mainThreadActions.Enqueue(() =>
        {
            Debug.LogWarning($"[Nebula] Disconnected: {d}");
            onDeviceDisconnected?.Invoke(d);
        });
        _transport.OnMessageReceived += msg => _mainThreadActions.Enqueue(() =>
        {
            if (logIncomingMessages) Debug.Log($"[Nebula] <- {msg}");
            onMessageReceived?.Invoke(msg);
        });
    }

    private INebulaTransport BuildTransport()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (transport == NebulaTransportKind.Serial) return new NebulaSerialTransport();
    #if NEBULA_BLE_WINRT
        return new NebulaBleWindowsTransport();
    #else
        Debug.LogWarning("[Nebula] BLE on Windows needs BleWinrtDll + the NEBULA_BLE_WINRT define. No transport created.");
        return null;
    #endif
#else
        Debug.LogWarning("[Nebula] Only serial/BLE on Windows/Editor is supported for now.");
        return null;
#endif
    }

    private void ConnectTransport()
    {
        if (_transport == null) return;
        _transport.Connect(Device);
        _nextReconnectAttemptTime = Time.unscaledTime + reconnectDelaySeconds;
    }

    // =========================================================
    // Public control API
    // =========================================================

    /// <summary>Sends a raw NebulaProtocol command to the connected transport.</summary>
    public void SendCommand(string command)
    {
        if (_transport != null && _transport.IsConnected) _transport.Send(command);
        else Debug.LogWarning($"[Nebula] Command not sent (not connected): {command}");
    }

    public void StartDiffusion(NebulaAtomizer atomizer) => SendCommand(NebulaProtocol.Enable(atomizer));
    public void StopDiffusion(NebulaAtomizer atomizer) => SendCommand(NebulaProtocol.Disable(atomizer));

    public void Configure(NebulaAtomizer atomizer, int periodMs, int dutyCyclePercent)
        => SendCommand(NebulaProtocol.Configure(atomizer, periodMs, dutyCyclePercent));

    public void StopAll() => SendCommand(NebulaProtocol.StopAll);
    public void SetFanlessMode(bool enabled) => SendCommand(enabled ? NebulaProtocol.FanlessModeOn : NebulaProtocol.FanlessModeOff);
    public void SetManualExtraction(bool on) => SendCommand(on ? NebulaProtocol.ManualExtractionOn : NebulaProtocol.ManualExtractionOff);
    public void QueryState() => SendCommand(NebulaProtocol.QueryState);

    // =========================================================
    // Shutdown
    // =========================================================

    private void Shutdown()
    {
        if (_transport == null) return;

        // Turn off atomizers before closing the link.
        if (_transport.IsConnected)
        {
            _transport.Send(NebulaProtocol.StopAll);
            _transport.Tick();  // flush the queue (BLE); no-op for serial's own write thread
            Thread.Sleep(150);  // let the write actually go out
        }
        _transport.Dispose();
        _transport = null;
    }
}
