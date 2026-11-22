# CrashuTiles: notes for working on this repo

Custom Windows software for the Ulanzi D200 stream controller ("the dock"): live tiles and shortcuts.
C# / .NET 10, WinForms tray app. The only NuGet dependency is LibreHardwareMonitorLib (0.9.6), used by the app for sensors.

## Layout

- `src/CrashuTiles.Device`: the USB driver. It never draws; it takes PNG bytes.
  - `HidChannel`: one overlapped read handle and one synchronous write handle.
  - `D200Protocol`: framing and constants.
  - `ButtonZip`: builds the manifest and image ZIP.
  - `D200Device`: connect/reconnect, desired-state model, update scheduler and keep-alive.
- `src/CrashuTiles.App`: the tray app (assembly `CrashuTiles`).
  - `Deck/DeckController`: a single worker thread that owns pages, tiles and input.
  - `Tiles/Tiles.cs`: every tile type, plus `SensorFormat` (titles, units, scale, warning colours).
  - `Rendering/Painter`: fonts, text, icons, sparklines, gauges.
  - `Data/SensorHub`: LibreHardwareMonitor sampled every 1 s on a timer thread.
    - Tiles call `Get(key)`, which starts tracking that key, and read a snapshot with a 60-sample history.
    - Keys are aliases (the table in `SensorHub`) or raw LHM identifiers starting with `/`.
  - `Data/Batteries/*`: `BatteryHub` plus one source per vendor (see Battery facts).
  - `Data/MediaHub`: `GlobalSystemMediaTransportControlsSessionManager`, polled every 1 s.
    - It holds the current session plus a `MediaState` snapshot: title, artist, app, status, controls, timeline and an art `Bitmap`.
    - Commands go to the session's `Try*Async` methods.
    - Art is reloaded when the track key changes and retried up to five times, because some apps attach the thumbnail late.
    - Browsers give their own logo as the art.
  - `Data/AudioHub` + `Data/CoreAudio.cs`: Core Audio COM via `[ComImport]`, polled every 500 ms.
    - It reads the default output and mic (name, volume, mute) and lists active outputs every 5 s.
    - Switching the default uses the undocumented `IPolicyConfig` (`SetDefaultEndpoint` for all three roles), which is the only way and is what every audio switcher uses.
    - `AudioTile` handles the controls `output`, `mute`, `mic`, `volup`, `voldown` and `device`. `DeckController.AudioPage` (`__audio`) is the output picker.
    - Windows names look like "Alto-falantes (Realtek(R) Audio)". `AudioTile.SplitName` shows the part in parentheses as the main name.
  - `MultiSensorTile` ("sensors"): three sensors per key, the same row layout as `MultiBatteryTile`.
  - The maintainer's main page: the usage tile and the GPU temp gauge (both open the "sensors" page), the batteries, Explorer/Snip/More, and now playing on the small screen.
  - Media tiles: `NowPlayingTile` (`control` info, wide on the small screen) and `MediaControlTile`. Default taps are filled in by `DeckController.OnButton`.
  - `Actions/*`, `Config/*` (`DefaultConfig.cs` is the first-run config).
  - `Preview.cs`: `--preview out.png [page] [--default]` renders a page to an image with no device.
- `tools/probe`: the original PowerShell + C# 5 probe and benchmarks, loaded via Add-Type with no build step.
- `tools/SensorProbe`: `dotnet run --project tools/SensorProbe` dumps every sensor LHM can see. Use it to find raw ids and check what new hardware exposes.

## Battery facts (2026-11-18)

