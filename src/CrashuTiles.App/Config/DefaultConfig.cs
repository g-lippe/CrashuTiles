namespace CrashuTiles.Config;

static class DefaultConfig
{
    public const string Json = """
{
  // CrashuTiles config. Save the file and the dock updates on its own.
  //
  // Keys are "0".."12" (index = row * 5 + col) plus "small", the wide screen:
  //   row 0:   0   1   2   3   4
  //   row 1:   5   6   7   8   9
  //   row 2:  10  11  12  [ small ]
  //
  // tile:    label (default) | sensor | clock | graph   (cpu, ram, gpu = sensor shortcuts)
  // sensor:  cpu.load cpu.coremax cpu.temp* cpu.power* cpu.clock*  ram.load ram.used ram.free
  //          gpu.load gpu.temp gpu.hotspot gpu.memtemp gpu.power gpu.clock gpu.fan gpu.vram gpu.vramused
  //          net.down net.up (busiest adapter; "net.down@Ethernet 2" picks one)
  //          or a raw LibreHardwareMonitor id like "/gpu-nvidia/0/temperature/0"
  //          (* needs CrashuTiles running as admin plus the PawnIO driver)
  // style:   spark (default) | gauge      max: top of the scale      warn / crit: orange / red from this value
  // sensors: graph tiles, up to three, e.g. ["cpu.load", "gpu.load", "ram.load"]
  // icon:    "glyph:E768" (Segoe Fluent Icons code) or a .png/.jpg/.ico/.exe path.
  //          Launch keys without an icon show the app's own icon.
  // color:   accent colour "#RRGGBB"
  // action:  what a tap does. hold: what holding for half a second does.
  //          { "type": "launch", "path": "notepad.exe", "args": "" }
  //          { "type": "url", "url": "https://example.com" }
  //          { "type": "hotkey", "keys": "ctrl+shift+esc" }   (also playpause, next, prev, mute, volup, voldown)
  //          { "type": "page", "page": "tools" }  |  { "type": "back" }  |  { "type": "config" }
  // small:   any tile drawn wide, or "native": clock | clock-date | clock-weekday | clock-full | dial | stats

  "brightness": 80,
  "startPage": "main",
  "pages": {
    "main": {
      "0":  { "tile": "sensor", "sensor": "cpu.load", "warn": 80, "crit": 95 },
      "1":  { "tile": "sensor", "sensor": "gpu.load", "warn": 90 },
      "2":  { "tile": "sensor", "sensor": "gpu.temp", "style": "gauge", "warn": 75, "crit": 85 },
      "3":  { "tile": "sensor", "sensor": "ram.load", "warn": 85 },
      "4":  { "tile": "clock", "color": "#F5A623" },

      "5":  { "label": "Previous", "icon": "glyph:E892", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "prev" } },
      "6":  { "label": "Play / Pause", "icon": "glyph:E768", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "playpause" } },
      "7":  { "label": "Next", "icon": "glyph:E893", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "next" } },
      "8":  { "label": "Mute", "icon": "glyph:E74F", "color": "#C0504D", "action": { "type": "hotkey", "keys": "mute" } },
      "9":  { "label": "Desktop", "icon": "glyph:E7F4", "action": { "type": "hotkey", "keys": "win+d" } },

      "10": { "label": "Explorer", "action": { "type": "launch", "path": "explorer.exe" } },
      "11": { "label": "Snip", "icon": "glyph:E722", "action": { "type": "hotkey", "keys": "win+shift+s" } },
      "12": { "label": "More", "icon": "glyph:E712", "action": { "type": "page", "page": "tools" } },

      "small": { "tile": "graph", "sensors": ["cpu.load", "gpu.load", "ram.load"] }
    },
    "tools": {
      "0":  { "label": "Back", "icon": "glyph:E72B", "color": "#F5A623", "action": { "type": "back" } },
      "1":  { "label": "Sensors", "icon": "glyph:E9D9", "color": "#3FA9F5", "action": { "type": "page", "page": "sensors" } },
      "2":  { "label": "Calculator", "icon": "glyph:E8EF", "action": { "type": "launch", "path": "calc.exe" } },
      "3":  { "label": "Task Manager", "icon": "glyph:E7EF", "action": { "type": "hotkey", "keys": "ctrl+shift+esc" } },
      "4":  { "label": "Notepad", "action": { "type": "launch", "path": "notepad.exe" } },
      "5":  { "label": "Settings", "icon": "glyph:E713", "action": { "type": "url", "url": "ms-settings:" } },
      "6":  { "label": "Edit tiles", "icon": "glyph:E70F", "action": { "type": "config" } },
      "small": { "native": "clock-full" }
    },
    "sensors": {
      "0":  { "label": "Back", "icon": "glyph:E72B", "color": "#F5A623", "action": { "type": "back" } },
      "1":  { "tile": "sensor", "sensor": "cpu.coremax", "style": "gauge" },
      "2":  { "tile": "sensor", "sensor": "cpu.temp" },
      "3":  { "tile": "sensor", "sensor": "net.down" },
      "4":  { "tile": "sensor", "sensor": "net.up" },
      "5":  { "tile": "sensor", "sensor": "gpu.hotspot", "warn": 85, "crit": 95 },
      "6":  { "tile": "sensor", "sensor": "gpu.memtemp", "warn": 90, "crit": 100 },
      "7":  { "tile": "sensor", "sensor": "gpu.power", "max": 200 },
      "8":  { "tile": "sensor", "sensor": "gpu.fan", "style": "gauge", "max": 3000 },
      "9":  { "tile": "sensor", "sensor": "gpu.vram", "style": "gauge" },
      "10": { "tile": "sensor", "sensor": "gpu.clock" },
      "11": { "tile": "sensor", "sensor": "ram.used" },
      "12": { "tile": "sensor", "sensor": "gpu.vramused" },
      "small": { "tile": "graph", "sensors": ["gpu.temp", "gpu.hotspot", "gpu.memtemp"] }
    }
  }
}
""";
}
