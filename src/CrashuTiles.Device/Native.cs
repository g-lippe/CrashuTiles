using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CrashuTiles.Device;

/// <summary>The handful of Win32 calls needed to find and open a HID interface.</summary>
internal static class Native
{
    public static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index,
        ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
        IntPtr detail, int detailSize, out int required, IntPtr devInfo);

    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    /// <summary>Device paths of every present HID interface.</summary>
    public static List<string> HidInterfacePaths()
    {
        var result = new List<string>();
        var guid = HidInterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return result;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out _, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringUni(buf + 4);
                        if (path != null) result.Add(path);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
