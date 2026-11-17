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
  // tile:   label (default) | cpu | ram | clock | graph
  // icon:   "glyph:E768" (Segoe Fluent Icons code) or a .png/.jpg/.ico/.exe path.
  //         Launch keys without an icon show the app's own icon.
  // color:  accent colour "#RRGGBB"
  // action: what a tap does. hold: what holding for half a second does.
  //         { "type": "launch", "path": "notepad.exe", "args": "" }
  //         { "type": "url", "url": "https://example.com" }
  //         { "type": "hotkey", "keys": "ctrl+shift+esc" }   (also playpause, next, prev, mute, volup, voldown)
  //         { "type": "page", "page": "tools" }  |  { "type": "back" }  |  { "type": "config" }
  // small:  any tile drawn wide, or "native": clock | clock-date | clock-weekday | clock-full | dial | stats

  "brightness": 80,
  "startPage": "main",
  "pages": {
    "main": {
      "0":  { "tile": "cpu", "color": "#3FA9F5" },
      "1":  { "tile": "ram", "color": "#B57CFF" },
      "2":  { "tile": "clock", "color": "#F5A623" },
      "3":  { "label": "Calculator", "icon": "glyph:E8EF", "action": { "type": "launch", "path": "calc.exe" } },
      "4":  { "label": "Task Manager", "icon": "glyph:E9D9", "action": { "type": "hotkey", "keys": "ctrl+shift+esc" } },

      "5":  { "label": "Previous", "icon": "glyph:E892", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "prev" } },
      "6":  { "label": "Play / Pause", "icon": "glyph:E768", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "playpause" } },
      "7":  { "label": "Next", "icon": "glyph:E893", "color": "#2D9C6F", "action": { "type": "hotkey", "keys": "next" } },
      "8":  { "label": "Mute", "icon": "glyph:E74F", "color": "#C0504D", "action": { "type": "hotkey", "keys": "mute" } },
      "9":  { "label": "Desktop", "icon": "glyph:E7F4", "action": { "type": "hotkey", "keys": "win+d" } },

      "10": { "label": "Explorer", "action": { "type": "launch", "path": "explorer.exe" } },
      "11": { "label": "Snip", "icon": "glyph:E722", "action": { "type": "hotkey", "keys": "win+shift+s" } },
      "12": { "label": "More", "icon": "glyph:E712", "action": { "type": "page", "page": "tools" } },

      "small": { "tile": "graph" }
    },
    "tools": {
      "0":  { "label": "Back", "icon": "glyph:E72B", "color": "#F5A623", "action": { "type": "back" } },
      "1":  { "label": "Notepad", "action": { "type": "launch", "path": "notepad.exe" } },
      "2":  { "label": "Settings", "icon": "glyph:E713", "action": { "type": "url", "url": "ms-settings:" } },
      "3":  { "label": "Edit tiles", "icon": "glyph:E70F", "action": { "type": "config" } },
      "small": { "native": "clock-full" }
    }
  }
}
""";
}
