# CrashuTiles

Custom software for the **Ulanzi D200** stream controller: live tiles on its LCD keys
(CPU, GPU, RAM, network, clock, graphs) and shortcuts that go further than the stock app.

It replaces Ulanzi Studio and talks to the dock directly over USB. It needs no special drivers
for the dock. Hardware sensors come from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

> Not affiliated with or endorsed by Ulanzi.

## What it does

- **Sensor tiles:** any sensor as a big number with a one-minute sparkline, or as a ring gauge. Optional warning colours turn the tile orange or red above a threshold.
- **Sensors without admin rights:** CPU load (total and busiest core), RAM, network up/down, and the full GPU: load, temperatures, power, fans, clocks, VRAM.
- **Battery tiles:** wireless devices with a percentage, battery bar, charging bolt and low-battery colours:
  - **Logitech** mice and keyboards through HID++. This works alongside G HUB.
  - **Corsair** devices through iCUE.
  - **Bluetooth** devices: the same level the Windows Settings app shows.
  - **AirPods:** left, right and case, read from Apple's broadcasts.
  - **Xbox controllers.**

  A device that's asleep or off keeps showing its last level with "last seen …", even after a restart.
- **Now playing:** album art, title, artist and progress from whatever Windows shows in its media flyout (Spotify, browsers, players). On the small screen it lays out wide. Play/pause, next and previous keys follow the player's state and dim when the app doesn't support them.
- **Audio:**
  - The current output device with its volume. Pressing it opens a picker with one key per output, and choosing one makes it the default.
  - Mute and microphone-mute keys that turn red while muted.
  - Volume up and down keys.

  All of them follow changes made elsewhere, such as the volume wheel or the tray.
- **Graphs and a clock:** a graph of up to three sensors (on the small screen or a key) and a clock tile.
- **Shortcuts:** launch apps (their own icon shows on the key), open URLs, send hotkeys and media keys, and switch pages.
- **Tap or hold:** a key can do one thing on tap and another when held for half a second.
- **Pages:** folders of keys with a back button.
- **Small screen:** your own image (any tile drawn wide), or the firmware's built-in clock or stats. It also works as a 14th key.
- **Live config:** save `config.json` and the dock updates within half a second. Errors show as a tray notification, and the last good config stays on the dock.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- **Ulanzi Studio must be closed.** It talks to the dock too, so every press would fire twice.
  CrashuTiles warns you and offers to close it from its tray menu.

## Install

```bash
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

This builds a Release copy into `%LOCALAPPDATA%\Programs\CrashuTiles`, adds a **CrashuTiles** Start Menu shortcut and starts it.
Re-run it to update. Your config in `%APPDATA%\CrashuTiles` is kept.
To start it automatically, tick **Start with Windows** in the tray menu.

## Build and run (development)

```bash
dotnet build
```

```bash
src/CrashuTiles.App/bin/Debug/net10.0-windows10.0.19041.0/CrashuTiles.exe
```

It runs in the tray. Right-click the icon for brightness, editing the config, reloading and exit.
Double-click the icon to open the config.

To preview a page as an image without touching the dock:

```bash
CrashuTiles.exe --preview out.png main
```

Add `--default` to render the built-in default config instead of yours, or `--config file.json` to render another file.
Use `__batteries` as the page name to render the built-in battery page.

## Configuring

The config lives at `%APPDATA%\CrashuTiles\config.json` and is created on first run.
Comments are allowed. Keys are numbered by position:

```
row 0:   0   1   2   3   4
row 1:   5   6   7   8   9
row 2:  10  11  12  [ small ]
```

```jsonc
{
  "brightness": 80,
  "startPage": "main",
  "pages": {
    "main": {
      "0":  { "tile": "sensor", "sensor": "cpu.load", "warn": 80, "crit": 95 },
      "2":  { "tile": "sensor", "sensor": "gpu.temp", "style": "gauge", "warn": 75, "crit": 85 },
      "3":  { "label": "Calculator", "icon": "glyph:E8EF", "action": { "type": "launch", "path": "calc.exe" } },
      "6":  { "label": "Play / Pause", "icon": "glyph:E768", "color": "#2D9C6F",
              "action": { "type": "hotkey", "keys": "playpause" },
              "hold":   { "type": "hotkey", "keys": "next" } },
      "12": { "label": "More", "icon": "glyph:E712", "action": { "type": "page", "page": "tools" } },
      "small": { "tile": "graph", "sensors": ["cpu.load", "gpu.load", "ram.load"] }
    }
  }
}
```

| Field | Values |
|---|---|
| `tile` | `label` (default), `sensor`, `battery`, `batteries`, `media`, `clock`, `graph`; `cpu`, `ram` and `gpu` are shortcuts for sensor tiles |
| `sensors` (tile) | up to three sensors on one key (name, value and bar per row, like `batteries`); list them in `sensors`, default CPU, GPU and RAM load |
| `control` (audio) | `output` (default; pressing opens the output picker), `mute`, `mic`, `volup`, `voldown` (`step`, default 5) |
| `control` | media tiles: `info` (now playing, default; pressing it toggles play/pause), `playpause`, `next` or `prev` |
| `batteries` | three devices per tile. Every further `batteries` tile on the page shows the next three, and stays black when there's nothing left. Pressing one opens a page with one battery per key, unless the tile has its own `action` |
| `sensor` | an alias from the list below, or a raw LibreHardwareMonitor id like `/gpu-nvidia/0/temperature/0` |
| `style` | sensor tiles: `spark` (default) or `gauge` |
| `max` | top of the scale. The default is 100 for % and °C, otherwise the recent peak |
| `warn` / `crit` | the tile turns orange / red at or above this value |
| `sensors` | graph tiles: up to three sensors (default `cpu.load` and `ram.load`) |
| `battery` | battery tiles: `logitech`, `corsair`, `bluetooth`, `xinput` or `any` |
| `match` | battery tiles: part of the device name, e.g. `G502` or `AirPods`. Without it, the first device from that source is used |
| `label` | text under the icon, or alone when there's no icon |
| `icon` | `glyph:XXXX` (a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font) code), or a `.png` / `.jpg` / `.ico` / `.exe` path |
| `color` | accent colour `#RRGGBB` |
| `action` / `hold` | `launch` (`path`, `args`), `url` (`url`), `hotkey` (`keys`), `media` (`command`: `playpause`, `play`, `pause`, `next`, `prev`), `audio` (`command`: `mute`, `micmute`, `volup`, `voldown`, or `output` with `device`: part of a device name), `page` (`page`), `back`, `config` |
| `native` | small screen only: `clock`, `clock-date`, `clock-weekday`, `clock-full`, `dial`, `stats` |

