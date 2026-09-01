# Nebula 

Control a Nebula olfactory device from Unity over **Serial (USB)** or **BLE**.

The code is split in two independent layers:

- **Driver** (`Protocol/`, `Transports/`, `NebulaManager`, `NebulaDevice`) — talks to the
  device. Reusable on its own, no VR or gameplay dependency.
- **Odor layer** (`Odor/`) — optional Unity gameplay: trigger-based diffusion driven by the
  player's head. Uses the driver; you can ignore or replace it.

## Structure

```
Runtime/
├── Protocol/NebulaProtocol.cs        Firmware command language (single source of truth)
├── NebulaDevice.cs                   Connected-device info (passed to events)
├── Transports/
│   ├── INebulaTransport.cs           Transport contract
│   ├── NebulaSerialTransport.cs      Serial (Windows/Editor)
│   └── Ble/
│       ├── NebulaBleTransportBase.cs Shared BLE logic
│       └── NebulaBleWindowsTransport.cs  BLE via BleWinrtDll
├── NebulaManager.cs                  Entry point: 1 transport, events, typed API
└── Odor/
    ├── NebulaPlayerHead.cs           Marks the player's head as the odor detector
    └── NebulaOdorZone.cs             Diffuses while the head is inside its trigger
```

## Quick start

1. Add a `NebulaManager` to your scene (one is enough — it survives scene loads).
2. Pick the **Transport** (Serial or BLE) and set the **Device Name** (default `Nebula`).
3. To trigger smells by proximity: put `NebulaPlayerHead` on your VR camera, and
   `NebulaOdorZone` on each odor object (give it a trigger collider and pick an atomizer).

## BLE on Windows

BLE uses [BleWinrtDll](https://github.com/adabru/BleWinrtDll):

1. Import BleWinrtDll (x64 DLL + `BleApi.cs`); keep its asmdef **Auto Referenced**.
2. Add the define `NEBULA_BLE_WINRT` in *Player Settings → Scripting Define Symbols*.
3. Build target **x64**, Windows 10+ with Bluetooth on.

Without the define, the serial transport still works; BLE simply isn't created.

## Platform support

Windows / Unity Editor (serial + BLE).
