using UnityEngine;

/// <summary>
/// Identifiers of Nebula's 4 atomizers.
/// The order matches the firmware 2.0 indexes (units[0..3]).
/// </summary>
public enum NebulaAtomizer
{
    L1 = 0, // Unit 1 -> 'A' / 'a' / 'Z'
    L2 = 1, // Unit 2 -> 'B' / 'b' / 'Y'
    R1 = 2, // Unit 3 -> 'C' / 'c' / 'X'
    R2 = 3  // Unit 4 -> 'D' / 'd' / 'W'
}

/// <summary>
/// Communication protocol with the Nebula 2.0 firmware.
/// Single source of truth on the Unity side: if the firmware changes, edit only this file.
/// </summary>
public static class NebulaProtocol
{
    // Per-atomizer commands (index 0..3).
    private static readonly char[] EnableCommands = { 'A', 'B', 'C', 'D' };
    private static readonly char[] DisableCommands = { 'a', 'b', 'c', 'd' };
    private static readonly char[] ConfigCommands = { 'Z', 'Y', 'X', 'W' };

    // Global commands.
    public const string StopAll = "S";
    public const string FanlessModeOn = "L";
    public const string FanlessModeOff = "l";
    public const string ManualExtractionOn = "N";
    public const string ManualExtractionOff = "n";
    public const string QueryState = "?";

    // Advertised name used to identify the device (serial handshake / BLE name).
    public const string Handshake = "Nebula";

    // Notable firmware responses.
    public const string StateBusy = "STATE:BUSY";
    public const string StateIdle = "STATE:IDLE";

    /// <summary>Enable command for an atomizer (e.g. "A").</summary>
    public static string Enable(NebulaAtomizer atomizer)
        => EnableCommands[(int)atomizer].ToString();

    /// <summary>Disable command for an atomizer (e.g. "a").</summary>
    public static string Disable(NebulaAtomizer atomizer)
        => DisableCommands[(int)atomizer].ToString();

    /// <summary>
    /// Configuration command for an atomizer (e.g. "Z1000;50").
    /// </summary>
    /// <param name="periodMs">Square signal period in ms (>= 10, clamped by firmware).</param>
    /// <param name="dutyCyclePercent">Duty cycle in % (0-100).</param>
    public static string Configure(NebulaAtomizer atomizer, int periodMs, int dutyCyclePercent)
    {
        periodMs = Mathf.Max(10, periodMs);
        dutyCyclePercent = Mathf.Clamp(dutyCyclePercent, 0, 100);
        return $"{ConfigCommands[(int)atomizer]}{periodMs};{dutyCyclePercent}";
    }
}
