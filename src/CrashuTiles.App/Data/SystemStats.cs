using System.Runtime.InteropServices;

namespace CrashuTiles.Data;

/// <summary>CPU and RAM load sampled once a second, with a one-minute history. No admin needed.</summary>
sealed class SystemStats : IDisposable
{
    public const int HistoryLength = 60;

    readonly object _gate = new();
    readonly float[] _cpuHist = new float[HistoryLength], _ramHist = new float[HistoryLength];
    int _head, _count;
    long _pIdle, _pKernel, _pUser;
    readonly System.Threading.Timer? _timer;

    public int Cpu { get; private set; }
    public int Ram { get; private set; }
    public double RamUsedGb { get; private set; }
    public double RamTotalGb { get; private set; }

    public SystemStats(bool autoSample = true)
    {
        Sample();
        if (autoSample) _timer = new System.Threading.Timer(_ => Sample(), null, 1000, 1000);
    }

    public void Sample()
    {
        GetSystemTimes(out long idle, out long kernel, out long user);
        long dIdle = idle - _pIdle, dTotal = (kernel - _pKernel) + (user - _pUser); // kernel time includes idle
        bool first = _pKernel == 0;
        _pIdle = idle; _pKernel = kernel; _pUser = user;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);

        lock (_gate)
        {
            if (!first && dTotal > 0) Cpu = (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal);
            Ram = (int)mem.dwMemoryLoad;
            RamTotalGb = mem.ullTotalPhys / 1073741824.0;
            RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
            if (first) return;
            _cpuHist[_head] = Cpu; _ramHist[_head] = Ram;
            _head = (_head + 1) % HistoryLength;
            _count = Math.Min(_count + 1, HistoryLength);
        }
    }

    /// <summary>History oldest to newest (values 0..100).</summary>
    public (float[] Cpu, float[] Ram) History()
    {
        lock (_gate)
        {
            var c = new float[_count]; var r = new float[_count];
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - _count + i + HistoryLength) % HistoryLength;
                c[i] = _cpuHist[idx]; r[i] = _ramHist[idx];
            }
            return (c, r);
        }
    }

    public void Dispose() => _timer?.Dispose();

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
