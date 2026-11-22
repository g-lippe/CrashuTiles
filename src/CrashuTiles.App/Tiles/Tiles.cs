using System.Globalization;
using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Data.Batteries;
using CrashuTiles.Rendering;
using LibreHardwareMonitor.Hardware;

namespace CrashuTiles.Tiles;

sealed record TileContext(SensorHub Sensors, BatteryHub Batteries, DateTime Now, bool Pressed);

/// <summary>A key's look. Draw fills the whole canvas: 196x196 for keys, 458x196 for the small screen.</summary>
abstract class Tile
{
    protected readonly KeyConfig Config;
    protected readonly Color Accent;

    protected Tile(KeyConfig config, Color defaultAccent)
    {
        Config = config;
        Accent = Painter.ParseColor(config.Color, defaultAccent);
    }

    /// <summary>How often the tile needs redrawing; 0 = only when the page loads or the key is pressed.</summary>
    public virtual int RefreshMs => 0;

    public abstract void Draw(Graphics g, Rectangle r, TileContext ctx);

    public static Tile Create(KeyConfig cfg) => (cfg.Tile ?? "label").ToLowerInvariant() switch
    {
        "sensor" => new SensorTile(cfg, cfg.Sensor ?? "cpu.load"),
        "cpu" => new SensorTile(cfg, cfg.Sensor ?? "cpu.load"),
        "ram" => new SensorTile(cfg, cfg.Sensor ?? "ram.load"),
        "gpu" => new SensorTile(cfg, cfg.Sensor ?? "gpu.load"),
        "battery" => new BatteryTile(cfg),
        "batteries" => new MultiBatteryTile(cfg),
        "clock" => new ClockTile(cfg),
        "graph" => new GraphTile(cfg),
        _ => new LabelTile(cfg),
    };

    /// <summary>Renders a tile to PNG, with the pressed highlight on top when the key is held down.</summary>
    public byte[] Render(int width, int height, TileContext ctx)
    {
        using var bmp = Painter.Canvas(width, height, out var g);
        using (g)
        {
            var r = new Rectangle(0, 0, width, height);
            Draw(g, r, ctx);
            if (ctx.Pressed)
            {
                using var veil = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
                g.FillRectangle(veil, r);
                using var pen = new Pen(Painter.Mix(Accent, Color.White, 0.5f), 8);
                g.DrawRectangle(pen, 4, 4, width - 8, height - 8);
            }
        }
        return Painter.Png(bmp);
    }

    /// <summary>Numbers the "batteries" tiles of a page in key order, so each shows the next three devices.</summary>
    public static void AssignGroups(IEnumerable<Tile?> pageTiles)
    {
        int group = 0;
        foreach (var t in pageTiles)
            if (t is MultiBatteryTile m) m.Group = group++;
    }

    public static byte[] Blank(int width, int height)
    {
        using var bmp = Painter.Canvas(width, height, out var g);
        using (g) g.Clear(Color.Black);
        return Painter.Png(bmp);
    }
}

/// <summary>An icon and/or a label. Launch keys without an icon use the app's own icon.</summary>
sealed class LabelTile(KeyConfig cfg) : Tile(cfg, Painter.DefaultAccent)
{
    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        Painter.Background(g, r, Accent);
        string? icon = Config.Icon;
        if (icon == null && Config.Action is { Type: var t, Path: { } p } && t.Equals("launch", StringComparison.OrdinalIgnoreCase))
            icon = p;
        string label = Config.Label ?? "";

        if (icon != null)
        {
            float size = r.Height * (label.Length > 0 ? 0.50f : 0.62f);
            var iconBox = new RectangleF(r.X + (r.Width - size) / 2, r.Y + (label.Length > 0 ? r.Height * 0.12f : (r.Height - size) / 2), size, size);
            Painter.Icon(g, icon, iconBox, Color.White);
            if (label.Length > 0)
                Painter.Text(g, label, Painter.Ui, FontStyle.Regular, 26, new RectangleF(r.X + 8, r.Y + r.Height * 0.68f, r.Width - 16, r.Height * 0.28f), Color.FromArgb(225, 255, 255, 255));
        }
        else
        {
            Painter.Text(g, label, Painter.Ui, FontStyle.Regular, 44, RectangleF.Inflate(r, -12, -12), Color.White);
        }
    }
}

