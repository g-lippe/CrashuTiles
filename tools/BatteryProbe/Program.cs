// Probes Logitech HID++ 2.0 devices behind a receiver for their battery features.
// Usage: dotnet run --project tools/BatteryProbe [vid_pid, default 046d_c539]
// Safe alongside G HUB: requests carry our own software id and only read state.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

//        dotnet run --project tools/BatteryProbe icue path\to\iCUESDK.x64_2019.dll
if (args.Length == 2 && args[0] == "icue") { ICue.Probe(args[1]); return; }

string vp = (args.Length > 0 ? args[0] : "046d_c539").ToLowerInvariant();
string match = $"vid_{vp[..4]}&pid_{vp[5..]}";
const byte SwId = 0x0A;

var reports = new BlockingCollection<byte[]>();
var channels = new List<(string Path, FileStream Stream, int InLen, int OutLen)>();
foreach (var path in Hid.Paths().Where(p => p.ToLowerInvariant().Contains(match)))
{
    var h = Hid.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
    if (h.IsInvalid) { Console.WriteLine($"cannot open {Short(path)} ({Marshal.GetLastWin32Error()})"); continue; }
    var (usagePage, usage, inLen, outLen) = Hid.Caps(h);
    Console.WriteLine($"{Short(path)}  page 0x{usagePage:X4} usage 0x{usage:X4} in {inLen} out {outLen}");
    if (usagePage < 0xFF00) { h.Dispose(); continue; } // only the vendor (HID++) collections
    var fs = new FileStream(h, FileAccess.ReadWrite, 0, true);
    channels.Add((path, fs, inLen, outLen));
    _ = Task.Run(async () =>
    {
        var buf = new byte[inLen];
        try { while (true) { int n = await fs.ReadAsync(buf); if (n > 0) reports.Add(buf[..n]); } }
        catch { }
    });
}
var shortCh = channels.FirstOrDefault(c => c.OutLen == 7);
var longCh = channels.FirstOrDefault(c => c.OutLen == 20);
if (shortCh.Stream == null && longCh.Stream == null) { Console.WriteLine("No HID++ collections found"); return; }
Console.WriteLine();

byte[]? Request(byte dev, byte feat, byte func, params byte[] p)
{
    bool useLong = shortCh.Stream == null || p.Length > 3;
    var ch = useLong ? longCh : shortCh;
    var msg = new byte[ch.OutLen];
    msg[0] = (byte)(useLong ? 0x11 : 0x10); msg[1] = dev; msg[2] = feat; msg[3] = (byte)((func << 4) | SwId);
    p.CopyTo(msg, 4);
    while (reports.TryTake(out _)) { }
    ch.Stream.Write(msg);
    var deadline = DateTime.UtcNow.AddMilliseconds(600);
    while (DateTime.UtcNow < deadline)
    {
        if (!reports.TryTake(out var r, 50)) continue;
        if (Environment.GetEnvironmentVariable("HIDPP_RAW") == "1") Console.WriteLine($"    rx {Convert.ToHexString(r)}");
        if (r.Length < 4 || r[1] != dev) continue;
        if (r[2] == feat && r[3] == msg[3]) return r[4..];                      // our answer
        if ((r[2] == 0xFF || r[2] == 0x8F) && r[3] == feat && r[4] == msg[3])    // HID++ 2.0 / 1.0 error
        { Console.WriteLine($"    error 0x{r[5]:X2} for feature index {feat} func {func}"); return null; }
    }
    return null;
}

byte? FeatureIndex(byte dev, ushort id)
{
    var r = Request(dev, 0x00, 0, (byte)(id >> 8), (byte)id);
    return r == null || r[0] == 0 ? null : r[0];
}

for (byte dev = 1; dev <= 6; dev++)
{
    var ping = Request(dev, 0x00, 1, 0, 0, 0x5A);
    if (ping == null) continue;
    Console.WriteLine($"Device {dev}: HID++ {ping[0]}.{ping[1]}");

    if (FeatureIndex(dev, 0x0005) is byte nameIdx && Request(dev, nameIdx, 0) is { } cnt)
    {
        var name = new StringBuilder();
        while (name.Length < cnt[0] && Request(dev, nameIdx, 1, (byte)name.Length) is { } part)
            name.Append(Encoding.ASCII.GetString(part).TrimEnd('\0'));
        Console.WriteLine($"    name: {name}");
    }

    foreach (ushort feat in new ushort[] { 0x1004, 0x1001, 0x1000, 0x1F20 })
    {
        if (FeatureIndex(dev, feat) is not byte idx) { Console.WriteLine($"    feature 0x{feat:X4}: no"); continue; }
        byte func = feat == 0x1004 ? (byte)1 : (byte)0;
        var r = Request(dev, idx, func);
        string raw = r == null ? "no answer" : Convert.ToHexString(r[..Math.Min(8, r.Length)]);
        string meaning = (feat, r) switch
        {
            (0x1004, { } d) => $"charge {d[0]}%, level bits 0x{d[1]:X2}, charging status {d[2]}, external power {d[3]}",
            (0x1001, { } d) => $"voltage {(d[0] << 8) | d[1]} mV, flags 0x{d[2]:X2}",
            (0x1000, { } d) => $"level {d[0]}%, next {d[1]}%, status {d[2]}",
            _ => "",
        };
        Console.WriteLine($"    feature 0x{feat:X4} at index {idx}: {raw}  {meaning}");
    }
}
Console.WriteLine("done");

static string Short(string p) => p.Length > 70 ? p[..70] + "..." : p;

static class Hid
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);

    [StructLayout(LayoutKind.Sequential)]
    struct CAPS
    {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] R;
        public ushort a, b, c, d, e, f, g, h, i, j;
    }
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out CAPS c);

    public static (int Page, int Usage, int InLen, int OutLen) Caps(SafeFileHandle h)
    {
        if (!HidD_GetPreparsedData(h, out var p)) return (0, 0, 0, 0);
        HidP_GetCaps(p, out var c);
        HidD_FreePreparsedData(p);
        return (c.UsagePage, c.Usage, c.InLen, c.OutLen);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IFDATA { public int cbSize; public Guid g; public int Flags; public IntPtr R; }
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, uint f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref IFDATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref IFDATA d, IntPtr det, int size, out int req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);

    public static List<string> Paths()
    {
        var g = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        var list = new List<string>();
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new IFDATA { cbSize = Marshal.SizeOf<IFDATA>() };
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int size, IntPtr.Zero);
            var buf = Marshal.AllocHGlobal(size);
            Marshal.WriteInt32(buf, 8);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, size, out _, IntPtr.Zero)) list.Add(Marshal.PtrToStringUni(buf + 4)!);
            Marshal.FreeHGlobal(buf);
        }
        SetupDiDestroyDeviceInfoList(set);
        return list;
    }
}
