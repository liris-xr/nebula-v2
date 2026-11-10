using UnityEngine;

/// <summary>
/// VR bootstrap for Nebula: connects the NebulaManager singleton over BLE on start so the
/// distance-triggered odor zones (NebulaOdorZone) work without the desktop OnGUI demo.
///
/// The original nebula-v2 NebulaManager auto-connected in Start(); the BleBridge-backed
/// package defers transport construction until Connect() is called (so a GUI can set the
/// transport + device name first). This component performs that one-time setup for VR, where
/// there is no GUI. Put it on any scene GameObject (e.g. alongside the NebulaManager, or on
/// the XR Rig). autoReconnect on NebulaManager then keeps the link up if it drops.
/// </summary>
public class NebulaVRBootstrap : MonoBehaviour
{
    [Tooltip("Advertised BLE name of the Nebula device (firmware advertises \"Nebula\").")]
    public string deviceName = "Nebula";

    private void Start()
    {
        var manager = NebulaManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[NebulaVR] No NebulaManager in scene; add one before odor zones will work.");
            return;
        }

        // Android builds always use BLE regardless of this field; on Windows/Editor BLE is what
        // the bridge wires to NebulaBleTransport, so force BLE for the VR demo.
        manager.transport = NebulaTransportKind.Ble;
        manager.deviceName = deviceName;
        manager.autoReconnect = true;
        manager.Connect();
    }
}