/// <summary>How sensor values are titled, scaled and printed.</summary>
static class SensorFormat
{
    static readonly Dictionary<string, string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu.load"] = "CPU", ["cpu.coremax"] = "CPU max core", ["cpu.temp"] = "CPU temp", ["cpu.power"] = "CPU power",
        ["cpu.clock"] = "CPU clock", ["ram.load"] = "RAM", ["ram.used"] = "RAM used", ["ram.free"] = "RAM free",
        ["gpu.load"] = "GPU", ["gpu.temp"] = "GPU temp", ["gpu.hotspot"] = "GPU hot spot", ["gpu.memtemp"] = "VRAM temp",
        ["gpu.power"] = "GPU power", ["gpu.clock"] = "GPU clock", ["gpu.fan"] = "GPU fan", ["gpu.vram"] = "VRAM",
        ["gpu.vramused"] = "VRAM used", ["net.down"] = "Download", ["net.up"] = "Upload",
    };

    public static readonly Color CpuColor = Color.FromArgb(63, 169, 245);   // blue
    public static readonly Color GpuColor = Color.FromArgb(118, 185, 0);    // green
    public static readonly Color RamColor = Color.FromArgb(230, 180, 34);   // golden
    public static readonly Color NetColor = Color.FromArgb(0, 190, 170);    // teal

    /// <summary>Each sensor family has one colour: CPU blue, GPU green, RAM golden, network teal.</summary>
    public static Color? GroupColor(string key) => key.Split('.')[0].ToLowerInvariant() switch
    {
        "cpu" => CpuColor,
        "gpu" => GpuColor,
        "ram" => RamColor,
        "net" => NetColor,
        _ => null,
    };

    public static readonly Color Warn = Color.FromArgb(245, 166, 35);
    public static readonly Color Crit = Color.FromArgb(229, 72, 77);

    public static string Title(string key, SensorReading? r) =>
        Titles.TryGetValue(key.Split('@')[0], out var t) ? t : r?.Name ?? key;

    public static string Value(SensorReading? r)
    {
        if (r == null) return "–";
        if (r.Value is not float v) return "n/a";
        return r.Type switch
        {
            SensorType.Load or SensorType.Control or SensorType.Level => $"{v:0}%",
            SensorType.Temperature => $"{v:0}°C",
            SensorType.Power => v < 10 ? $"{v:0.0} W" : $"{v:0} W",
            SensorType.Clock => v >= 1000 ? $"{v / 1000:0.00} GHz" : $"{v:0} MHz",
            SensorType.Fan => $"{v:0} RPM",
            SensorType.Voltage => $"{v:0.00} V",
            SensorType.Data => $"{v:0.0} GB",
            SensorType.SmallData => v >= 1024 ? $"{v / 1024:0.0} GB" : $"{v:0} MB",
            SensorType.Throughput => v >= 1_000_000 ? $"{v / 1_000_000:0.0} MB/s" : v >= 1000 ? $"{v / 1000:0} KB/s" : $"{v:0} B/s",
            _ => $"{v:0.#}",
        };
    }

    /// <summary>Top of the scale: configured, else 100 for percentages and temperatures, else the recent peak.</summary>
    public static float Max(SensorReading? r, float? configured)
    {
        if (configured is > 0) return configured.Value;
        if (r == null) return 100;
        if (r.Type is SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Temperature) return 100;
        float peak = r.History.Length > 0 ? r.History.Max() : 0;
        // An idle network shouldn't blow a few bytes up to full height: scale to at least 100 KB/s.
        float floor = r.Type == SensorType.Throughput ? 100_000 : 1;
        return Math.Max(Math.Max(peak * 1.15f, r.Value ?? 0), floor);
    }

    public static Color Accent(SensorReading? r, KeyConfig cfg, Color normal)
    {
        if (r?.Value is not float v) return normal;
        if (cfg.Crit is float c && v >= c) return Crit;
        if (cfg.Warn is float w && v >= w) return Warn;
        return normal;
    }
}

/// <summary>Any sensor: big value with a one-minute sparkline, or a ring gauge.</summary>
sealed class SensorTile : Tile
{
    readonly string _key;

