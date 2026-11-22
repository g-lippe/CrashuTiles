namespace CrashuTiles.Data.Batteries;

/// <summary>One device's battery. Percent is null when unknown; Detail is an optional extra line (AirPods: L / R / case).</summary>
sealed record BatteryReading(string Source, string Name, int? Percent, bool Charging, bool Online, DateTime LastSeen, string? Detail = null);

interface IBatterySource : IDisposable
{
    /// <summary>"logitech", "corsair", "bluetooth" or "xinput"; what configs refer to.</summary>
    string Kind { get; }
    /// <summary>Reads every device this source can see. Called on the hub's thread only.</summary>
    IReadOnlyList<BatteryReading> Poll();
}

/// <summary>
/// Polls every battery source on a background thread, every 30 seconds (batteries change slowly,
/// and waking a sleeping mouse every second would be rude). Tiles read the latest snapshot.
/// </summary>
sealed class BatteryHub : IDisposable
{
    const int PollMs = 30_000;

    readonly List<IBatterySource> _sources;
    readonly System.Threading.Timer? _timer;
    readonly object _gate = new();
    IReadOnlyList<BatteryReading> _readings = [];
    bool _disposed;

    static string CachePath => Path.Combine(AppPaths.Folder, "batteries.json");
    readonly Dictionary<string, BatteryReading> _cache;

    public BatteryHub(bool autoPoll = true)
    {
        _sources = [new LogitechSource(), new CorsairSource(), new BluetoothSource(), new AirPodsSource(), new XInputSource()];
        _cache = LoadCache();
        _readings = _cache.Values.Select(r => r with { Online = false }).ToList();
        if (autoPoll) _timer = new System.Threading.Timer(_ => Poll(), null, 500, PollMs);
    }

    static Dictionary<string, BatteryReading> LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, BatteryReading>>(File.ReadAllText(CachePath)) ?? new();
        }
        catch (Exception e) { Log.Info("Battery cache unreadable: " + e.Message); }
        return new();
    }

    void SaveCache()
    {
        try { File.WriteAllText(CachePath, System.Text.Json.JsonSerializer.Serialize(_cache)); }
        catch (Exception e) { Log.Info("Saving the battery cache failed: " + e.Message); }
    }

    public void Poll()
    {
        var raw = new List<BatteryReading>();
        foreach (var s in _sources)
        {
            try { raw.AddRange(s.Poll()); }
            catch (Exception e) { Log.Info($"Battery source {s.Kind} failed: {e.Message}"); }
        }
        // One device can be reachable two ways (a mouse on its cable and through its receiver):
        // keep one reading per name, preferring online, then the freshest.
        var fresh = raw.GroupBy(r => r.Source + "|" + r.Name)
            .Select(g => g.OrderByDescending(r => r.Online).ThenByDescending(r => r.LastSeen).First())
            .ToList();

        // Remember every device's last level (also across restarts), so a sleeping mouse or a
        // switched-off headset shows its last value marked offline instead of "not found".
        bool cacheChanged = false;
        foreach (var r in fresh.Where(r => r.Online && r.Percent != null))
        {
            string key = r.Source + "|" + r.Name;
            if (!_cache.TryGetValue(key, out var old) || old.Percent != r.Percent || old.Charging != r.Charging || old.Detail != r.Detail) cacheChanged = true;
            _cache[key] = r;
        }
        var reported = fresh.Select(r => r.Source + "|" + r.Name).ToHashSet();
        var all = fresh.Concat(_cache.Where(kv => !reported.Contains(kv.Key)).Select(kv => kv.Value with { Online = false })).ToList();
        if (cacheChanged) SaveCache();

        lock (_gate)
        {
            if (_disposed) return;
            if (!all.Select(r => (r.Name, r.Percent, r.Charging, r.Online)).SequenceEqual(_readings.Select(r => (r.Name, r.Percent, r.Charging, r.Online))))
                Log.Info("Batteries: " + (all.Count == 0 ? "none" : string.Join(", ", all.Select(r => $"{r.Name} {(r.Percent is int p ? p + "%" : "?")}{(r.Charging ? " charging" : "")}{(r.Online ? "" : " (offline)")}"))));
            _readings = all;
        }
    }

    public IReadOnlyList<BatteryReading> All { get { lock (_gate) return _readings; } }

    static readonly string[] SourceOrder = ["logitech", "corsair", "bluetooth", "xinput"];

    /// <summary>Every known device (online or remembered) in a stable order: by source, then name.</summary>
    public IReadOnlyList<BatteryReading> Devices() => All
        .OrderBy(r => Array.IndexOf(SourceOrder, r.Source) is var i && i >= 0 ? i : SourceOrder.Length)
        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>A short display name: "G502 LIGHTSPEED Wireless Gaming Mouse" becomes "G502"; "AirPods Pro" stays.</summary>
    public static string ShortName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (name.Length <= 12 || words.Length < 2) return name;
        return words[0].Length >= 3 ? words[0] : string.Join(' ', words.Take(2));
    }

    /// <summary>
    /// First device from the given source ("any" for all) whose name contains match (case-insensitive).
    /// No match string means the first device of that source.
    /// </summary>
    public BatteryReading? Find(string source, string? match)
    {
        foreach (var r in All)
        {
            if (!source.Equals("any", StringComparison.OrdinalIgnoreCase) && !r.Source.Equals(source, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(match) || r.Name.Contains(match, StringComparison.OrdinalIgnoreCase)) return r;
        }
        return null;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        lock (_gate) _disposed = true;
        foreach (var s in _sources) s.Dispose();
    }
}
