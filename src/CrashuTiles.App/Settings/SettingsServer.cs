using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Data.Batteries;
using CrashuTiles.Deck;
using CrashuTiles.Tiles;

namespace CrashuTiles.Settings;

/// <summary>
/// The settings page: a small HTTP server on http://localhost:4325 serving the page (embedded
/// in the exe) and a JSON API. Saving writes config.json; the file watcher then reloads the dock.
///
/// Only this machine can reach it, and every API call needs the random token handed to the
/// browser when the page is opened from the tray. A web page elsewhere can't read the token,
/// and the custom header it travels in can't be sent cross-origin without a CORS preflight
/// (which we never approve), so other sites can't rewrite the config or its "launch" actions.
/// </summary>
sealed class SettingsServer : IDisposable
{
    public const int Port = 4325;
    const string TokenHeader = "X-CrashuTiles-Token";

    readonly HttpListener _listener = new();
    readonly DeckController _deck;
    readonly BatteryHub _batteries;
    readonly AudioHub _audio;
    readonly string _token = LoadOrCreateToken();
    readonly CancellationTokenSource _cts = new();

    public SettingsServer(DeckController deck, BatteryHub batteries, AudioHub audio)
    {
        _deck = deck;
        _batteries = batteries;
        _audio = audio;
        _listener.Prefixes.Add($"http://localhost:{Port}/");
    }

    /// <summary>Raised (on a background thread) after the page saved the config.</summary>
    public event Action? Saved;

    /// <summary>The address to open, token included.</summary>
    public string Url => $"http://localhost:{Port}/#token={_token}";

    public bool Start()
    {
        try
        {
            _listener.Start();
            _ = Loop();
            Log.Info($"Settings page at http://localhost:{Port}/");
            try { File.WriteAllText(Path.Combine(AppPaths.Folder, "settings-url.txt"), Url); } catch { }
            return true;
        }
        catch (Exception e)
        {
            Log.Info("Settings server could not start: " + e.Message);
            return false;
        }
    }

    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            // DNS-rebinding guard: only answer requests addressed to localhost.
            string host = req.UserHostName ?? "";
            if (!host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                await Send(res, 403, "text/plain", "Forbidden");
                return;
            }

            string path = req.Url?.AbsolutePath ?? "/";
            if (!path.StartsWith("/api/"))
            {
                await ServeStatic(res, path == "/" ? "index.html" : path.TrimStart('/'));
                return;
            }

            if (req.Headers[TokenHeader] != _token)
            {
                await SendJson(res, 401, new { error = "Open the settings page from the CrashuTiles tray icon." });
                return;
            }

            switch (req.HttpMethod, path)
            {
                case ("GET", "/api/config"):
                    var cfg = AppConfig.LoadOrCreate(AppPaths.Config);
                    await Send(res, 200, "application/json", cfg.ToJson());
                    break;

                case ("POST", "/api/config"):
                {
                    string body = await Body(req);
                    AppConfig parsed;
                    try { parsed = AppConfig.Parse(body); }
                    catch (Exception e) { await SendJson(res, 400, new { error = "Invalid config: " + e.Message }); return; }
                    if (!parsed.Pages.ContainsKey(parsed.StartPage))
                    {
                        await SendJson(res, 400, new { error = $"The start page '{parsed.StartPage}' doesn't exist." });
                        return;
                    }
                    parsed.Save(AppPaths.Config);
                    Log.Info("Config saved from the settings page");
                    Saved?.Invoke(); // reload right away instead of waiting on the file watcher
                    await SendJson(res, 200, new { ok = true });
                    break;
                }

                case ("GET", "/api/catalog"):
                    await SendJson(res, 200, Catalog());
                    break;

                case ("POST", "/api/render"):
                {
                    var r = JsonSerializer.Deserialize<RenderRequest>(await Body(req), AppConfig.WriteOptions);
                    if (r?.Key == null) { await SendJson(res, 400, new { error = "No key" }); return; }
                    byte[] png = await _deck.RenderAsync(r.Key, r.Small, r.Group);
                    res.Headers["Cache-Control"] = "no-store";
                    await Send(res, 200, "image/png", png);
                    break;
                }

                default:
                    await SendJson(res, 404, new { error = "Not found" });
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Info($"Settings request {req.HttpMethod} {req.Url?.AbsolutePath} failed: {e.Message}");
            try { await SendJson(res, 500, new { error = e.Message }); } catch { }
        }
    }

    sealed record RenderRequest(KeyConfig? Key, bool Small, int Group);

    /// <summary>
    /// The key is kept in the data folder so an open settings tab keeps working after a restart.
    /// Local programs can already edit config.json, so it's no secret from them; it keeps
    /// websites out. settings-url.txt next to it holds the full address.
    /// </summary>
    static string LoadOrCreateToken()
    {
        string path = Path.Combine(AppPaths.Folder, "settings-token.txt");
        try
        {
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: 32 } saved && saved.All(Uri.IsHexDigit)) return saved;
        }
        catch { }
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        try { Directory.CreateDirectory(AppPaths.Folder); File.WriteAllText(path, token); } catch { }
        return token;
    }

    object Catalog()
    {
        var titles = SensorHub.AliasNames.Select(k => new { key = k, title = SensorFormat.Title(k, null) });
        var batteries = _batteries.Devices().Select(d => new { source = d.Source, name = d.Name, shortName = BatteryHub.ShortName(d.Name), online = d.Online });
        var outputs = (_audio.State?.Outputs ?? []).Select(d => new { id = d.Id, name = d.Name, isDefault = d.IsDefault });
        return new { sensors = titles, batteries, outputs };
    }

    // ---------- plumbing ----------

    static async Task ServeStatic(HttpListenerResponse res, string name)
    {
        if (name.Contains("..") || name.Contains('/')) { await Send(res, 404, "text/plain", "Not found"); return; }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wwwroot/" + name);
        if (stream == null) { await Send(res, 404, "text/plain", "Not found"); return; }
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        string type = Path.GetExtension(name) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
        res.Headers["Cache-Control"] = "no-cache";
        await Send(res, 200, type, ms.ToArray());
    }

    static async Task<string> Body(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    static Task SendJson(HttpListenerResponse res, int status, object value) =>
        Send(res, status, "application/json", JsonSerializer.Serialize(value, AppConfig.WriteOptions));

    static Task Send(HttpListenerResponse res, int status, string type, string text) =>
        Send(res, status, type.Contains("charset") ? type : type + "; charset=utf-8", Encoding.UTF8.GetBytes(text));

    static async Task Send(HttpListenerResponse res, int status, string type, byte[] body)
    {
        res.StatusCode = status;
        res.ContentType = type;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
        res.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