    public SensorTile(KeyConfig cfg, string key) : base(cfg, SensorFormat.GroupColor(key) ?? Painter.DefaultAccent) => _key = key;

    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        var reading = ctx.Sensors.Get(_key);
        var color = SensorFormat.Accent(reading, Config, Accent);
        bool gauge = string.Equals(Config.Style, "gauge", StringComparison.OrdinalIgnoreCase);
        if (gauge) g.Clear(Color.Black); // gauges sit on plain black; the ring carries the colour
        else Painter.Background(g, r, color);
        string title = Config.Label ?? SensorFormat.Title(_key, reading);
        string value = SensorFormat.Value(reading);
        float max = SensorFormat.Max(reading, Config.Max);
        var dim = Color.FromArgb(200, 255, 255, 255);

        if (gauge)
        {
            // Ring on top, label in and below the gap at the bottom.
            float size = r.Height - 44;
            var ring = new RectangleF(r.X + (r.Width - size) / 2, r.Y + 10, size, size);
            Painter.Gauge(g, ring, (reading?.Value ?? 0) / max, color, 14);
            Painter.Text(g, value, Painter.Numbers, FontStyle.Bold, 46, new RectangleF(ring.X + 22, ring.Y + size * 0.30f, size - 44, size * 0.38f), Color.White);
            Painter.Text(g, title, Painter.Ui, FontStyle.Regular, 22, new RectangleF(r.X + 8, r.Bottom - 40, r.Width - 16, 32), dim);
            return;
        }

        Painter.Sparkline(g, new RectangleF(r.X, r.Y + r.Height * 0.55f, r.Width, r.Height * 0.45f),
            reading?.History ?? [], color, SensorHub.HistoryLength, max: max);
        Painter.Text(g, title, Painter.Ui, FontStyle.Regular, 24, new RectangleF(r.X + 14, r.Y + 10, r.Width - 28, 32), dim, StringAlignment.Near);
        Painter.Text(g, value, Painter.Numbers, FontStyle.Bold, 70, new RectangleF(r.X + 6, r.Y + 36, r.Width - 12, 80), Color.White);

        string? sub = Subtitle(ctx.Sensors);
        if (sub != null)
            Painter.Text(g, sub, Painter.Ui, FontStyle.Regular, 18, new RectangleF(r.X, r.Y + 112, r.Width, 26), dim);
    }

    // A second line for the sensors where one obviously helps.
    string? Subtitle(SensorHub hub)
    {
        switch (_key.ToLowerInvariant())
        {
            case "ram.load":
                if (hub.Get("ram.used")?.Value is float used && hub.Get("ram.free")?.Value is float free)
                    return $"{used:0.0} / {used + free:0} GB";
                return null;
            case "gpu.vram":
                return hub.Get("gpu.vramused") is { Value: not null } v ? SensorFormat.Value(v) + " used" : null;
            case "cpu.temp" or "cpu.power" or "cpu.clock":
                return hub.Get(_key) is { Value: null } ? "needs admin + PawnIO" : null;
            default:
                return null;
        }
    }
}

/// <summary>A wireless device's battery: percentage, a battery bar, and a bolt while charging.</summary>
sealed class BatteryTile(KeyConfig cfg) : Tile(cfg, Color.FromArgb(52, 199, 89))
{
    public override int RefreshMs => 1000; // cheap: reads the hub's snapshot; unchanged images aren't resent

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        var reading = ctx.Batteries.Find(Config.Battery ?? "any", Config.Match);
        int? pct = reading?.Percent;
        bool online = reading?.Online == true;

        // Green, orange at 30% and below, red at 15% and below (warn/crit override these).
        float warn = Config.Warn ?? 30, crit = Config.Crit ?? 15;
        var level = pct is int p ? (p <= crit ? SensorFormat.Crit : p <= warn ? SensorFormat.Warn : Accent) : Painter.DefaultAccent;
        var color = online ? level : Painter.Mix(level, Color.Black, 0.55f);
        Painter.Background(g, r, color);

        var dim = Color.FromArgb(online ? 200 : 120, 255, 255, 255);
        string title = Config.Label ?? Config.Match ?? (reading != null ? BatteryHub.ShortName(reading.Name) : "Battery");
        Painter.Text(g, title, Painter.Ui, FontStyle.Regular, 24, new RectangleF(r.X + 14, r.Y + 10, r.Width - 28, 32), dim, StringAlignment.Near);