- `MultiBatteryTile` ("batteries") shows three devices from `BatteryHub.Devices()`, which is in stable order: by source, then name. `Tile.AssignGroups` numbers these tiles in key order, so a fourth device spills into the next one.
- A "batteries" tile without its own action opens the built-in page `DeckController.BatteryPageName` (`__batteries`). The page is generated from the devices known at that moment: Back plus one `battery` tile each. `--preview out.png __batteries` renders it.
- `Data/Batteries/BatteryHub` polls every source every 30 s on a timer thread. It caches the last level per device in `%APPDATA%\CrashuTiles\batteries.json`, so offline devices show their last level.
- **Logitech G502 LIGHTSPEED** (receiver `046D:C539`, slot 1):
  - It has only feature 0x1001 (voltage); 4056 mV is about 89% on the Li-ion curve.
  - We use the long-report collection (page 0xFF00, usage 2, 20 bytes) with software id 0x0A, alongside G HUB.
  - Receiver errors: 0x08 means an empty slot, **0x09 means the device is asleep or unreachable**. A sleeping mouse can't be discovered until it wakes.
  - **On its USB cable** it shows up as `046D:C08D` (HID++ slot 0xFF), and the receiver loses it. `BatteryHub` keeps one reading per source+name, preferring online and then the newest, so it isn't listed twice.
  - **Charging:** flags bit 7 is set (0x80) while charging. The measured voltage rises under charge (14% jumped to 83% when plugged in), so voltage-based percentages read high until the cable comes out. Flag changes are logged.
- **Corsair HS80:** reads through iCUE SDK v4 (`CorsairReadDeviceProperty`, `CDPI_BatteryLevel` = 9, an int at offset 8 of the 24-byte property struct). It's only listed while the headset is on. The DLL is not redistributable, so the user supplies it. Third-party control was already enabled in iCUE.
- **Bluetooth:** read via CfgMgr32 on present BTHENUM/BTHLE nodes. Untested with a device that reports its battery. **AirPods have no battery property in Windows.**
- **AirPods:** `AirPodsSource` uses the WinRT `BluetoothLEAdvertisementWatcher`, which is why the app's TFM is `net10.0-windows10.0.19041.0` and the exe now builds into that folder.
  - It listens for Apple manufacturer data (0x004C) of type 0x07. Byte 6 holds the two bud nibbles, swapped when `(byte5 & 0x20) == 0`. Byte 7 holds the charging bits (high nibble) and the case level (low nibble). Levels are 0..10 ×10%; 15 means unknown.
  - Bytes 3..4 are the Bluetooth PID, little-endian. Adverts are filtered to the PIDs of connected Apple devices, taken from `VID&0001004C_PID&xxxx` in BTHENUM ids. The maintainer's pair is PID 0x2027.
  - Without the filter, other Apple devices nearby produced bogus readings.
- **8BitDo** (`2DC8:3106` via dongle): XInput reports it as "wired", so there's no battery.
- `tools/BatteryProbe`: `dotnet run --project tools/BatteryProbe` dumps HID++ devices and battery features. Add `icue <dll>` to list iCUE devices.

## Sensor facts (Ryzen 7 5700X3D, RTX 4070, ASRock B450M, non-admin, LHM 0.9.6)

- **Read fine:** NVIDIA via NVAPI (everything), CPU load, memory, and network throughput. A full update takes about 80 ms, and `Computer.Open` takes about 1.4 s.
- **Need admin plus the PawnIO driver** (LHM swapped WinRing0 for PawnIO): CPU temperature, clocks and power, plus motherboard/Super I/O. Without them they read 0 and the CPU voltages are bogus. `SensorHub` turns those zeros into null, shown as "n/a".
- **Need admin:** storage (SMART), which is disabled for now.
- **Plan for CPU temperature:** an elevated helper started by a scheduled task, feeding the unelevated app over a named pipe. The user installs PawnIO themselves; anti-cheat concerns have been reported.

## Protocol facts (verified on the maintainer's unit, 2026-11-17)

- **Device:** `VID 2207` / `PID 0019`, manufacturer "Zkswe". Interface `MI_00` is the deck protocol: 1024-byte reports, plus a report-ID byte on Windows. `MI_01` is a keyboard used for the dock's standalone hotkeys; we ignore it.
- **Packet:** `7C 7C | cmd u16 BE | total length u32 LE | data`. The first packet holds 1016 data bytes; the rest of a payload continues in raw 1024-byte packets.
- **Commands:**
  - Set buttons `0x0001` (full layout) and partial update `0x000d`, both carrying a ZIP.
  - Small window data `0x0006`: `mode|cpu|mem|HH:mm:ss|gpu|24H|suffix`. This doubles as the keep-alive.
  - Brightness `0x000a`: ASCII number.
  - Input: `0x0101`, with data `[state][index][type][1 = press / 0 = release]`.
