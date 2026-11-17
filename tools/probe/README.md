# D200 probe

The scripts used to discover and measure the dock's protocol. They're plain Windows PowerShell
5.1 + C# 5 compiled on the fly with Add-Type, so there's nothing to build or install.
Close Ulanzi Studio and CrashuTiles first.

| Command | What it does |
|---|---|
| `hidcaps.ps1` | Lists the dock's HID interfaces with their usage pages and report sizes |
| `probe.ps1 -Listen 30` | Uploads numbered tiles, times uploads, then logs presses for 30 s and repaints pressed keys |
| `probe.ps1 -Listen -1` | Benchmark 1: one key flat out, one key at 10 fps, all keys flat out, small-screen graph |
| `probe.ps1 -Listen -2` | Benchmark 2: all keys flat out vs paced at 150 ms, small screen at 4 fps vs flat out |

Both benchmarks wait for a key press on the dock before starting.
