using System.Globalization;
using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Rendering;

namespace CrashuTiles.Tiles;

sealed record TileContext(SystemStats Stats, DateTime Now, bool Pressed);

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
        "cpu" => new MetricTile(cfg, MetricTile.Kind.Cpu),
        "ram" => new MetricTile(cfg, MetricTile.Kind.Ram),
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

/// <summary>CPU or RAM load: big number with a one-minute sparkline.</summary>
sealed class MetricTile : Tile
{
    public enum Kind { Cpu, Ram }
    readonly Kind _kind;

    public MetricTile(KeyConfig cfg, Kind kind)
        : base(cfg, kind == Kind.Cpu ? Color.FromArgb(63, 169, 245) : Color.FromArgb(181, 124, 255)) => _kind = kind;

    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        Painter.Background(g, r, Accent);
        var s = ctx.Stats;
        var (cpuHist, ramHist) = s.History();
        float[] hist = _kind == Kind.Cpu ? cpuHist : ramHist;
        int value = _kind == Kind.Cpu ? s.Cpu : s.Ram;

        Painter.Sparkline(g, new RectangleF(r.X, r.Y + r.Height * 0.55f, r.Width, r.Height * 0.45f), hist, Accent, SystemStats.HistoryLength);

        string title = Config.Label ?? (_kind == Kind.Cpu ? "CPU" : "RAM");
        Painter.Text(g, title, Painter.Ui, FontStyle.Regular, 24, new RectangleF(r.X + 14, r.Y + 10, r.Width - 28, 32),
            Color.FromArgb(200, 255, 255, 255), StringAlignment.Near);
        Painter.Text(g, value + "%", Painter.Numbers, FontStyle.Bold, 70, new RectangleF(r.X, r.Y + 36, r.Width, 80), Color.White);
        if (_kind == Kind.Ram)
            Painter.Text(g, $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB", Painter.Ui, FontStyle.Regular, 18,
                new RectangleF(r.X, r.Y + 112, r.Width, 26), Color.FromArgb(200, 255, 255, 255));
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
        Painter.Text(g, ctx.Now.ToString("ddd d MMM", c), Painter.Ui, FontStyle.Regular, 24, new RectangleF(r.X + 6, r.Y + r.Height * 0.62f, r.Width - 12, r.Height * 0.22f), Color.FromArgb(210, 255, 255, 255));
    }
}

/// <summary>CPU and RAM over the last minute. Meant for the small screen, but works on a key too.</summary>
sealed class GraphTile(KeyConfig cfg) : Tile(cfg, Color.FromArgb(63, 169, 245))
{
    static readonly Color CpuColor = Color.FromArgb(63, 169, 245);
    static readonly Color RamColor = Color.FromArgb(181, 124, 255);

    public override int RefreshMs => 1000;

    public override void Draw(Graphics g, Rectangle r, TileContext ctx)
    {
        g.Clear(Painter.Base);
        var s = ctx.Stats;
        var (cpu, ram) = s.History();
        bool wide = r.Width > r.Height * 1.5f;
        float legendW = wide ? 150 : 0;

        var chart = new RectangleF(r.X + legendW + 8, r.Y + 14, r.Width - legendW - 22, r.Height - 28);
        using (var grid = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            for (int i = 1; i <= 3; i++)
            {
                float y = chart.Bottom - chart.Height * i / 4f;
                g.DrawLine(grid, chart.Left, y, chart.Right, y);
            }
        Painter.Sparkline(g, chart, ram, RamColor, SystemStats.HistoryLength, fill: false);
        Painter.Sparkline(g, chart, cpu, CpuColor, SystemStats.HistoryLength);

        if (wide)
        {
            Legend(g, "CPU", s.Cpu, CpuColor, new RectangleF(r.X + 16, r.Y + 18, legendW - 8, 80));
            Legend(g, "RAM", s.Ram, RamColor, new RectangleF(r.X + 16, r.Y + 102, legendW - 8, 80));
        }
        else
        {
            Painter.Text(g, $"{s.Cpu}%", Painter.Numbers, FontStyle.Bold, 44, new RectangleF(r.X, r.Y + 8, r.Width, 56), Color.White);
        }
    }

    static void Legend(Graphics g, string name, int value, Color color, RectangleF box)
    {
        Painter.Text(g, name, Painter.Ui, FontStyle.Regular, 22, new RectangleF(box.X, box.Y, box.Width, 28), color, StringAlignment.Near);
        Painter.Text(g, value + "%", Painter.Numbers, FontStyle.Bold, 48, new RectangleF(box.X, box.Y + 24, box.Width, 54), Color.White, StringAlignment.Near);
    }
}
