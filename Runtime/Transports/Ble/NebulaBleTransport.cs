using System;
using System.Text;
using Lirisxr.BleBridge;
using UnityEngine;

/// <summary>
/// BLE transport for Nebula, wired to the shared BleConnectionManager (com.lirisxr.blebridge).
///
/// This replaces the old NebulaBleWindowsTransport, which imported BleWinrtDll directly.
/// Talking to the native library is now BleBridge's job, so Nebula no longer conflicts with
/// StimulHeat's copy of the same DLL and never tears StimulHeat's link down by calling Quit().
///
/// Nebula uses service 0x180C / characteristic 0x2A56 (the same one as the original transport).
/// The BleBridge manager discovers the device by advertised name ("Nebula") + service UUID,
/// subscribes to 2A56 for incoming firmware messages, and writes commands to the same 2A56.
///
/// Prerequisites:
///  - com.lirisxr.blebridge must be installed (this package depends on it).
///  - Windows/Editor: builds for x64, Windows 10+ with Bluetooth on. The backend is
///    BleWinrtDll, selected automatically by BleConnectionManager.
///  - Android: the bridge auto-switches to its Velorexe-based Android backend. No
///    manual scripting define is needed; switching is automatic per build target.
/// </summary>
public class NebulaBleTransport : NebulaBleTransportBase
{
    // Target 16-bit UUIDs. BleBridge matches them as substrings of the full 128-bit UUID.
    private const string ServiceShortUuid = "180c";
    private const string CharShortUuid = "2a56";

    private IBleLink _link;

    // =========================================================
    // Startup
    // =========================================================
    protected override void StartNativeConnect()
    {
        // Release any previous link before requesting a new one.
        ReleaseLink();

        var profile = new BleDeviceProfile
        {
            deviceName = DeviceName,
            serviceUuid = ServiceShortUuid,
            notifyCharacteristicUuids = new[] { CharShortUuid },
            writeCharacteristicUuid = CharShortUuid
        };

        Debug.Log($"[Nebula][BLE] Requesting BLE link (searching for \"{DeviceName}\")...");
        _link = BleConnectionManager.Instance.RequestLink(profile);
        _link.OnConnected += OnLinkConnected;
        _link.OnDisconnected += OnLinkDisconnected;
        _link.OnDataReceived += OnLinkData;
    }

    // =========================================================
    // IBleLink events (fire on a background thread) -> main-thread Report*() calls
    // =========================================================
    private void OnLinkConnected(IBleLink link)
    {
        EnqueueNotification(() =>
        {
            SetDiscoveredDeviceId(link.DeviceId);
            Debug.Log($"[Nebula][BLE] \"{DeviceName}\" found ({link.DeviceId}). Link ready.");
            ReportLinkReady();
        });
    }

    private void OnLinkDisconnected(IBleLink link)
    {
        EnqueueNotification(() => ReportConnectionLost($"link to \"{DeviceName}\" lost"));
    }

    private void OnLinkData(IBleLink link, BleDataPacket packet)
    {
        // Firmware sends ASCII "[NEBULA][BLE] ...". One packet = one message.
        if (packet.size <= 0) return;
        int len = Math.Min(packet.size, packet.buf?.Length ?? 0);
        if (len <= 0) return;
        string message = Encoding.ASCII.GetString(packet.buf, 0, len);
        EnqueueNotification(() => ReportMessage(message));
    }

    // =========================================================
    // Heartbeat (no-op: BleBridge drives everything via events)
    // =========================================================
    protected override void PumpNative() { }

    // =========================================================
    // Writing
    // =========================================================
    protected override void WriteNative(string command)
    {
        if (string.IsNullOrEmpty(command) || _link == null) return;
        byte[] payload = Encoding.ASCII.GetBytes(command);
        _link.Write(payload);
    }

    // =========================================================
    // Cleanup
    // =========================================================
    protected override void StopNative()
    {
        ReleaseLink();
    }

    private void ReleaseLink()
    {
        if (_link == null) return;
        try
        {
            _link.OnConnected -= OnLinkConnected;
            _link.OnDisconnected -= OnLinkDisconnected;
            _link.OnDataReceived -= OnLinkData;
            _link.Dispose();
        }
        catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] link dispose: {e.Message}"); }
        _link = null;
    }
}
