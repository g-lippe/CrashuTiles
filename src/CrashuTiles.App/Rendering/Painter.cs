using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CrashuTiles.Rendering;

/// <summary>Shared drawing helpers: palette, fonts, shrink-to-fit text, icons, sparklines, PNG encoding.</summary>
static class Painter
{
    public static readonly Color Base = Color.FromArgb(18, 20, 26);
    public static readonly Color DefaultAccent = Color.FromArgb(90, 100, 120);

    public static readonly FontFamily Numbers = Family("Bahnschrift", "Segoe UI");
    public static readonly FontFamily Ui = Family("Segoe UI Semibold", "Segoe UI");
    public static readonly FontFamily Icons = Family("Segoe Fluent Icons", "Segoe MDL2 Assets");

    static FontFamily Family(params string[] names)
    {
        foreach (var n in names)
        {
            try { return new FontFamily(n); } catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    public static Bitmap Canvas(int w, int h, out Graphics g)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return bmp;
    }

    public static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return ColorTranslator.FromHtml(hex.Trim()); } catch { return fallback; }
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Dark tile with a soft tint of the accent at the top.</summary>
    public static void Background(Graphics g, Rectangle r, Color accent)
    {
        using var b = new LinearGradientBrush(r, Mix(Base, accent, 0.30f), Mix(Base, Color.Black, 0.35f), 90f);
        g.FillRectangle(b, r);
    }

    /// <summary>Draws text at the largest size up to maxPx that fits the box (wrapping on spaces).</summary>
    public static void Text(Graphics g, string text, FontFamily family, FontStyle style, float maxPx, RectangleF box,
        Color color, StringAlignment h = StringAlignment.Center, StringAlignment v = StringAlignment.Center)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var fmt = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisWord };
        float px = maxPx;
        Font font = new(family, px, style, GraphicsUnit.Pixel);
        while (px > 10 && !Fits(g, text, font, box))
        {
            font.Dispose();
            px -= 2;
            font = new Font(family, px, style, GraphicsUnit.Pixel);
        }
        using (font)
        using (var brush = new SolidBrush(color))
            g.DrawString(text, font, brush, box, fmt);
    }

    static bool Fits(Graphics g, string text, Font font, RectangleF box)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (g.MeasureString(word, font).Width > box.Width) return false;
        return g.MeasureString(text, font, (int)box.Width).Height <= box.Height;
    }

    /// <summary>Line + filled area for values 0..max, newest at the right edge.</summary>
    public static void Sparkline(Graphics g, RectangleF r, float[] values, Color color, int capacity, bool fill = true, float max = 100)
    {
        if (values.Length < 2 || max <= 0) return;
        float step = r.Width / (capacity - 1);
        var pts = new PointF[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float x = r.Right - (values.Length - 1 - i) * step;
            float y = r.Bottom - Math.Clamp(values[i], 0, max) / max * r.Height;
            pts[i] = new PointF(x, y);
        }
        if (fill)
        {
            using var path = new GraphicsPath();
            path.AddLines(pts);
            path.AddLine(pts[^1], new PointF(pts[^1].X, r.Bottom));
            path.AddLine(new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
            path.CloseFigure();
            using var b = new LinearGradientBrush(r, Alpha(color, 110), Alpha(color, 10), 90f);
            g.FillPath(b, path);
        }
        using var pen = new Pen(color, 3f) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    /// <summary>A 270-degree ring gauge (gap at the bottom) filled to fraction 0..1.</summary>
    public static void Gauge(Graphics g, RectangleF box, float fraction, Color color, float thickness)
    {
        var arc = RectangleF.Inflate(box, -thickness / 2, -thickness / 2);
        using (var track = new Pen(Alpha(Color.White, 34), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(track, arc, 135, 270);
        float sweep = 270 * Math.Clamp(fraction, 0, 1);
        if (sweep < 1) return;
        using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, arc, 135, sweep);
    }

    /// <summary>A horizontal battery outline filled to fraction 0..1, with an optional charging bolt.</summary>
    public static void Battery(Graphics g, RectangleF box, float fraction, Color fill, bool charging, Color outline)
    {
        float capW = box.Height * 0.18f, stroke = Math.Max(3, box.Height * 0.09f);
        var body = new RectangleF(box.X, box.Y, box.Width - capW, box.Height);
        using (var pen = new Pen(outline, stroke))
            g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
        using (var cap = new SolidBrush(outline))
            g.FillRectangle(cap, body.Right + stroke / 2, box.Y + box.Height * 0.3f, capW - stroke / 2, box.Height * 0.4f);
        var inner = RectangleF.Inflate(body, -stroke * 1.4f, -stroke * 1.4f);
        float w = inner.Width * Math.Clamp(fraction, 0, 1);
        if (w > 0.5f)
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, inner.X, inner.Y, w, inner.Height);
        if (!charging) return;
        // Lightning bolt in the middle, outlined so it reads over any fill.
        float cx = body.X + body.Width / 2, cy = body.Y + body.Height / 2, h = body.Height * 0.8f, bw = h * 0.42f;
        PointF[] bolt =
        [
            new(cx + bw * 0.15f, cy - h / 2), new(cx - bw * 0.5f, cy + h * 0.06f), new(cx - bw * 0.02f, cy + h * 0.06f),
            new(cx - bw * 0.15f, cy + h / 2), new(cx + bw * 0.5f, cy - h * 0.06f), new(cx + bw * 0.02f, cy - h * 0.06f),
        ];
        using (var p = new Pen(Base, stroke * 0.8f) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, bolt);
        using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, bolt);
    }

    // ---------- icons ----------

    static readonly Dictionary<string, Image?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Draws "glyph:XXXX" or an image/exe icon, fitted into the box.</summary>
    public static void Icon(Graphics g, string spec, RectangleF box, Color color)
    {
        if (spec.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(spec.AsSpan(6), System.Globalization.NumberStyles.HexNumber, null, out int code)) return;
            using var font = new Font(Icons, box.Height * 0.78f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(char.ConvertFromUtf32(code), font, brush, box, fmt);
            return;
        }

        var img = LoadImage(spec);
        if (img == null) return;
        float scale = Math.Min(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    static Image? LoadImage(string path)
    {
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        Image? img = null;
        try
        {
            string? full = AppResolver.Resolve(path);
            if (full != null)
            {
                string ext = Path.GetExtension(full).ToLowerInvariant();
                if (ext is ".exe" or ".dll" or ".ico")
                    img = System.Drawing.Icon.ExtractIcon(full, 0, 256)?.ToBitmap()
                          ?? System.Drawing.Icon.ExtractAssociatedIcon(full)?.ToBitmap();
                else
                    using (var fs = File.OpenRead(full)) img = new Bitmap(Image.FromStream(fs));
            }
        }
        catch (Exception e) { Log.Info($"Icon '{path}' failed: {e.Message}"); }
        IconCache[path] = img;
        return img;
    }
}

/// <summary>Finds "notepad.exe"-style names the way Run does: as given, then Windows, System32 and PATH.</summary>
static class AppResolver
{
    public static string? Resolve(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
        if (File.Exists(path)) return Path.GetFullPath(path);
        if (Path.IsPathRooted(path)) return null;
        var dirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.SystemDirectory,
        };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs)
        {
            try
            {
                string candidate = Path.Combine(d, path);
                if (File.Exists(candidate)) return candidate;
                if (!Path.HasExtension(candidate) && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
