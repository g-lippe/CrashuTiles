# CrashuTiles

Custom software for the **Ulanzi D200** stream controller: live tiles on its LCD keys
(CPU, GPU, RAM, network, clock, graphs) and shortcuts that go further than the stock app.

It replaces Ulanzi Studio and talks to the dock directly over USB. It needs no special drivers
for the dock. Hardware sensors come from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

> Not affiliated with or endorsed by Ulanzi.

## What it does

- **Sensor tiles:** any sensor as a big number with a one-minute sparkline, or as a ring gauge. Optional warning colours turn the tile orange or red above a threshold.
- **Sensors without admin rights:** CPU load (total and busiest core), RAM, network up/down, and the full GPU: load, temperatures, power, fans, clocks, VRAM.
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

## Build and run

```bash
dotnet build
```

```bash
src/CrashuTiles.App/bin/Debug/net10.0-windows/CrashuTiles.exe
```

It runs in the tray. Right-click the icon for brightness, editing the config, reloading and exit.
Double-click the icon to open the config.

To preview a page as an image without touching the dock:

```bash
CrashuTiles.exe --preview out.png main
```

Add `--default` to render the built-in default config instead of yours.

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
| `tile` | `label` (default), `sensor`, `clock`, `graph`; `cpu`, `ram` and `gpu` are shortcuts for sensor tiles |
| `sensor` | an alias from the list below, or a raw LibreHardwareMonitor id like `/gpu-nvidia/0/temperature/0` |
| `style` | sensor tiles: `spark` (default) or `gauge` |
| `max` | top of the scale. The default is 100 for % and °C, otherwise the recent peak |
| `warn` / `crit` | the tile turns orange / red at or above this value |
| `sensors` | graph tiles: up to three sensors (default `cpu.load` and `ram.load`) |
| `label` | text under the icon, or alone when there's no icon |
| `icon` | `glyph:XXXX` (a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font) code), or a `.png` / `.jpg` / `.ico` / `.exe` path |
| `color` | accent colour `#RRGGBB` |
| `action` / `hold` | `launch` (`path`, `args`), `url` (`url`), `hotkey` (`keys`), `page` (`page`), `back`, `config` |
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
