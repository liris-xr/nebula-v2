using System.Text;
using UnityEngine;

/// <summary>
/// Minimal Nebula demo using the shared BLE bridge.
///
/// Requires the com.lirisxr.blebridge + com.lirisxr.nebula packages.
/// Windows/Editor: builds for x64, Windows 10+ with Bluetooth on. Android:
/// the bridge auto-switches to its Velorexe-based backend; no manual define needed.
///
/// Usage: drop this on any GameObject in a scene and press Play. The on-screen panel
/// lets you choose the transport (Serial or BLE) and the advertised device name
/// (default "Nebula"), then press Connect. The NebulaManager is a singleton that
/// survives scene loads, so one instance is enough.
///
/// Transport selection only matters on Windows/Editor: on Android the manager
/// always builds a BLE transport regardless of the chosen value.
/// </summary>
public class NebulaBleDemo : MonoBehaviour
{
    private NebulaManager _manager;

    // Connection state (editable from the GUI before Connect)
    private NebulaTransportKind transport = NebulaTransportKind.Ble;
    private string deviceName = "Nebula";
    private readonly NebulaTransportKind[] transportChoices =
        { NebulaTransportKind.Serial, NebulaTransportKind.Ble };
    private readonly string[] transportLabels = { "Serial", "BLE" };
    private int transportIndex = 1; // BLE by default

    // Per-atomizer control (4 atomizers: L1, L2, R1, R2)
    private int currentAtomizer;
    private readonly NebulaAtomizer[] atomizers =
        { NebulaAtomizer.L1, NebulaAtomizer.L2, NebulaAtomizer.R1, NebulaAtomizer.R2 };
    private readonly string[] atomizerNames = { "L1", "L2", "R1", "R2" };

    private int periodMs = 100;
    private int dutyCycle = 50;
    private Vector2 logScroll;
    private string lastMessage;

    private void Start()
    {
        // The manager is a DontDestroyOnLoad singleton created by the component itself;
        // if none exists yet, add it here. It does NOT build/connect a transport until
        // Connect() is called, so the GUI can set transport + deviceName first.
        _manager = NebulaManager.Instance;
        if (_manager == null)
        {
            var go = new GameObject("NebulaManager");
            _manager = go.AddComponent<NebulaManager>();
        }

        _manager.logIncomingMessages = true;
        _manager.autoReconnect = true;
        _manager.onMessageReceived.AddListener(msg =>
        {
            lastMessage = msg;
            Debug.Log($"[NebulaDemo] <- {msg}");
        });
        _manager.onDeviceConnected.AddListener(d => Debug.Log($"[NebulaDemo] Connected: {d}"));
        _manager.onDeviceDisconnected.AddListener(d => Debug.LogWarning($"[NebulaDemo] Disconnected: {d}"));
    }

    private void OnGUI()
    {
        GUILayout.BeginVertical("box", GUILayout.Width(420));
        GUILayout.Label("Nebula Demo  (com.lirisxr.nebula + blebridge)");

        if (_manager == null)
        {
            GUILayout.Label("No NebulaManager available.");
            GUILayout.EndVertical();
            return;
        }

        GUILayout.Label($"State: {(_manager.IsConnected ? "Connected" : (_manager.HasTransport ? "Connecting..." : "Disconnected"))}");

        GUILayout.Space(8);
        GUILayout.Label("Transport (Windows/Editor only; Android is always BLE)");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < transportLabels.Length; i++)
        {
            if (GUILayout.Toggle(transportIndex == i, transportLabels[i], "Button", GUILayout.Width(120)))
                transportIndex = i;
        }
        GUILayout.EndHorizontal();
        transport = transportChoices[transportIndex];

        GUILayout.Space(6);
        GUILayout.Label("Device name (BLE advertised name / serial handshake)");
        deviceName = GUILayout.TextField(deviceName, GUILayout.Width(300));

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !_manager.HasTransport;
        if (GUILayout.Button("Connect", GUILayout.Width(120)))
        {
            _manager.transport = transport;
            _manager.deviceName = deviceName;
            _manager.Connect();
        }
        GUI.enabled = wasEnabled && _manager.HasTransport;
        if (GUILayout.Button("Disconnect", GUILayout.Width(120)))
            _manager.Disconnect();
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal();

        // Atomizer controls are only meaningful once connected.
        GUILayout.Space(10);
        wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && _manager.IsConnected;
        GUILayout.Label("Atomizer");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < atomizers.Length; i++)
        {
            if (GUILayout.Toggle(currentAtomizer == i, atomizerNames[i], "Button", GUILayout.Width(60)))
                currentAtomizer = i;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label($"Period (ms): {periodMs}");
        periodMs = Mathf.RoundToInt(GUILayout.HorizontalSlider(periodMs, 10, 2000));
        GUILayout.Label($"Duty cycle (%): {dutyCycle}");
        dutyCycle = Mathf.RoundToInt(GUILayout.HorizontalSlider(dutyCycle, 0, 100));

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Configure", GUILayout.Width(120)))
            _manager.Configure(atomizers[currentAtomizer], periodMs, dutyCycle);
        if (GUILayout.Button("Start", GUILayout.Width(120)))
            _manager.StartDiffusion(atomizers[currentAtomizer]);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop", GUILayout.Width(120)))
            _manager.StopDiffusion(atomizers[currentAtomizer]);
        if (GUILayout.Button("Stop All", GUILayout.Width(120)))
            _manager.StopAll();
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        if (GUILayout.Button("Query State (?)"))
            _manager.QueryState();
        GUI.enabled = wasEnabled;

        GUILayout.Space(10);
        GUILayout.Label("Last message:");
        GUILayout.Label(lastMessage ?? "(none)");

        GUILayout.EndVertical();
    }
}
