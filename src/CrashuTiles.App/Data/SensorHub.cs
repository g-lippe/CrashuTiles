using LibreHardwareMonitor.Hardware;

namespace CrashuTiles.Data;

/// <summary>A sensor's latest value plus its last minute (oldest first). Value is null when unreadable.</summary>
sealed record SensorReading(float? Value, SensorType Type, string Name, float[] History);

/// <summary>
/// Hardware sensors via LibreHardwareMonitor, sampled once a second on a background thread.
///
/// Tiles ask for a sensor by key: a friendly alias like "gpu.temp" (see <see cref="Aliases"/>),
/// or a raw LibreHardwareMonitor identifier like "/gpu-nvidia/0/temperature/0". Asking is
/// enough to start tracking it, and the first value arrives with the next sample.
///
/// Without admin rights (and the PawnIO driver) the CPU temperature, clocks, power and the
/// motherboard sensors read 0; those come back as null so tiles can show "-".
/// </summary>
sealed class SensorHub : IDisposable
{
    public const int HistoryLength = 60;

    // Declared before Aliases: static fields initialise in order.
    static readonly HardwareType[] Cpu = [HardwareType.Cpu];
    static readonly HardwareType[] Mem = [HardwareType.Memory];
    static readonly HardwareType[] Gpu = [HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel];