- **Slots:** key index = row*5+col, 0..12. Slot `3_2` is the small screen and **always** needs `SmallViewMode` in the manifest. Mode 2 is our own image, which goes in its `Icon`. The small screen is pressable and reports as index 13.
- **ZIP bug:** the byte at payload offset `1016 + 1024*N` must not be `0x00` or `0x7C`. `ButtonZip` pads with a dummy file until it's clear.
- **No ACK:** the dock never sent the `0x010b` ACK or the `0x0303` device info that other projects mention. Don't wait for either.
- **Pacing (measured, filmed in slow motion):**
  - All 13 keys sent flat out: the dock pushes back about 63 ms per write, skips frames, and the **last keys in the ZIP go black**.
  - All 13 keys every 150 ms: perfect.
  - One key at about 26 updates/s: fine.
  - Small screen alone, flat out at about 24 updates/s: fine.
  - Therefore `D200Device` waits `30 + 9 * cost` ms after an update (cost = keys + 2 for the small image), plus however long the write blocked once that exceeds 25 ms, and at least 250 ms after a full set.
- **Ulanzi Studio** (`UlanziDeck.exe`, with child `node.exe` and `QtWebEngineProcess.exe`) must be closed, or both apps drive the dock.

## Working here

- Build: `dotnet build` at the repo root. Check tile designs with `--preview`, then Read the PNG.
- On-device behaviour (flicker, black tiles, timing) can only be judged by a person looking at
  the dock. Ask the maintainer before anything that animates or floods it, and gate tests on a key press.
- The running exe locks `bin/`. Stop `CrashuTiles` before rebuilding.
- **Installed copy:** `tools/install.ps1` publishes a Release build to `%LOCALAPPDATA%\Programs\CrashuTiles`, makes a Start Menu shortcut and launches it through `explorer.exe`. That copy is what the maintainer runs day to day. "Start with Windows" in the tray writes `HKCU\...\Run`.
- **Claude Code desktop gotcha (2026-11-22):** Claude's shell runs inside the Claude **MSIX package container**.
  - Anything started from it (`Start-Process CrashuTiles.exe`) belongs to the container and is **killed when it recycles**. It looks like a silent exit, with no log line and no WER report.
  - `%APPDATA%` folders first created from inside the container are **redirected** to `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\…`.
  - Always launch the app with `Start-Process explorer.exe "<exe path>"` (or `tools/install.ps1`), which runs it outside the container.
  - The data was moved to the real `%APPDATA%\CrashuTiles`. The old private copy is `…\LocalCache\Roaming\CrashuTiles.migrated`.
  - Reading `%APPDATA%\CrashuTiles` from the shell now shows the real folder. Writing new files there from the shell may still be redirected.
- Exits are logged ("Stopped normally", "Process exiting", "Windows session ending", "Fatal: …"). A run that ends with none of these was killed from outside.
- Collaboration: Claude builds, the maintainer reviews. Don't commit or push unless asked.

## Roadmap

1. ~~Device layer, tiles, CPU/RAM, tray app~~ (v0.1)
2. ~~Sensor tiles via LibreHardwareMonitor, non-admin set~~ (v0.2). Still open: CPU temperature, board fans and disk temperatures through an elevated helper.
3. ~~Peripheral batteries: Logitech HID++, Corsair iCUE, Bluetooth, AirPods BLE, XInput~~ (v0.3)
4. ~~Media now playing and state-aware media keys~~ (v0.4). ~~Audio output switching, mute, mic mute, volume~~ (v0.5).
5. Settings web page served locally (plain HTML/JS), autostart with Windows.
   - The tile picker must offer **every** tile type, including ones not in the default layout. The maintainer removed the media control keys (`media` + `control`: `playpause`/`next`/`prev`) from his layout but wants to be able to add them back from the page.
