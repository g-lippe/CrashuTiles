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
        int ci = Array.IndexOf(args, "--config");
        string? configPath = ci >= 0 && ci + 1 < args.Length ? args[ci + 1] : null;
        string? page = args.Skip(2).Where((a, i) => !a.StartsWith("--") && a != configPath).FirstOrDefault();
        var cfg = useDefault ? AppConfig.Parse(DefaultConfig.Json)
            : configPath != null ? AppConfig.Parse(File.ReadAllText(configPath))
            : AppConfig.LoadOrCreate(AppPaths.Config);
        page ??= cfg.StartPage;

        // Tiles request their sensors on first draw, so draw once to register them,
        // then sample for a few seconds so the graphs have something to show.
        using var sensors = new SensorHub(autoSample: false);
        using var batteries = new CrashuTiles.Data.Batteries.BatteryHub(autoPoll: false);
        using var media = new MediaHub(autoPoll: false);
        using var audio = new AudioHub(autoPoll: false);
        sensors.Sample();
        batteries.Poll();
        media.PollAsync().GetAwaiter().GetResult();
        audio.Poll();
        var keys = page == Deck.DeckController.BatteryPageName ? Deck.DeckController.BatteryPage(batteries)
            : page == Deck.DeckController.AudioPageName ? Deck.DeckController.AudioPage(audio)
            : cfg.Pages[page];
        var tiles = new Dictionary<int, Tile>();
        for (int i = 0; i <= KeyCount; i++)
            if (keys.TryGetValue(i == SmallScreenIndex ? "small" : i.ToString(), out var kc) && !(i == SmallScreenIndex && kc.Native != null))
                tiles[i] = Tile.Create(kc);
        Tile.AssignGroups(Enumerable.Range(0, KeyCount + 1).Select(i => tiles.GetValueOrDefault(i)));
        var warm = new TileContext(sensors, batteries, media, audio, DateTime.Now, false);
        foreach (var (i, t) in tiles) t.Render(i == SmallScreenIndex ? SmallWidth : KeySize, KeySize, warm);
        for (int s = 0; s < 8; s++) { Thread.Sleep(500); sensors.Sample(); }

        int pad = 30;
        int w = pad * 2 + 5 * KeySize + 4 * Gap, h = pad * 2 + 3 * KeySize + 2 * Gap;
        using var sheet = Painter.Canvas(w, h, out var g);
        using (g)
        {
            g.Clear(Color.FromArgb(40, 42, 48));
            var ctx = new TileContext(sensors, batteries, media, audio, DateTime.Now, false);
            for (int i = 0; i <= KeyCount; i++)
            {
                bool small = i == SmallScreenIndex;
                int col = small ? 3 : i % Columns, row = small ? 2 : i / Columns;
                int x = pad + col * (KeySize + Gap), y = pad + row * (KeySize + Gap);
                keys.TryGetValue(small ? "small" : i.ToString(), out var kc);
                byte[] png;
                if (tiles.TryGetValue(i, out var tile)) png = tile.Render(small ? SmallWidth : KeySize, KeySize, ctx);
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