    /// <summary>Alias -> (hardware kinds, sensor type, sensor name). Network aliases are special-cased.</summary>
    static readonly Dictionary<string, (HardwareType[] Hw, SensorType Type, string Name)> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu.load"] = (Cpu, SensorType.Load, "CPU Total"),
        ["cpu.coremax"] = (Cpu, SensorType.Load, "CPU Core Max"),
        ["cpu.temp"] = (Cpu, SensorType.Temperature, "Core (Tctl/Tdie)"),
        ["cpu.power"] = (Cpu, SensorType.Power, "Package"),
        ["cpu.clock"] = (Cpu, SensorType.Clock, "Cores (Average)"),
        ["ram.load"] = (Mem, SensorType.Load, "Memory"),
        ["ram.used"] = (Mem, SensorType.Data, "Memory Used"),
        ["ram.free"] = (Mem, SensorType.Data, "Memory Available"),
        ["gpu.load"] = (Gpu, SensorType.Load, "GPU Core"),
        ["gpu.temp"] = (Gpu, SensorType.Temperature, "GPU Core"),
        ["gpu.hotspot"] = (Gpu, SensorType.Temperature, "GPU Hot Spot"),
        ["gpu.memtemp"] = (Gpu, SensorType.Temperature, "GPU Memory Junction"),
        ["gpu.power"] = (Gpu, SensorType.Power, "GPU Package"),
        ["gpu.clock"] = (Gpu, SensorType.Clock, "GPU Core"),
        ["gpu.fan"] = (Gpu, SensorType.Fan, "GPU Fan 1"),
        ["gpu.vram"] = (Gpu, SensorType.Load, "GPU Memory"),
        ["gpu.vramused"] = (Gpu, SensorType.SmallData, "GPU Memory Used"),
    };

    public static IEnumerable<string> AliasNames => Aliases.Keys.Concat(["net.down", "net.up"]);

    readonly object _gate = new();
    readonly Computer _computer = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsNetworkEnabled = true,
    };
    readonly Dictionary<string, Tracked> _tracked = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Threading.Timer? _timer;
    volatile bool _open;
    bool _disposed;

    sealed class Tracked
    {
        public ISensor? Sensor;
        public float? Value;
        public SensorType Type;
        public string Name = "";
        public readonly float[] Ring = new float[HistoryLength];
        public int Head, Count;
    }

    public bool Ready => _open;

    /// <param name="autoSample">false for one-shot use (the preview), which calls <see cref="Sample"/> itself.</param>
    public SensorHub(bool autoSample = true)
    {
        if (autoSample)
            _timer = new System.Threading.Timer(_ => Sample(), null, 0, 1000);
    }

    /// <summary>Opens on first call (about 1.5 s), then refreshes every tracked sensor.</summary>
    public void Sample()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                if (!_open)
                {
                    _computer.Open();
                    _open = true;
                    Log.Info("Sensors ready: " + string.Join(", ", _computer.Hardware.Select(h => h.Name).Distinct().Take(6)));
                }
                foreach (var h in _computer.Hardware) Update(h);

                foreach (var (key, t) in _tracked)
                {
                    // Network aliases re-pick the busiest adapter every sample (cheap, and it can change).
                    if (t.Sensor == null || key.StartsWith("net.", StringComparison.OrdinalIgnoreCase) && !key.Contains('@'))
                        t.Sensor = Resolve(key);
                    if (t.Sensor == null) { t.Value = null; continue; }
                    t.Type = t.Sensor.SensorType;
                    t.Name = t.Sensor.Name;
                    float? v = t.Sensor.Value;
                    // Driver-less reads come back as 0 (or NaN); treat those as unreadable.
                    if (v is float f && (float.IsNaN(f) || (f == 0 && NeedsDriver(t.Sensor)))) v = null;
                    t.Value = v;
                    t.Ring[t.Head] = v ?? 0;
                    t.Head = (t.Head + 1) % HistoryLength;
                    t.Count = Math.Min(t.Count + 1, HistoryLength);
                }
            }
            catch (Exception e)
            {
                Log.Info("Sensor sample failed: " + e.Message);
            }
        }
    }

    static void Update(IHardware h)
    {
        h.Update();
        foreach (var sub in h.SubHardware) Update(sub);
    }

    static bool NeedsDriver(ISensor s) =>
        s.Hardware.HardwareType is HardwareType.Cpu or HardwareType.Motherboard or HardwareType.SuperIO
        && s.SensorType is SensorType.Temperature or SensorType.Clock or SensorType.Power or SensorType.Fan;

    /// <summary>Latest reading for a key; starts tracking it if it's new (null until the next sample).</summary>
    public SensorReading? Get(string key)
    {
        lock (_gate)
        {
            if (!_tracked.TryGetValue(key, out var t))
            {
                _tracked[key] = new Tracked();
                return null;
            }
            if (t.Sensor == null) return null;
            var hist = new float[t.Count];
            for (int i = 0; i < t.Count; i++) hist[i] = t.Ring[(t.Head - t.Count + i + HistoryLength) % HistoryLength];
            return new SensorReading(t.Value, t.Type, t.Name, hist);
        }
    }

    /// <summary>Numbers for the firmware's own Stats screen.</summary>
    public (int Cpu, int Mem, int Gpu) NativeStats() =>
        ((int)(Get("cpu.load")?.Value ?? 0), (int)(Get("ram.load")?.Value ?? 0), (int)(Get("gpu.load")?.Value ?? 0));

    // ---------- resolving keys ----------

    ISensor? Resolve(string key)
    {
        var all = _computer.Hardware.SelectMany(Flatten).ToList();
        if (key.StartsWith('/'))
            return all.SelectMany(h => h.Sensors).FirstOrDefault(s => s.Identifier.ToString().Equals(key, StringComparison.OrdinalIgnoreCase));

        // net.down / net.up, optionally "net.down@Ethernet 2"
        if (key.StartsWith("net.", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = key.Split('@', 2);
            string wanted = parts[0].Equals("net.up", StringComparison.OrdinalIgnoreCase) ? "Upload Speed" : "Download Speed";
            var adapter = parts.Length > 1
                ? all.FirstOrDefault(h => h.HardwareType == HardwareType.Network && h.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase))
                : BusiestAdapter(all);
            return adapter?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name == wanted);
        }

        if (!Aliases.TryGetValue(key, out var a)) return null;
        // "Total Memory" holds the RAM sensors; "Virtual Memory" has the same names.
        return all.Where(h => a.Hw.Contains(h.HardwareType) && !h.Name.StartsWith("Virtual", StringComparison.OrdinalIgnoreCase))
                  .SelectMany(h => h.Sensors)
                  .FirstOrDefault(s => s.SensorType == a.Type && s.Name == a.Name);
    }

    static IEnumerable<IHardware> Flatten(IHardware h) => new[] { h }.Concat(h.SubHardware.SelectMany(Flatten));

    /// <summary>The real adapter that has moved the most data: skips filter layers, Bluetooth and virtual ones.</summary>
    static IHardware? BusiestAdapter(List<IHardware> all) => all
        .Where(h => h.HardwareType == HardwareType.Network
                    && !h.Name.Contains("-QoS") && !h.Name.Contains("-WFP") && !h.Name.Contains("Bluetooth")
                    && !h.Name.Contains('*'))
        .OrderByDescending(h => h.Sensors.Where(s => s.SensorType == SensorType.Data).Sum(s => s.Value ?? 0))
        .FirstOrDefault();

    public void Dispose()
    {
        _timer?.Dispose();
        lock (_gate)
        {
            _disposed = true;
            if (_open) _computer.Close();
        }
    }
}
