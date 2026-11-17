using System.Runtime.InteropServices;

namespace CrashuTiles.Actions;

/// <summary>
/// Sends key combos like "ctrl+shift+esc" or "playpause" with SendInput.
/// Windows blocks input into elevated windows unless CrashuTiles is elevated too.
/// </summary>
static class KeySender
{
    static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Keys.ControlKey, ["control"] = Keys.ControlKey, ["shift"] = Keys.ShiftKey,
        ["alt"] = Keys.Menu, ["win"] = Keys.LWin, ["windows"] = Keys.LWin,
        ["esc"] = Keys.Escape, ["del"] = Keys.Delete, ["ins"] = Keys.Insert, ["enter"] = Keys.Return,
        ["space"] = Keys.Space, ["tab"] = Keys.Tab, ["backspace"] = Keys.Back,
        ["pgup"] = Keys.PageUp, ["pgdn"] = Keys.PageDown, ["printscreen"] = Keys.PrintScreen,
        ["up"] = Keys.Up, ["down"] = Keys.Down, ["left"] = Keys.Left, ["right"] = Keys.Right,
        ["playpause"] = Keys.MediaPlayPause, ["next"] = Keys.MediaNextTrack, ["prev"] = Keys.MediaPreviousTrack,
        ["previous"] = Keys.MediaPreviousTrack, ["stop"] = Keys.MediaStop,
        ["mute"] = Keys.VolumeMute, ["volup"] = Keys.VolumeUp, ["voldown"] = Keys.VolumeDown,
    };

    // Keys that need KEYEVENTF_EXTENDEDKEY to be read correctly.
    static readonly HashSet<Keys> Extended =
    [
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
        Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.NumLock, Keys.Divide,
        Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Apps, Keys.PrintScreen,
    ];

    public static List<Keys> Parse(string combo)
    {
        var keys = new List<Keys>();
        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Aliases.TryGetValue(raw, out var k)) keys.Add(k);
            else if (raw.Length == 1 && char.IsDigit(raw[0])) keys.Add(Keys.D0 + (raw[0] - '0'));
            else if (!int.TryParse(raw, out _) && Enum.TryParse(raw, true, out k)) keys.Add(k);
            else throw new FormatException($"Unknown key '{raw}' in \"{combo}\"");
        }
        return keys;
    }

    public static void Send(string combo)
    {
        var keys = Parse(combo);
        var inputs = new List<INPUT>();
        foreach (var k in keys) inputs.Add(Key(k, up: false));
        for (int i = keys.Count - 1; i >= 0; i--) inputs.Add(Key(keys[i], up: true));
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) Log.Info($"SendInput sent {sent}/{inputs.Count} events for \"{combo}\" (error {Marshal.GetLastWin32Error()})");
    }

    static INPUT Key(Keys k, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)k,
                dwFlags = (up ? 2u : 0u) | (Extended.Contains(k) ? 1u : 0u),
            },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
