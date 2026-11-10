using System;

/// <summary>
/// Information about the connected Nebula device, passed to the connection events.
/// deviceId is the COM port (serial) or the BLE address; null until discovered.
/// </summary>
[Serializable]
public class NebulaDevice
{
    public string deviceName;
    public string deviceId;
    public bool connected;

    public override string ToString()
        => $"Nebula [Name: {deviceName} | ID: {deviceId ?? "?"} | Connected: {connected}]";
}
