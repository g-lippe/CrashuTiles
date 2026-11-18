using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CrashuTiles.Data.Batteries;

/// <summary>
/// Corsair wireless devices through the iCUE SDK v4, which asks the running iCUE app.
/// Corsair's licence doesn't clearly allow redistributing the SDK DLL, so it isn't shipped:
/// users download iCUESDK.x64_2019.dll themselves and put it in %APPDATA%\CrashuTiles
/// (or next to CrashuTiles.exe). Without it, or without iCUE running, this source is empty.
/// </summary>
sealed unsafe class CorsairSource : IBatterySource
{
    public const string DllName = "iCUESDK.x64_2019.dll";
    const int StringM = 128, DeviceMax = 64, PropBatteryLevel = 9, StateConnected = 6;

    public string Kind => "corsair";

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
    static void OnStateChanged(void* context, int* data) => _state = *data; // CorsairSessionStateChanged.state

    IntPtr _lib;
    bool _triedLoad, _connected;
    delegate* unmanaged[Cdecl]<int*, int, DeviceInfo*, int*, int> _getDevices;
    delegate* unmanaged[Cdecl]<byte*, int, uint, Property*, int> _readProperty;
    delegate* unmanaged[Cdecl]<Property*, int> _freeProperty;
    delegate* unmanaged[Cdecl]<int> _disconnect;
    readonly Dictionary<string, BatteryReading> _last = new();

    public static string? FindDll()
    {
        string[] candidates = [Path.Combine(AppPaths.Folder, DllName), Path.Combine(AppContext.BaseDirectory, DllName)];
        return candidates.FirstOrDefault(File.Exists);
    }

    bool EnsureConnected()
    {
        if (_connected) return _state == StateConnected;
        if (_triedLoad) return false;
        _triedLoad = true;
        string? dll = FindDll();
        if (dll == null) { Log.Info($"Corsair batteries off: {DllName} not found in {AppPaths.Folder}"); return false; }
        try
        {
            _lib = NativeLibrary.Load(dll);
            var connect = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<void*, int*, void>, void*, int>)NativeLibrary.GetExport(_lib, "CorsairConnect");
            _getDevices = (delegate* unmanaged[Cdecl]<int*, int, DeviceInfo*, int*, int>)NativeLibrary.GetExport(_lib, "CorsairGetDevices");
            _readProperty = (delegate* unmanaged[Cdecl]<byte*, int, uint, Property*, int>)NativeLibrary.GetExport(_lib, "CorsairReadDeviceProperty");
            _freeProperty = (delegate* unmanaged[Cdecl]<Property*, int>)NativeLibrary.GetExport(_lib, "CorsairFreeProperty");
            _disconnect = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(_lib, "CorsairDisconnect");
            int err = connect(&OnStateChanged, null);
            if (err != 0) { Log.Info($"CorsairConnect failed with {err}"); return false; }
            _connected = true; // the SDK reconnects by itself when iCUE restarts; _state follows it
            for (int i = 0; i < 30 && _state != StateConnected; i++) Thread.Sleep(100);
            Log.Info(_state == StateConnected ? "Connected to iCUE" : $"iCUE session state {_state} (is iCUE running?)");
            return _state == StateConnected;
        }
        catch (Exception e)
        {
            Log.Info("Loading the iCUE SDK failed: " + e.Message);
            return false;
        }
    }

    public IReadOnlyList<BatteryReading> Poll()
    {
        var result = new List<BatteryReading>();
        if (EnsureConnected())
        {
            int filter = -1; // CDT_All
            var devices = new DeviceInfo[DeviceMax];
            int count = 0;
            fixed (DeviceInfo* p = devices)
                if (_getDevices(&filter, DeviceMax, p, &count) != 0) count = 0;

            var seen = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                fixed (DeviceInfo* d = &devices[i])
                {
                    Property prop;
                    if (_readProperty(d->Id, PropBatteryLevel, 0, &prop) != 0) continue; // wired: no battery
                    int level = prop.Int32;
                    _freeProperty(&prop);
                    string name = Str(d->Model);
                    seen.Add(name);
                    // The SDK has no charging flag; iCUE stops reporting the level while charging.
                    var r = new BatteryReading(Kind, name, Math.Clamp(level, 0, 100), false, true, DateTime.Now);
                    _last[name] = r;
                    result.Add(r);
                }
            }
            // Devices that dropped out (headset switched off) stay visible as offline.
            foreach (var (name, r) in _last)
                if (!seen.Contains(name)) result.Add(r with { Online = false });
        }
        return result;
    }

    static string Str(byte* s)
    {
        int n = 0;
        while (n < StringM && s[n] != 0) n++;
        return Encoding.UTF8.GetString(s, n);
    }

    public void Dispose()
    {
        if (_connected && _disconnect != null) { try { _disconnect(); } catch { } }
        _connected = false;
    }
}
