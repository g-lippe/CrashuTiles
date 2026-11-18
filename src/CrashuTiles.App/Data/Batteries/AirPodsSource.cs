using Windows.Devices.Bluetooth.Advertisement;

namespace CrashuTiles.Data.Batteries;

/// <summary>
/// AirPods (and Beats) battery from Apple's "proximity pairing" Bluetooth LE broadcasts, the ones
/// an iPhone uses for its battery pop-up. Windows itself has no battery value for AirPods.
///
/// The broadcast is Apple manufacturer data (company 0x004C) starting with type 0x07. Batteries
/// come in 10% steps, separately for left, right and case. Broadcasts carry no owner id, so
/// only the strongest (nearest) signal is used. They're only sent while the buds are out
/// or the case is open; in between, BatteryHub shows the last known level.
///
/// Reported as source "bluetooth", so a { "battery": "bluetooth", "match": "AirPods" } tile finds it.
/// </summary>
sealed class AirPodsSource : IBatterySource
{
    const ushort Apple = 0x004C;
    static readonly TimeSpan Fresh = TimeSpan.FromSeconds(90);

    public string Kind => "bluetooth";

    readonly BluetoothLEAdvertisementWatcher? _watcher;
    readonly object _gate = new();
    Parsed? _best;

    sealed record Parsed(string Model, int? Left, int? Right, int? Case, bool ChargingLeft, bool ChargingRight, bool ChargingCase, short Rssi, DateTime At);

    public AirPodsSource()
    {
        _connectedPids = BluetoothSource.ConnectedApplePids();
        try
        {
            _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            _watcher.Received += OnReceived;
            _watcher.Start();
        }
        catch (Exception e)
        {
            Log.Info("Bluetooth LE listening unavailable: " + e.Message);
        }
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var md in args.Advertisement.ManufacturerData)
        {
            if (md.CompanyId != Apple || md.Data.Length < 8) continue;
            var d = new byte[md.Data.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(md.Data)) reader.ReadBytes(d);
            if (d[0] != 0x07) continue; // proximity pairing message
            // Only our own model: the broadcast's model bytes are the Bluetooth product id, little-endian.
            int pid = (d[4] << 8) | d[3];
            var mine = _connectedPids;
            if (mine.Count > 0 && !mine.Contains(pid)) continue;
            var p = Parse(d, args.RawSignalStrengthInDBm);
            lock (_gate)
            {
                // Prefer the nearest device; a weaker one only replaces it once it has gone quiet.
                if (_best == null || p.Rssi >= _best.Rssi - 3 || DateTime.Now - _best.At > TimeSpan.FromSeconds(15))
                    _best = p;
            }
        }
    }

    static Parsed Parse(byte[] d, short rssi)
    {
        int model = (d[3] << 8) | d[4];
        bool flipped = (d[5] & 0x20) == 0; // which bud is "primary" swaps the nibbles
        int hi = d[6] >> 4, lo = d[6] & 0x0F;
        int charge = d[7] >> 4, caseLevel = d[7] & 0x0F;
        static int? Level(int n) => n <= 10 ? n * 10 : null; // 15 = unknown / not in range
        return new Parsed(
            ModelName(model),
            Level(flipped ? hi : lo), Level(flipped ? lo : hi), Level(caseLevel),
            (charge & (flipped ? 0b10 : 0b01)) != 0, (charge & (flipped ? 0b01 : 0b10)) != 0, (charge & 0b100) != 0,
            rssi, DateTime.Now);
    }

    static string ModelName(int model) => model switch
    {
        0x0220 => "AirPods",
        0x0F20 => "AirPods 2",
        0x1320 => "AirPods 3",
        0x1920 => "AirPods 4",
        0x0E20 => "AirPods Pro",
        0x1420 or 0x2420 => "AirPods Pro 2",
        0x2720 => "AirPods Pro", // PID 0x2027, the maintainer's pair (named "AirPods Pro" by Windows)
        0x0A20 => "AirPods Max",
        _ => LogUnknown(model),
    };

    static readonly HashSet<int> Reported = [];
    static string LogUnknown(int model)
    {
        lock (Reported) if (Reported.Add(model)) Log.Info($"AirPods: unknown model code 0x{model:X4}");
        return "AirPods";
    }

    volatile HashSet<int> _connectedPids = [];

    public IReadOnlyList<BatteryReading> Poll()
    {
        _connectedPids = BluetoothSource.ConnectedApplePids();
        Parsed? p;
        lock (_gate) p = _best;
        if (p == null || DateTime.Now - p.At > Fresh) return [];

        int?[] buds = [p.Left, p.Right];
        int? main = buds.Where(b => b != null).Min() ?? p.Case;
        string detail = $"L {Fmt(p.Left, p.ChargingLeft)}  R {Fmt(p.Right, p.ChargingRight)}  C {Fmt(p.Case, p.ChargingCase)}";
        return [new BatteryReading(Kind, p.Model, main, p.ChargingLeft || p.ChargingRight, true, p.At, detail)];
    }

    static string Fmt(int? v, bool charging) => v is int n ? n + (charging ? "+" : "") : "–";

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
    }
}
