# CrashuTiles: notes for working on this repo

Custom Windows software for the Ulanzi D200 stream controller ("the dock"): live tiles and shortcuts.
C# / .NET 10, WinForms tray app, no NuGet dependencies so far.

## Layout

- `src/CrashuTiles.Device`: the USB driver. It never draws; it takes PNG bytes.
  - `HidChannel`: one overlapped read handle and one synchronous write handle.
  - `D200Protocol`: framing and constants.
  - `ButtonZip`: builds the manifest and image ZIP.
  - `D200Device`: connect/reconnect, desired-state model, update scheduler and keep-alive.
- `src/CrashuTiles.App`: the tray app (assembly `CrashuTiles`).
  - `Deck/DeckController`: a single worker thread that owns pages, tiles and input.
  - `Tiles/Tiles.cs`: every tile type.
  - `Rendering/Painter`: fonts, text, icons, sparklines.
  - `Actions/*`, `Config/*` (`DefaultConfig.cs` is the first-run config), `Data/SystemStats`.
  - `Preview.cs`: `--preview out.png [page] [--default]` renders a page to an image with no device.
- `tools/probe`: the original PowerShell + C# 5 probe and benchmarks, loaded via Add-Type with no build step.

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
- Collaboration: Claude builds, the maintainer reviews. Don't commit or push unless asked.

## Roadmap

1. ~~Device layer, tiles, CPU/RAM, tray app~~ (v0.1)
2. Hardware sensors via LibreHardwareMonitor: temperatures, GPU, fans. Needs admin, so likely an elevated helper.
3. Peripheral batteries: Logitech G502 LIGHTSPEED (HID++), Corsair HS80, and Bluetooth devices through Windows.
4. Media now playing (GlobalSystemMediaTransportControls), audio device switching, toggles that show state.
5. Settings web page served locally (plain HTML/JS), autostart with Windows.
