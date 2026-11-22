using System.Collections.Concurrent;
using System.Text;
using CrashuTiles.Device;

namespace CrashuTiles.Data.Batteries;

/// <summary>
/// Logitech wireless devices over HID++ 2.0, through a LIGHTSPEED/Unifying receiver (device
/// slots 1..6) or plugged in directly (slot 0xFF). Works alongside G HUB: every request carries
/// our own software id, so answers can't be confused with G HUB's, and we only read.
///
/// Battery features tried, best first: 0x1004 unified battery (percent), 0x1000 battery status
/// (percent), then 0x1001 battery voltage (mV, turned into a percentage with a Li-ion curve).
/// The G502 LIGHTSPEED only has 0x1001.
/// </summary>
sealed class LogitechSource : IBatterySource
{
    const byte SwId = 0x0A;
    const int TimeoutMs = 400;
    static readonly TimeSpan KeepAsleepValue = TimeSpan.FromMinutes(20);

    public string Kind => "logitech";

    sealed class Channel(string path, FileStream stream, int outLen) : IDisposable
    {
        public readonly string Path = path;
        public readonly FileStream Stream = stream;
        public readonly int OutLen = outLen;
        public readonly BlockingCollection<byte[]> Reports = new();
        public bool Dead;
        public void Dispose() { Dead = true; try { Stream.Dispose(); } catch { } }
    }

    sealed class Known
    {
        public string Name = "Logitech device";
        public byte BatteryFeature;  // feature index, 0 = none
        public ushort BatteryKind;   // 0x1004 / 0x1000 / 0x1001
        public BatteryReading? Last;
        public int LastFlags = -1;
    }

    readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, byte), Known> _known = new();

    public IReadOnlyList<BatteryReading> Poll()
    {
        OpenNewChannels();
        var result = new List<BatteryReading>();
        foreach (var ch in _channels.Values.ToList())
        {
            if (ch.Dead) { _channels.Remove(ch.Path); continue; }
            bool receiver = ch.Path.Contains("pid_c5", StringComparison.OrdinalIgnoreCase); // receivers are 046D:C5xx
            byte[] slots = receiver ? [1, 2, 3, 4, 5, 6] : [0xFF];
            foreach (byte slot in slots)
            {
                var key = (ch.Path, slot);
                _known.TryGetValue(key, out var known);
                if (known == null)
                {
                    // Unknown slot: ping it. Receivers answer "unknown device" right away for empty slots.
                    if (Request(ch, slot, 0x00, 1, 0, 0, 0x5A) == null) continue;
                    known = Discover(ch, slot);
                    if (known == null) continue;
                    _known[key] = known;
                }
                if (known.BatteryFeature == 0) continue;
                var reading = ReadBattery(ch, slot, known);
                if (reading != null) known.Last = reading;
                else if (known.Last != null)
                    // Asleep or switched off: keep the last value for a while, flagged offline after that.
                    reading = known.Last with { Online = DateTime.Now - known.Last.LastSeen < KeepAsleepValue };
                if (reading != null) result.Add(reading);
            }
        }
        return result;
    }

    void OpenNewChannels()
    {
        foreach (var path in HidDevices.Paths())
        {
            string p = path.ToLowerInvariant();
            if (!p.Contains("vid_046d") || _channels.ContainsKey(path)) continue;
            var h = HidDevices.OpenShared(path);
            if (h == null) continue;
            var caps = HidDevices.GetCaps(h);
            // The HID++ long-report collection: vendor page 0xFF00, usage 2, 20-byte reports.
            if (caps.UsagePage != 0xFF00 || caps.Usage != 0x0002 || caps.OutputLength != 20) { h.Dispose(); continue; }
            var ch = new Channel(path, new FileStream(h, FileAccess.ReadWrite, 0, isAsync: true), caps.OutputLength);
            _channels[path] = ch;
            _ = ReadLoop(ch, caps.InputLength);
            Log.Info("HID++ channel opened: " + path);
        }
    }

    static async Task ReadLoop(Channel ch, int inLen)
    {
        var buf = new byte[inLen];
        try
        {
            while (!ch.Dead)
            {
                int n = await ch.Stream.ReadAsync(buf).ConfigureAwait(false);
                if (n > 0) ch.Reports.Add(buf[..n]);
            }
        }
        catch { ch.Dead = true; }
    }

    static byte[]? Request(Channel ch, byte slot, byte feature, byte function, params byte[] args)
    {
        if (ch.Dead) return null;
        var msg = new byte[ch.OutLen];
        msg[0] = 0x11; msg[1] = slot; msg[2] = feature; msg[3] = (byte)((function << 4) | SwId);
        args.CopyTo(msg, 4);
        while (ch.Reports.TryTake(out _)) { }
        try { ch.Stream.Write(msg); }
        catch { ch.Dead = true; return null; }
        var deadline = Environment.TickCount64 + TimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!ch.Reports.TryTake(out var r, 30)) continue;
            if (r.Length < 5 || r[1] != slot) continue;
            if (r[2] == feature && r[3] == msg[3]) return r[4..];
            if ((r[2] == 0xFF || r[2] == 0x8F) && r[3] == feature && r[4] == msg[3]) return null; // error reply
        }
        return null;
    }

    static byte FeatureIndex(Channel ch, byte slot, ushort id) =>
        Request(ch, slot, 0x00, 0, (byte)(id >> 8), (byte)id) is { } r ? r[0] : (byte)0;

    static Known? Discover(Channel ch, byte slot)
    {
        var k = new Known();
        byte nameIdx = FeatureIndex(ch, slot, 0x0005);
        if (nameIdx != 0 && Request(ch, slot, nameIdx, 0) is { } count)
        {
            var name = new StringBuilder();
            while (name.Length < count[0] && Request(ch, slot, nameIdx, 1, (byte)name.Length) is { } part)
            {
                string s = Encoding.ASCII.GetString(part).TrimEnd('\0');
                if (s.Length == 0) break;
                name.Append(s);
            }
            if (name.Length > 0) k.Name = name.ToString().Trim();
        }
        foreach (ushort f in new ushort[] { 0x1004, 0x1000, 0x1001 })
        {
            byte idx = FeatureIndex(ch, slot, f);
            if (idx != 0) { k.BatteryFeature = idx; k.BatteryKind = f; break; }
        }
        Log.Info($"Logitech slot {slot}: {k.Name}, battery feature 0x{k.BatteryKind:X4}");
        return k;
    }

    BatteryReading? ReadBattery(Channel ch, byte slot, Known k)
    {
        byte function = k.BatteryKind == 0x1004 ? (byte)1 : (byte)0;
        var d = Request(ch, slot, k.BatteryFeature, function);
        if (d == null) return null;
        int percent; bool charging;
        switch (k.BatteryKind)
        {
            case 0x1004: percent = d[0]; charging = d[2] is 1 or 2; break;      // soc, level, charging status
            case 0x1000: percent = d[0]; charging = d[2] is 1 or 2 or 3; break; // level, next, status
            default:                                                              // 0x1001: voltage + flags
                int mv = (d[0] << 8) | d[1];
                percent = VoltageToPercent(mv);
                // Flags bit 7 = external power / charging. While charging the measured voltage
                // rises, so the percentage reads high until the cable comes out.
                charging = (d[2] & 0x80) != 0;
                if (d[2] != k.LastFlags) { k.LastFlags = d[2]; Log.Info($"{k.Name}: {mv} mV, flags 0x{d[2]:X2}"); }
                break;
        }
        return new BatteryReading(Kind, k.Name, Math.Clamp(percent, 0, 100), charging, true, DateTime.Now);
    }

    // Li-ion discharge curve used by open-source Logitech tools for 0x1001 devices.
    static readonly (int Mv, int Pct)[] Curve =
    [
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    ];

    static int VoltageToPercent(int mv)
    {
        if (mv >= Curve[0].Mv) return 100;
        for (int i = 1; i < Curve.Length; i++)
        {
            if (mv < Curve[i].Mv) continue;
            var (hiMv, hiPct) = Curve[i - 1];
            var (loMv, loPct) = Curve[i];
            return loPct + (int)Math.Round((mv - loMv) * (hiPct - loPct) / (double)(hiMv - loMv));
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var ch in _channels.Values) ch.Dispose();
        _channels.Clear();
    }
}
