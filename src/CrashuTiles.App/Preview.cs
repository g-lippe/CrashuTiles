using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Rendering;
using CrashuTiles.Tiles;
using static CrashuTiles.Device.D200Protocol;

namespace CrashuTiles;

/// <summary>
/// "CrashuTiles.exe --preview out.png [page] [--default]" renders a page laid out like the
/// dock, without touching the device. Handy for checking tile designs.
/// </summary>
static class Preview
{
    const int Gap = SmallWidth - 2 * KeySize; // so the small screen spans exactly two key cells

    public static int Run(string[] args)
    {
        string output = args.Length > 1 ? args[1] : "preview.png";
        bool useDefault = args.Contains("--default");
        string? page = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
        var cfg = useDefault ? AppConfig.Parse(DefaultConfig.Json) : AppConfig.LoadOrCreate(AppPaths.Config);
        page ??= cfg.StartPage;
        var keys = cfg.Pages[page];

        // Give the graphs something to show.
        using var stats = new SystemStats(autoSample: false);
        for (int s = 0; s < 12; s++) { Thread.Sleep(250); stats.Sample(); }

        int pad = 30;
        int w = pad * 2 + 5 * KeySize + 4 * Gap, h = pad * 2 + 3 * KeySize + 2 * Gap;
        using var sheet = Painter.Canvas(w, h, out var g);
        using (g)
        {
            g.Clear(Color.FromArgb(40, 42, 48));
            var ctx = new TileContext(stats, DateTime.Now, false);
            for (int i = 0; i <= KeyCount; i++)
            {
                bool small = i == SmallScreenIndex;
                int col = small ? 3 : i % Columns, row = small ? 2 : i / Columns;
                int x = pad + col * (KeySize + Gap), y = pad + row * (KeySize + Gap);
                keys.TryGetValue(small ? "small" : i.ToString(), out var kc);
                byte[] png;
                if (kc != null && !(small && kc.Native != null)) png = Tile.Create(kc).Render(small ? SmallWidth : KeySize, KeySize, ctx);
                else if (small) png = NativePlaceholder(kc?.Native ?? "clock");
                else png = Tile.Blank(KeySize, KeySize);
                using var ms = new MemoryStream(png);
                using var img = Image.FromStream(ms);
                g.DrawImage(img, x, y, img.Width, img.Height);
            }
        }
        sheet.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return 0;
    }

    static byte[] NativePlaceholder(string mode)
    {
        using var bmp = Painter.Canvas(SmallWidth, SmallHeight, out var g);
        using (g)
        {
            g.Clear(Color.Black);
            Painter.Text(g, $"firmware: {mode}", Painter.Ui, FontStyle.Regular, 30, new RectangleF(0, 0, SmallWidth, SmallHeight), Color.Gray);
        }
        return Painter.Png(bmp);
    }
}
