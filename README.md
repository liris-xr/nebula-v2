# Nebula (`com.lirisxr.nebula`)

Unity package to control a **Nebula** olfactory device (4-channel atomizer diffuser) from Unity,
over Serial (USB) or BLE. Includes a desktop demo and a VR sample for distance-driven odor
diffusion, ported from the original
[`liris-xr/nebula-v2`](https://github.com/liris-xr/nebula-v2).

## Relation to the BLE Bridge

Nebula supports two transports (`Serial` and `Ble`, chosen on `NebulaManager` or via `Connect()`).
The BLE transport does **not** talk to BLE hardware itself: it is wired through the shared
**[`com.lirisxr.blebridge`](https://github.com/liris-xr/multisensory-bridge)** package,
which this package depends on (`com.lirisxr.blebridge` is declared in `package.json`, so UPM
installs it automatically).

How the two relate:

- **The bridge owns the native stack.** `NebulaBleTransport` requests an `IBleLink` from the
  bridge's `BleConnectionManager` with a `BleDeviceProfile` for the advertised name `"Nebula"`,
  service `0x180C` and characteristic `0x2A56`. Scanning, connecting, subscribing and writing
  are all done by the bridge; Nebula only sends commands and receives firmware messages over
  the link.
- **No native DLL shipping.** Nebula ships no `BleWinrtDll.dll` and no Android jar, and never
  calls the native `Quit()`. Before the bridge existed, Nebula and StimulHeat each bound the
  same Windows BLE library and could not coexist in one project (duplicate DLL, and one
  package's `Quit()` tore the other's connection down). The bridge is now the single owner, so
  Nebula and StimulHeat can be used together.
- **Automatic platform coverage.** Because the bridge picks its backend from the build target
  (Windows/Editor: `BleWinrtDll`; Android: Velorexe), Nebula's BLE transport works on Windows
  and Android unchanged, with no scripting defines.
- **Shared scan.** The bridge runs one scan and routes devices by name + service UUID, so the
  Nebula device is discovered alongside StimulHeat's devices without competing scanners.
- **Serial is independent.** The `Serial` transport (`NebulaSerialTransport`) does not involve
  the bridge at all.

If you use BLE, keep the bridge installed; see the bridge repo for platform setup (Android
permissions manifest wizard, entry point, etc.).

## Installation

Unity 2022.3+. Add this package via the Unity Package Manager (git URL or local path). The BLE
bridge dependency is pulled in automatically.

## Usage

Add a `NebulaManager` to the scene, then:

```csharp
nebulaManager.transport = NebulaTransportKind.Ble;   // or Serial
nebulaManager.deviceName = "Nebula";                  // advertised BLE name / serial port
nebulaManager.Connect();

nebulaManager.StartDiffusion(NebulaAtomizer.L1);
nebulaManager.StopDiffusion(NebulaAtomizer.L2);
nebulaManager.Configure(NebulaAtomizer.L1, periodMs: 1000, dutyCyclePercent: 50);
nebulaManager.StopAll();
```

`onDeviceConnected` / `onDeviceDisconnected` / `onMessageReceived` are UnityEvents; commands can
also be sent directly with `SendCommand(string)`. BLE connection, reconnection and incoming
message queuing are handled internally (`autoReconnect`, `reconnectDelaySeconds`).

## Samples

Import from the Package Manager ("Import samples" on Nebula):

- **Demo** (`Samples~/Demo`) — `NebulaBleDemo.cs`, a desktop OnGUI control of the 4 atomizers
  with period/duty sliders, switching transport from the toolbar. Works on Windows and Android
  unchanged.
- **VR Demo** (`Samples~/VR`) — distance-triggered odor diffusion, no XR Toolkit dependency and
  no physics setup: diffusion is driven purely by the distance between the player's head and the
  zone (head = the scene's main camera, or `headOverride`).
  - `NebulaVRBootstrap` — put on any scene GameObject. Calls `NebulaManager.Connect()` on start
    with `transport = Ble`, `deviceName`, and `autoReconnect = true`.
  - `NebulaOdorZone` — put on an odor-emitting object. While the head is within
    `triggerDistance` metres, it starts diffusion on the chosen `Atomizer`. Two modes:
    `Binary` (fixed duty) and `Linear` (duty scales with head-to-object distance between
    `distanceAtMaxDuty` and `distanceAtMinDuty`, updated every `updateInterval`).

VR setup:
1. In the scene, add a `NebulaManager` and a `NebulaVRBootstrap` (with the device name).
2. On each object that emits odor, add `NebulaOdorZone`, then set the `Atomizer`, `Mode`,
   `Trigger Distance`, period and duty fields.
3. Run on Android (or Windows). When the player's head comes within `Trigger Distance` of an
   odor zone, the Nebula atomizer diffuses; when it moves away, it stops.
