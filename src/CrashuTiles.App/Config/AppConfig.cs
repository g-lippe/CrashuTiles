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

    /// <summary>camelCase, indented, nulls left out: the shape the settings page and the file both use.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "°", accents, "&" readable
    };

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>
    /// Writes the config (keeping the reference comments from the default config at the top)
    /// after saving the previous file as config.json.bak.
    /// </summary>
    public void Save(string path)
    {
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        var header = DefaultConfig.Json.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.TrimStart().StartsWith("//"));
        string json = ToJson();
        // json starts with "{"; put the comment block right after it.
        string text = "{\n" + string.Join("\n", header) + "\n" + json[1..].TrimStart('\r', '\n').Insert(0, "\n");
        File.WriteAllText(path, text.Replace("\r\n", "\n"));
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
    /// <summary>label (default), sensor, battery, batteries, media, clock, graph; cpu / ram / gpu are shortcuts for sensor tiles.</summary>
    public string? Tile { get; set; }
    /// <summary>Sensor tiles: an alias like "gpu.temp" or a raw LibreHardwareMonitor id like "/gpu-nvidia/0/temperature/0".</summary>
    public string? Sensor { get; set; }
    /// <summary>Graph tiles: up to three sensors to plot (default cpu.load and ram.load).</summary>
    public List<string>? Sensors { get; set; }
    /// <summary>Sensor tiles: "spark" (number + sparkline, default) or "gauge" (ring).</summary>
    public string? Style { get; set; }
    /// <summary>Top of the scale for sparklines and gauges. Defaults to 100 for % and °C, else the recent peak.</summary>
    public float? Max { get; set; }
    /// <summary>Media tiles: info (default), playpause, next, prev. Audio tiles: output (default), mute, mic, volup, voldown.</summary>
    public string? Control { get; set; }
    /// <summary>Audio volup / voldown tiles: percentage points per press (default 5).</summary>
    public int? Step { get; set; }
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
    /// <summary>launch, url, hotkey, media, audio, page, back, config.</summary>
    public string Type { get; set; } = "";
    /// <summary>media: playpause, play, pause, next, prev. audio: mute, micmute, volup, voldown, output (sets Device as default).</summary>
    public string? Command { get; set; }
    /// <summary>audio output command: the device to make default (id or part of its name).</summary>
    public string? Device { get; set; }
    /// <summary>audio volup / voldown: percentage points (default 5).</summary>
    public int? Step { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Url { get; set; }
    public string? Keys { get; set; }
    public string? Page { get; set; }
}
