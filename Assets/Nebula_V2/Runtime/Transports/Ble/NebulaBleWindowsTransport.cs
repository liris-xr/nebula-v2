// Compiles only if the BleWinrtDll plugin is present AND the NEBULA_BLE_WINRT define is set
// (Player Settings -> Other Settings -> Scripting Define Symbols).
#if NEBULA_BLE_WINRT && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// BLE transport for Windows / Unity Editor, wired to BleWinrtDll (adabru).
/// https://github.com/adabru/BleWinrtDll — C# wrapper: the static class BleApi.
///
/// Prerequisites:
///  - Import BleWinrtDll (x64 DLL + BleApi.cs), with its asmdef "Auto Referenced".
///  - Add the NEBULA_BLE_WINRT define. Build target x64. Windows 10+ with Bluetooth on.
///
/// The DLL is non-blocking and poll-based, so we poll on each PumpNative():
///   StartDeviceScan -> ScanServices -> ScanCharacteristics -> Subscribe -> PollData loop.
/// There is no explicit Connect(); StopNative() calls Quit() to release the link.
/// </summary>
public class NebulaBleWindowsTransport : NebulaBleTransportBase
{
    // Target 16-bit UUIDs. The DLL returns 128-bit UUIDs; we match the short segment.
    private const string ServiceShortUuid = "180c";
    private const string CharShortUuid = "2a56";

    private enum Phase { Idle, ScanDevices, ScanServices, ScanCharacteristics, Ready }
    private Phase _phase = Phase.Idle;

    private string _deviceId;
    private string _serviceUuid; // reuse verbatim as returned by the DLL
    private string _charUuid;
    private string _lastError;

    // Name and "connectable" state of an advertisement can arrive in separate updates.
    private readonly Dictionary<string, ScannedDevice> _seen = new Dictionary<string, ScannedDevice>();

    private struct ScannedDevice { public string name; public bool connectable; }

    // =========================================================
    // Startup
    // =========================================================

    protected override void StartNativeConnect()
    {
        _deviceId = null;
        _serviceUuid = null;
        _charUuid = null;
        _lastError = null;
        _seen.Clear();

        BleApi.StartDeviceScan();
        _phase = Phase.ScanDevices;
        Debug.Log($"[Nebula][BLE] BLE scan started (searching for \"{DeviceName}\")...");
    }

    // =========================================================
    // Heartbeat (polling)
    // =========================================================

    protected override void PumpNative()
    {
        switch (_phase)
        {
            case Phase.ScanDevices: PumpScanDevices(); break;
            case Phase.ScanServices: PumpScanServices(); break;
            case Phase.ScanCharacteristics: PumpScanCharacteristics(); break;
            case Phase.Ready: PumpIncoming(); break;
        }
        DrainErrors();
    }

    private void PumpScanDevices()
    {
        BleApi.ScanStatus status;
        var res = new BleApi.DeviceUpdate();
        do
        {
            status = BleApi.PollDevice(ref res, false);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                if (!_seen.TryGetValue(res.id, out var entry)) entry = new ScannedDevice();
                if (res.nameUpdated) entry.name = res.name;
                if (res.isConnectableUpdated) entry.connectable = res.isConnectable;
                _seen[res.id] = entry;

                bool nameMatches = !string.IsNullOrEmpty(entry.name)
                    && string.Equals(entry.name.Trim(), DeviceName, StringComparison.OrdinalIgnoreCase);

                if (entry.connectable && nameMatches)
                {
                    _deviceId = res.id;
                    SetDiscoveredDeviceId(_deviceId);
                    BleApi.StopDeviceScan();
                    BleApi.ScanServices(_deviceId);
                    _phase = Phase.ScanServices;
                    Debug.Log($"[Nebula][BLE] \"{DeviceName}\" found ({_deviceId}). Discovering services...");
                    return;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {
                ReportConnectionLost($"device \"{DeviceName}\" not found during BLE scan");
                return;
            }
        }
        while (status == BleApi.ScanStatus.AVAILABLE);
    }

    private void PumpScanServices()
    {
        BleApi.ScanStatus status;
        var res = new BleApi.Service();
        do
        {
            status = BleApi.PollService(out res, false);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                if (UuidContains(res.uuid, ServiceShortUuid))
                {
                    _serviceUuid = res.uuid;
                    BleApi.ScanCharacteristics(_deviceId, _serviceUuid);
                    _phase = Phase.ScanCharacteristics;
                    Debug.Log($"[Nebula][BLE] Service {ServiceShortUuid} found. Discovering characteristics...");
                    return;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {
                ReportConnectionLost($"service {ServiceShortUuid} not found on the device");
                return;
            }
        }
        while (status == BleApi.ScanStatus.AVAILABLE);
    }

    private void PumpScanCharacteristics()
    {
        BleApi.ScanStatus status;
        var res = new BleApi.Characteristic();
        do
        {
            status = BleApi.PollCharacteristic(out res, false);
            if (status == BleApi.ScanStatus.AVAILABLE)
            {
                if (UuidContains(res.uuid, CharShortUuid))
                {
                    _charUuid = res.uuid;
                    // Subscribe to notifications: required to receive firmware status messages.
                    BleApi.SubscribeCharacteristic(_deviceId, _serviceUuid, _charUuid, false);
                    _phase = Phase.Ready;
                    Debug.Log($"[Nebula][BLE] Characteristic {CharShortUuid} subscribed. Link ready.");
                    ReportLinkReady();
                    return;
                }
            }
            else if (status == BleApi.ScanStatus.FINISHED)
            {
                ReportConnectionLost($"characteristic {CharShortUuid} not found in the service");
                return;
            }
        }
        while (status == BleApi.ScanStatus.AVAILABLE);
    }

    private void PumpIncoming()
    {
        var data = new BleApi.BLEData();
        while (BleApi.PollData(out data, false))
        {
            if (data.size > 0)
            {
                // Firmware sends ASCII "[NEBULA][BLE] ...". One packet = one message.
                string message = Encoding.ASCII.GetString(data.buf, 0, data.size);
                ReportMessage(message);
            }
        }
    }

    // =========================================================
    // Writing
    // =========================================================

    protected override void WriteNative(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        byte[] payload = Encoding.ASCII.GetBytes(command);
        var data = new BleApi.BLEData
        {
            buf = new byte[512],
            size = (short)Math.Min(payload.Length, 512),
            deviceId = _deviceId,
            serviceUuid = _serviceUuid,
            characteristicUuid = _charUuid
        };
        Array.Copy(payload, data.buf, data.size);
        BleApi.SendData(in data, false); // non-blocking write
    }

    // =========================================================
    // Cleanup
    // =========================================================

    protected override void StopNative()
    {
        _phase = Phase.Idle;
        try { BleApi.Quit(); } // releases all links; without this, reconnections fail
        catch (Exception e) { Debug.LogWarning($"[Nebula][BLE] Quit(): {e.Message}"); }
    }

    // =========================================================
    // Utilities
    // =========================================================

    private static bool UuidContains(string uuid, string shortCode)
        => !string.IsNullOrEmpty(uuid) && uuid.ToLowerInvariant().Contains(shortCode);

    // Surfaces the last DLL error to the console (once per message).
    private void DrainErrors()
    {
        var err = new BleApi.ErrorMessage();
        BleApi.GetError(out err);
        if (!string.IsNullOrEmpty(err.msg) && err.msg != _lastError)
        {
            _lastError = err.msg;
            Debug.LogWarning($"[Nebula][BLE][DLL] {err.msg}");
        }
    }
}
#endif