        string value = pct is int v ? v + "%" : "–";
        Painter.Text(g, value, Painter.Numbers, FontStyle.Bold, 64, new RectangleF(r.X + 6, r.Y + 38, r.Width - 12, 72),
            online ? Color.White : Color.FromArgb(140, 255, 255, 255));

        var bar = new RectangleF(r.X + 38, r.Y + 122, r.Width - 76, 44);
        Painter.Battery(g, bar, (pct ?? 0) / 100f, color, reading?.Charging == true && online, dim);

        string? note = reading == null ? "not found" : !online ? "last seen " + Ago(reading.LastSeen) : reading.Detail;
        if (note != null)
            Painter.Text(g, note, Painter.Ui, FontStyle.Regular, 18, new RectangleF(r.X, r.Bottom - 28, r.Width, 24), dim);
    }

    public static Color LevelColor(int? pct, KeyConfig cfg, Color normal)
    {
        float warn = cfg.Warn ?? 30, crit = cfg.Crit ?? 15;
        return pct is int p ? (p <= crit ? SensorFormat.Crit : p <= warn ? SensorFormat.Warn : normal) : Painter.DefaultAccent;
    }

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }
}

/// <summary>
/// Three devices per tile: name, percentage and a level bar each. Every "batteries" tile on a page
/// takes the next three devices (Group is set by page order), so a 4th device spills into the
/// next one; a tile with nothing left to show stays black. Pressing it opens the battery page.
/// </summary>
sealed class MultiBatteryTile(KeyConfig cfg) : Tile(cfg, Color.FromArgb(52, 199, 89))
{
    public const int PerTile = 3;

    /// <summary>0 = devices 1-3, 1 = devices 4-6, ...</summary>
    public int Group { get; set; }

    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        var devices = ctx.Batteries.Devices().Skip(Group * PerTile).Take(PerTile).ToList();
        if (devices.Count == 0) { g.Clear(Color.Black); return; }

        Painter.Background(g, r, Painter.Mix(Accent, Painter.Base, 0.5f));
        float rowH = (r.Height - 8f) / PerTile;
        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            bool online = d.Online;
            var level = BatteryTile.LevelColor(d.Percent, Config, Accent);
            var text = online ? Color.White : Color.FromArgb(120, 255, 255, 255);
            var row = new RectangleF(r.X + 12, r.Y + 6 + i * rowH, r.Width - 24, rowH);

            string pct = d.Percent is int p ? p + "%" : "–";
            if (d.Charging && online) pct = "+" + pct;
            using var vf = new Font(Painter.Numbers, 30, FontStyle.Bold, GraphicsUnit.Pixel);
            float vw = g.MeasureString(pct, vf).Width;
            Painter.Text(g, pct, Painter.Numbers, FontStyle.Bold, 30, new RectangleF(row.X, row.Y, row.Width, rowH - 14), text, StringAlignment.Far);
            Painter.Text(g, BatteryHub.ShortName(d.Name), Painter.Ui, FontStyle.Regular, 20,
                new RectangleF(row.X, row.Y, row.Width - vw, rowH - 14), online ? Color.FromArgb(210, 255, 255, 255) : text, StringAlignment.Near, wrap: false);

            // Level bar under the row.
            var bar = new RectangleF(row.X, row.Bottom - 13, row.Width, 7);
            using (var track = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(track, bar);
            float w = bar.Width * Math.Clamp((d.Percent ?? 0) / 100f, 0, 1);
            using (var fill = new SolidBrush(online ? level : Painter.Mix(level, Color.Black, 0.55f))) g.FillRectangle(fill, bar.X, bar.Y, w, bar.Height);
        }
    }
}

sealed class ClockTile(KeyConfig cfg) : Tile(cfg, Color.FromArgb(245, 166, 35))
{
    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        Painter.Background(g, r, Accent);
        var c = CultureInfo.CurrentCulture;
        Painter.Text(g, ctx.Now.ToString("HH:mm", c), Painter.Numbers, FontStyle.Bold, 66, new RectangleF(r.X, r.Y + r.Height * 0.16f, r.Width, r.Height * 0.44f), Color.White);
        Painter.Text(g, ctx.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Painter.Ui, FontStyle.Regular, 24, new RectangleF(r.X + 6, r.Y + r.Height * 0.62f, r.Width - 12, r.Height * 0.22f), Color.FromArgb(210, 255, 255, 255));
    }
}

