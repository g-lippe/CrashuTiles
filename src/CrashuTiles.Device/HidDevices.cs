using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CrashuTiles.Device;

/// <summary>Generic HID helpers shared with other device code (battery readers): enumerate, open, query caps.</summary>
public static class HidDevices
{
    public readonly record struct Caps(int UsagePage, int Usage, int InputLength, int OutputLength);

    /// <summary>Device paths of every present HID collection.</summary>
    public static List<string> Paths() => Native.HidInterfacePaths();

    /// <summary>Opens a collection for overlapped read/write; null if it's in exclusive use (keyboards, mice).</summary>
    public static SafeFileHandle? OpenShared(string path)
    {
        var h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    public static Caps GetCaps(SafeFileHandle h)
    {
        if (!HidD_GetPreparsedData(h, out var p)) return default;
        try
        {
            HidP_GetCaps(p, out var c);
            return new Caps(c.UsagePage, c.Usage, c.InLen, c.OutLen);
        }
        finally { HidD_FreePreparsedData(p); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NLinkColl, NInBtn, NInVal, NInData, NOutBtn, NOutVal, NOutData, NFBtn, NFVal, NFData;
    }

    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
}