**Sensor aliases**

| Group | Aliases |
|---|---|
| CPU | `cpu.load`, `cpu.coremax`, `cpu.temp`\*, `cpu.power`\*, `cpu.clock`\* |
| RAM | `ram.load`, `ram.used`, `ram.free` |
| GPU (NVIDIA, AMD, Intel) | `gpu.load`, `gpu.temp`, `gpu.hotspot`, `gpu.memtemp`, `gpu.power`, `gpu.clock`, `gpu.fan`, `gpu.vram`, `gpu.vramused` |
| Network | `net.down`, `net.up`: the busiest adapter. `net.down@Ethernet 2` picks a specific one |

\* CPU temperature, clocks and power, plus motherboard sensors, need CrashuTiles running as admin
**and** the [PawnIO](https://github.com/namazso/PawnIO.Setup) driver installed. Without them those tiles show "n/a".
Before installing PawnIO, check your games: some anti-cheat systems are reported to object to it.

**Battery sources**

| Source | Notes |
|---|---|
| `logitech` | HID++ 2.0 through a LIGHTSPEED/Unifying receiver or a direct connection. It reads features 0x1004, 0x1000 or 0x1001; voltage readings are converted to a percentage. Asleep devices are retried every 30 s |
| `corsair` | Needs iCUE running **and** Corsair's SDK DLL, which CrashuTiles doesn't ship because of Corsair's licence. Download the [iCUE SDK](https://github.com/CorsairOfficial/cue-sdk/releases) and copy `redist/x64/iCUESDK.x64_2019.dll` into `%APPDATA%\CrashuTiles`. iCUE reports no charging state |
| `bluetooth` | Connected devices whose battery Windows knows (`DEVPKEY_Bluetooth_Battery`), **plus AirPods/Beats**, read from Apple's Bluetooth LE broadcasts. Windows has no value for those. AirPods give left / right / case in 10% steps (shown on the tile, `+` = charging). They're sent only while the buds are out or the case is open, and only broadcasts matching the model of AirPods connected to this PC are used |
| `xinput` | Xbox controllers. XInput only has four steps, shown as 5 / 30 / 65 / 100 %. Controllers that report "wired", such as 8BitDo dongles, have no readable battery |

Hotkeys are written like `ctrl+shift+esc` or `win+d`. Any [Keys](https://learn.microsoft.com/dotnet/api/system.windows.forms.keys) name works,
plus the shortcuts `playpause`, `next`, `prev`, `stop`, `mute`, `volup` and `voldown`.
Windows won't let a non-elevated app send keys to an elevated window, such as an admin terminal.

## How it works

The D200 is a USB HID device (`VID 2207`, `PID 0019`). Interface 0 carries 1024-byte packets:

- **Key images** go up as a ZIP: a `manifest.json` plus 196×196 PNGs (458×196 for the small screen).
- **Presses and releases** come back as small input packets.

CrashuTiles batches every change into one upload and paces the uploads. Sent flat out, the
dock skips frames and blanks the last keys; spaced out, it is clean. [CLAUDE.md](CLAUDE.md) has the
measured details.

`tools/probe` holds the PowerShell/C# probe used to work out and measure the protocol.

## Credits

The wire protocol was reverse-engineered by [redphx/strmdck](https://github.com/redphx/strmdck)
and extended by [jcalado/companion-surface-d200](https://github.com/jcalado/companion-surface-d200)
(both MIT). CrashuTiles is a fresh implementation based on their findings.

## License

[MIT](LICENSE). Use it, fork it, or borrow the protocol code for your own D200 (or similar) software.
Keeping the copyright notice is the only requirement.