/// <summary>Up to three sensors over the last minute. Meant for the small screen, but works on a key too.</summary>
sealed class GraphTile(KeyConfig cfg) : Tile(cfg, Color.FromArgb(63, 169, 245))
{
    static readonly Color[] Palette = [Color.FromArgb(63, 169, 245), Color.FromArgb(181, 124, 255), Color.FromArgb(118, 185, 0)];

    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        g.Clear(Painter.Base);
        var keys = (Config.Sensors is { Count: > 0 } s ? s : ["cpu.load", "ram.load"]).Take(3).ToList();
        var readings = keys.Select(k => ctx.Sensors.Get(k)).ToList();
        var colors = SeriesColors(keys);
        bool wide = r.Width > r.Height * 1.5f;
        float legendW = wide ? (keys.Count > 2 ? 215 : 150) : 0;

        var chart = new RectangleF(r.X + legendW + 8, r.Y + 14, r.Width - legendW - 22, r.Height - 28);
        using (var grid = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            for (int i = 1; i <= 3; i++)
            {
                float y = chart.Bottom - chart.Height * i / 4f;
                g.DrawLine(grid, chart.Left, y, chart.Right, y);
            }

        // Draw back to front so the first sensor sits on top, with the only fill.
        for (int i = keys.Count - 1; i >= 0; i--)
            Painter.Sparkline(g, chart, readings[i]?.History ?? [], colors[i], SensorHub.HistoryLength,
                fill: i == 0, max: SensorFormat.Max(readings[i], Config.Max));

        if (wide)
        {
            float rowH = (r.Height - 24f) / keys.Count;
            for (int i = 0; i < keys.Count; i++)
                Legend(g, SensorFormat.Title(keys[i], readings[i]), SensorFormat.Value(readings[i]), colors[i],
                    new RectangleF(r.X + 16, r.Y + 12 + i * rowH, legendW - 8, rowH), keys.Count);
        }
        else
        {
            Painter.Text(g, SensorFormat.Value(readings[0]), Painter.Numbers, FontStyle.Bold, 44, new RectangleF(r.X, r.Y + 8, r.Width, 56), Color.White);
        }
    }

    /// <summary>Series take their family colour (CPU blue, GPU green, RAM golden); repeats get lighter shades.</summary>
    static Color[] SeriesColors(List<string> keys)
    {
        var result = new Color[keys.Count];
        var seen = new Dictionary<Color, int>();
        for (int i = 0; i < keys.Count; i++)
        {
            var baseColor = SensorFormat.GroupColor(keys[i]) ?? Palette[i];
            int n = seen.TryGetValue(baseColor, out var c) ? c : 0;
            seen[baseColor] = n + 1;
            result[i] = n == 0 ? baseColor : Painter.Mix(baseColor, Color.White, 0.35f * n);
        }
        return result;
    }

    static void Legend(Graphics g, string name, string value, Color color, RectangleF box, int rows)
    {
        if (rows > 2)
        {
            // Three rows are too short to stack: name on the left, value right-aligned on the same line.
            using var vf = new Font(Painter.Numbers, 32, FontStyle.Bold, GraphicsUnit.Pixel);
            float vw = g.MeasureString(value, vf).Width;
            Painter.Text(g, value, Painter.Numbers, FontStyle.Bold, 32, box, Color.White, StringAlignment.Far);
            Painter.Text(g, name, Painter.Ui, FontStyle.Regular, 26, new RectangleF(box.X, box.Y, box.Width - vw - 2, box.Height), color, StringAlignment.Near);
            return;
        }
        Painter.Text(g, name, Painter.Ui, FontStyle.Regular, 28, new RectangleF(box.X, box.Y, box.Width, 38), color, StringAlignment.Near);
        Painter.Text(g, value, Painter.Numbers, FontStyle.Bold, 46, new RectangleF(box.X, box.Y + 32, box.Width, box.Height - 32), Color.White, StringAlignment.Near, StringAlignment.Near);
    }
}
