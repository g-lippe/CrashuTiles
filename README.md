# CrashuTiles

Custom software for the **Ulanzi D200** stream controller: live tiles on its LCD keys
(CPU, RAM, clock, graphs) and shortcuts that go further than the stock app.

It replaces Ulanzi Studio and talks to the dock directly over USB. It needs no drivers
and no extra libraries.

> Not affiliated with or endorsed by Ulanzi.

## What it does (v0.1)

- **Live tiles:** CPU and RAM load with a one-minute sparkline, a clock, and a CPU/RAM graph on the small screen.
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
      "0":  { "tile": "cpu" },
      "3":  { "label": "Calculator", "icon": "glyph:E8EF", "action": { "type": "launch", "path": "calc.exe" } },
      "6":  { "label": "Play / Pause", "icon": "glyph:E768", "color": "#2D9C6F",
              "action": { "type": "hotkey", "keys": "playpause" },
              "hold":   { "type": "hotkey", "keys": "next" } },
      "12": { "label": "More", "icon": "glyph:E712", "action": { "type": "page", "page": "tools" } },
      "small": { "tile": "graph" }
    }
  }
}
```

| Field | Values |
|---|---|
| `tile` | `label` (default), `cpu`, `ram`, `clock`, `graph` |
| `label` | text under the icon, or alone when there's no icon |
| `icon` | `glyph:XXXX` (a [Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font) code), or a `.png` / `.jpg` / `.ico` / `.exe` path |
| `color` | accent colour `#RRGGBB` |
| `action` / `hold` | `launch` (`path`, `args`), `url` (`url`), `hotkey` (`keys`), `page` (`page`), `back`, `config` |
| `native` | small screen only: `clock`, `clock-date`, `clock-weekday`, `clock-full`, `dial`, `stats` |

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
