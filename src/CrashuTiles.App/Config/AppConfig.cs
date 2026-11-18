using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrashuTiles.Config;

/// <summary>
/// The whole config file. Keys on a page are "0".."12" (index = row * 5 + col)
/// plus "small" for the wide screen, which is also pressable.
/// </summary>
public sealed class AppConfig
{
    public int Brightness { get; set; } = 80;
    public string StartPage { get; set; } = "main";
    public Dictionary<string, Dictionary<string, KeyConfig>> Pages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppConfig Parse(string json)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? throw new JsonException("The config is empty");
        // keep page lookups case-insensitive after deserialization
        cfg.Pages = new Dictionary<string, Dictionary<string, KeyConfig>>(cfg.Pages, StringComparer.OrdinalIgnoreCase);
        if (cfg.Pages.Count == 0) throw new JsonException("The config has no pages");
        return cfg;
    }

    /// <summary>Loads the user's config, writing the default one on first run.</summary>
    public static AppConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DefaultConfig.Json);
        }
        return Parse(File.ReadAllText(path));
    }
}

public sealed class KeyConfig
{
    /// <summary>label (default), sensor, battery, clock, graph; cpu / ram / gpu are shortcuts for sensor tiles.</summary>
    public string? Tile { get; set; }
    /// <summary>Sensor tiles: an alias like "gpu.temp" or a raw LibreHardwareMonitor id like "/gpu-nvidia/0/temperature/0".</summary>
    public string? Sensor { get; set; }
    /// <summary>Graph tiles: up to three sensors to plot (default cpu.load and ram.load).</summary>
    public List<string>? Sensors { get; set; }
    /// <summary>Sensor tiles: "spark" (number + sparkline, default) or "gauge" (ring).</summary>
    public string? Style { get; set; }
    /// <summary>Top of the scale for sparklines and gauges. Defaults to 100 for % and °C, else the recent peak.</summary>
    public float? Max { get; set; }
    /// <summary>Battery tiles: logitech, corsair, bluetooth, xinput or any.</summary>
    public string? Battery { get; set; }
    /// <summary>Battery tiles: part of the device name to pick (e.g. "G502", "AirPods"); default the first device.</summary>
    public string? Match { get; set; }
    /// <summary>The tile turns orange at or above Warn and red at or above Crit.</summary>
    public float? Warn { get; set; }
    public float? Crit { get; set; }
    public string? Label { get; set; }
    /// <summary>"glyph:E768" (Segoe Fluent Icons code), a .png/.jpg/.ico/.exe path, or omitted (launch keys use the app's icon).</summary>
    public string? Icon { get; set; }
    /// <summary>Accent colour, "#RRGGBB".</summary>
    public string? Color { get; set; }
    /// <summary>Small screen only: let the firmware draw clock, clock-date, clock-weekday, clock-full, dial or stats.</summary>
    public string? Native { get; set; }
    /// <summary>What a tap does (fires on press, or on release when Hold is also set).</summary>
    public ActionConfig? Action { get; set; }
    /// <summary>What holding the key for half a second does.</summary>
    public ActionConfig? Hold { get; set; }
}

public sealed class ActionConfig
{
    /// <summary>launch, url, hotkey, page, back, config.</summary>
    public string Type { get; set; } = "";
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Url { get; set; }
    public string? Keys { get; set; }
    public string? Page { get; set; }
}
