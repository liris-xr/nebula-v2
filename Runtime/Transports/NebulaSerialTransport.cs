#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Serial (USB) transport for Windows / Unity Editor.
///
/// - Connect() scans all COM ports in parallel (one thread per port). Each thread probes
///   its port (Arduino reboots on open, ~2s), sends '?', waits for the firmware signature.
///   The first thread to find Nebula wins.
/// - Once found, the link opens and two threads run: a reader (line-based) and a writer
///   fed by a thread-safe queue (never a blocking Write from the Unity main thread).
/// - Disconnect()/Dispose() stop the threads through volatile flags.
///
/// Events are raised from background threads; the manager marshals them to the main thread.
/// </summary>
public class NebulaSerialTransport : INebulaTransport
{
    public bool IsConnected => _isConnected;
    public bool IsScanning => _isScanning;

    public event Action<NebulaDevice> OnConnected;
    public event Action<NebulaDevice> OnDisconnected;
    public event Action<string> OnMessageReceived;

    private const int BaudRate = 115200;
    private const int BootDelayMs = 2500;           // Arduino/ESP32 reboot after port open (DTR)
    private const int ProbeTimeoutMs = 500;         // reply timeout for the '?' probe
    private const int PostProbeStabilizeMs = 200;   // settle time after closing the probe
    private const int PreConnectStabilizeMs = 1000; // wait before opening for real
    private const int PostConnectWarmupMs = 1500;   // wait after open to let the firmware boot
    private const int WriteLoopIdleMs = 10;

    // Set to true to log every probed port (timeouts, other replies) while debugging a scan.
    private static readonly bool VerboseScan = false;

    // Legacy prefix, kept as a fallback. The real reply to '?' is "STATE:IDLE" / "STATE:BUSY".
    private const string DeviceSignature = "[NEBULA]";

    // A serial reply that identifies a Nebula device (answer to the '?' probe).
    private static bool LooksLikeNebula(string reply)
        => reply != null
           && (reply.Contains(NebulaProtocol.StateIdle)
               || reply.Contains(NebulaProtocol.StateBusy)
               || reply.Contains(DeviceSignature));

    private SerialPort _serial;
    private NebulaDevice _device;

    private Thread _connectionThread;
    private Thread _readingThread;
    private Thread _writingThread;

    private readonly ConcurrentQueue<string> _outgoingCommands = new ConcurrentQueue<string>();

    private volatile bool _isScanning;
    private volatile bool _isConnected;
    private volatile bool _shouldRun;

    // =========================================================
    // Connection
    // =========================================================

    public void Connect(NebulaDevice device)
    {
        if (_isConnected || _isScanning) return;

        _device = device ?? throw new ArgumentNullException(nameof(device));

        _isScanning = true;
        _connectionThread = new Thread(ScanAndConnect) { IsBackground = true, Name = "NebulaSerialScan" };
        _connectionThread.Start();
    }

    // Serial handles its I/O on its own threads, so no main-thread heartbeat is needed.
    public void Tick() { }

