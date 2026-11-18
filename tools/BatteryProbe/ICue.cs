using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Minimal iCUE SDK v4 client: connect, list devices, read CDPI_BatteryLevel.
/// The DLL (iCUESDK.x64_2019.dll) comes from Corsair's SDK download and is not redistributed.
/// </summary>
static unsafe class ICue
{
    const int StringM = 128, DeviceMax = 64, BatteryLevel = 9, Connected = 6;

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfo
    {
        public int Type;
        public fixed byte Id[StringM];
        public fixed byte Serial[StringM];
        public fixed byte Model[StringM];
        public int LedCount, ChannelCount;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct Property
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public int Int32;
    }

    static volatile int _state;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnState(void* ctx, int* data) => _state = *data; // first field of CorsairSessionStateChanged

    public static void Probe(string dllPath)
    {
        var lib = NativeLibrary.Load(dllPath);
        var connect = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<void*, int*, void>, void*, int>)NativeLibrary.GetExport(lib, "CorsairConnect");
        var getDevices = (delegate* unmanaged[Cdecl]<int*, int, DeviceInfo*, int*, int>)NativeLibrary.GetExport(lib, "CorsairGetDevices");
        var read = (delegate* unmanaged[Cdecl]<byte*, int, uint, Property*, int>)NativeLibrary.GetExport(lib, "CorsairReadDeviceProperty");
        var free = (delegate* unmanaged[Cdecl]<Property*, int>)NativeLibrary.GetExport(lib, "CorsairFreeProperty");

        int err = connect(&OnState, null);
        Console.WriteLine($"CorsairConnect: {err}");
        for (int i = 0; i < 50 && _state != Connected; i++) Thread.Sleep(100);
        Console.WriteLine($"Session state: {_state} ({(_state == Connected ? "connected" : "not connected; is third-party control enabled in iCUE?")})");
        if (_state != Connected) return;

        int filter = unchecked((int)0xFFFFFFFF);
        var devices = new DeviceInfo[DeviceMax];
        int count = 0;
        fixed (DeviceInfo* p = devices) err = getDevices(&filter, DeviceMax, p, &count);
        Console.WriteLine($"CorsairGetDevices: {err}, {count} device(s)");
        for (int i = 0; i < count; i++)
        {
            fixed (DeviceInfo* d = &devices[i])
            {
                string model = Str(d->Model), id = Str(d->Id);
                Property prop;
                int r = read(d->Id, BatteryLevel, 0, &prop);
                string battery = r == 0 ? $"battery {prop.Int32}% (type {prop.Type})" : $"no battery property (error {r})";
                if (r == 0) free(&prop);
                Console.WriteLine($"  type 0x{d->Type:X4}  {model,-36} {battery}");
            }
        }
    }

    static string Str(byte* s)
    {
        int n = 0;
        while (n < StringM && s[n] != 0) n++;
        return Encoding.UTF8.GetString(s, n);
    }
}