    private void ScanAndConnect()
    {
        try
        {
            string port = FindPortParallel();

            if (port == null)
            {
                _device.connected = false;
                _device.deviceId = null;
                return;
            }

            Debug.Log($"[Nebula][Serial] Opening port {port}...");

            Thread.Sleep(PreConnectStabilizeMs);

            _serial = BuildPort(port);
            _serial.Open();

            Debug.Log($"[Nebula][Serial] Port {port} open. Waiting for firmware boot (~1.5s)...");
            Thread.Sleep(PostConnectWarmupMs);

            _device.deviceId = port;
            _device.connected = true;
            _isConnected = true;
            _shouldRun = true;

            _readingThread = new Thread(ReadLoop) { IsBackground = true, Name = "NebulaSerialRead" };
            _writingThread = new Thread(WriteLoop) { IsBackground = true, Name = "NebulaSerialWrite" };
            _readingThread.Start();
            _writingThread.Start();

            OnConnected?.Invoke(_device);
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.LogError($"[Nebula][Serial] Port locked (Serial Monitor open?): {e.Message}");
            CleanupPort();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Nebula][Serial] Connection failed ({e.GetType().Name}): {e.Message}");
            CleanupPort();
        }
        finally
        {
            _isScanning = false;
        }
    }

    /// <summary>
    /// Scans all available COM ports in parallel (one thread per port). The first thread
    /// that finds Nebula wins. Global timeout: 10s. COM1 is skipped (common system port).
    /// </summary>
    private string FindPortParallel()
    {
        string[] ports = SerialPort.GetPortNames();
        if (VerboseScan)
            Debug.Log($"[Nebula][Serial] Parallel scan of {ports.Length} port(s): {string.Join(", ", ports)}");

        if (ports.Length == 0)
        {
            Debug.LogWarning("[Nebula][Serial] No COM port detected.");
            return null;
        }

        string foundPort = null;
        var foundSignal = new ManualResetEvent(false);
        var scanThreads = new List<Thread>();
        var threadLock = new object();

        foreach (string port in ports)
        {
            if (port == "COM1") continue;

            Thread scanThread = new Thread(() => ProbePortParallel(port, ref foundPort, foundSignal, threadLock))
            {
                IsBackground = true,
                Name = $"NebulaSerialProbe_{port}"
            };
            scanThreads.Add(scanThread);
            scanThread.Start();
        }

        const int GlobalTimeoutMs = 10000;
        bool foundAny = foundSignal.WaitOne(GlobalTimeoutMs);

        if (foundAny && foundPort != null)
            return foundPort;

        Debug.LogWarning("[Nebula][Serial] No Nebula device found after parallel scan.");
        return null;
    }

    // Probes a single port on its own thread and signals if it finds Nebula.
    private void ProbePortParallel(string port, ref string foundPort, ManualResetEvent signal, object threadLock)
    {
        SerialPort probe = null;
        try
        {
            probe = BuildPort(port);
            if (probe.IsOpen) return;

            probe.Open();
            Thread.Sleep(BootDelayMs);
            probe.DiscardInBuffer();
            probe.ReadTimeout = ProbeTimeoutMs;

            probe.WriteLine(NebulaProtocol.QueryState);
            string received = probe.ReadLine();

            if (LooksLikeNebula(received))
            {
                lock (threadLock)
                {
                    if (foundPort == null) // first to find wins
                    {
                        foundPort = port;
                        Debug.Log($"[Nebula][Serial] {port} identified as Nebula (replied \"{received}\").");
                        signal.Set();
                    }
                }
            }
            else if (VerboseScan)
            {
                Debug.Log($"[Nebula][Serial] {port} replied: \"{received}\" (not Nebula)");
            }
        }
        catch (TimeoutException)
        {
            if (VerboseScan) Debug.Log($"[Nebula][Serial] {port}: timeout");
        }
        catch (Exception e)
        {
            if (VerboseScan) Debug.Log($"[Nebula][Serial] {port}: {e.GetType().Name}");
        }
        finally
        {
            try
            {
                if (probe != null && probe.IsOpen)
                {
                    probe.Close();
                    Thread.Sleep(PostProbeStabilizeMs);
                }
            }
            catch (Exception) { }
            probe?.Dispose();
        }
    }

    private static SerialPort BuildPort(string portName) => new SerialPort(portName, BaudRate)
    {
        Parity = Parity.None,
        StopBits = StopBits.One,
        DataBits = 8,
        DtrEnable = true,
        ReadTimeout = ProbeTimeoutMs,
        WriteTimeout = ProbeTimeoutMs
    };

    // =========================================================
    // I/O loops
    // =========================================================

    private void ReadLoop()
    {
        while (_shouldRun)
        {
            try
            {
                if (_serial != null && _serial.IsOpen && _serial.BytesToRead > 0)
                {
                    string line = _serial.ReadTo("\n").TrimEnd('\r');
                    if (line.Length > 0) OnMessageReceived?.Invoke(line);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            catch (TimeoutException) { /* normal, keep going */ }
            catch (Exception e)
            {
                if (_shouldRun)
                {
                    Debug.LogError($"[Nebula][Serial] Link lost while reading: {e.Message}");
                    HandleConnectionLost();
                }
                return;
            }
        }
    }

    private void WriteLoop()
    {
        while (_shouldRun)
        {
            try
            {
                if (_outgoingCommands.TryDequeue(out string command))
                {
                    _serial.Write(command + "\n");
                }
                else
                {
                    Thread.Sleep(WriteLoopIdleMs);
                }
            }
            catch (Exception e)
            {
                if (_shouldRun)
                {
                    Debug.LogError($"[Nebula][Serial] Link lost while writing: {e.Message}");
                    HandleConnectionLost();
                }
                return;
            }
        }
    }

    // =========================================================
    // Sending
    // =========================================================

    public void Send(string command)
    {
        if (!_isConnected)
        {
            Debug.LogWarning($"[Nebula][Serial] Command ignored (not connected): {command}");
            return;
        }
        _outgoingCommands.Enqueue(command);
    }

    // =========================================================
    // Disconnect / cleanup
    // =========================================================

    private void HandleConnectionLost()
    {
        bool wasConnected = _isConnected;
        _shouldRun = false;
        _isConnected = false;
        CleanupPort();
        if (wasConnected && _device != null)
        {
            _device.connected = false;
            OnDisconnected?.Invoke(_device);
        }
    }

    public void Disconnect()
    {
        if (!_isConnected && !_isScanning) return;

        _shouldRun = false;

        // Give the I/O threads a chance to finish their current iteration.
        _readingThread?.Join(300);
        _writingThread?.Join(300);

        bool wasConnected = _isConnected;
        _isConnected = false;
        CleanupPort();

        if (wasConnected && _device != null)
        {
            _device.connected = false;
            OnDisconnected?.Invoke(_device);
        }
    }

    private void CleanupPort()
    {
        try { if (_serial != null && _serial.IsOpen) _serial.Close(); } catch (Exception) { }
        _serial?.Dispose();
        _serial = null;
        while (_outgoingCommands.TryDequeue(out _)) { }
    }

    public void Dispose() => Disconnect();
}
#endif
