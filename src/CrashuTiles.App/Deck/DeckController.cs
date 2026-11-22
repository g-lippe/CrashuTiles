using System.Collections.Concurrent;
using System.Diagnostics;
using CrashuTiles.Actions;
using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Data.Batteries;
using CrashuTiles.Device;
using CrashuTiles.Tiles;
using static CrashuTiles.Device.D200Protocol;

namespace CrashuTiles.Deck;

/// <summary>
/// Owns what the dock shows and what its keys do. All work runs on one thread, fed by a queue:
/// config changes, button events from the device, and a 250 ms tick that redraws live tiles.
/// Rendered PNGs go to D200Device, which drops unchanged images and paces the uploads.
/// </summary>
sealed class DeckController : IDisposable
{
    const int Slots = KeyCount + 1; // 13 keys + the small screen
    const int HoldMs = 500;

    readonly D200Device _device;
    readonly SensorHub _sensors;
    readonly BatteryHub _batteries;
    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;
    readonly System.Threading.Timer _tick;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    AppConfig? _config;
    string _page = "";
    readonly Stack<string> _history = new();
    readonly Tile?[] _tiles = new Tile?[Slots];
    readonly KeyConfig?[] _keys = new KeyConfig?[Slots];
    readonly bool[] _pressed = new bool[Slots];
    readonly long[] _pressStart = new long[Slots];
    readonly long[] _lastDraw = new long[Slots];
    readonly byte[] _blank = Tile.Blank(KeySize, KeySize);

    public string CurrentPage => _page;

    public DeckController(D200Device device, SensorHub sensors, BatteryHub batteries)
    {
        _device = device;
        _sensors = sensors;
        _batteries = batteries;
        _device.Button += (i, down) => Post(() => OnButton(i, down));
        _thread = new Thread(Run) { IsBackground = true, Name = "Deck" };
        _thread.Start();
        _tick = new System.Threading.Timer(_ => Post(() => Draw(force: false)), null, 250, 250);
    }

    public void Apply(AppConfig config) => Post(() =>
    {
        _config = config;
        _device.SetBrightness(config.Brightness);
        string page = config.Pages.ContainsKey(_page) ? _page : config.StartPage;
        if (!config.Pages.ContainsKey(page)) page = config.Pages.Keys.First();
        _history.Clear();
        Load(page);
    });

    public void SetBrightness(int percent) => Post(() =>
    {
        if (_config != null) _config.Brightness = percent;
        _device.SetBrightness(percent);
    });

    void Post(Action a)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(a);
    }

    void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch (Exception e) { Log.Info("Deck error: " + e); }
        }
    }

    // ---------- pages ----------

    /// <summary>Built-in page listing every battery, one per key; opened by pressing a "batteries" tile.</summary>
    public const string BatteryPageName = "__batteries";

    public static Dictionary<string, KeyConfig> BatteryPage(BatteryHub hub)
    {
        var keys = new Dictionary<string, KeyConfig>
        {
            ["0"] = new() { Label = "Back", Icon = "glyph:E72B", Color = "#F5A623", Action = new ActionConfig { Type = "back" } },
            ["small"] = new() { Native = "clock-full" },
        };
        int k = 1;
        foreach (var d in hub.Devices().Take(KeyCount - 1))
            keys[(k++).ToString()] = new KeyConfig { Tile = "battery", Battery = d.Source, Match = d.Name, Label = BatteryHub.ShortName(d.Name) };
        return keys;
    }

    Dictionary<string, KeyConfig>? PageKeys(string page) =>
        page == BatteryPageName ? BatteryPage(_batteries)
        : _config != null && _config.Pages.TryGetValue(page, out var keys) ? keys : null;

    void Load(string page)
    {
        var keys = PageKeys(page);
        if (keys == null) { Log.Info($"No page '{page}'"); return; }
        _page = page;
        for (int i = 0; i < Slots; i++)
        {
            string id = i == SmallScreenIndex ? "small" : i.ToString();
            _keys[i] = keys.TryGetValue(id, out var k) ? k : null;
            _tiles[i] = null;
            _pressed[i] = false;
            if (_keys[i] is { } cfg && (i != SmallScreenIndex || cfg.Native == null))
            {
                try { _tiles[i] = Tile.Create(cfg); }
                catch (Exception e) { Log.Info($"Key {id} on '{page}': {e.Message}"); }
            }
        }

        Tile.AssignGroups(_tiles);

        // Small screen: our own image, or the firmware's clock/stats.
        var small = _keys[SmallScreenIndex];
        if (_tiles[SmallScreenIndex] == null)
            _device.SetSmallNative(NativeMode(small?.Native), twelveHour: false);

        Draw(force: true);
        Log.Info($"Page '{page}' loaded");
    }

    static SmallMode NativeMode(string? name) => (name ?? "clock").ToLowerInvariant() switch
    {
        "dial" => SmallMode.Dial,
        "stats" => SmallMode.Stats,
        "clock-date" => SmallMode.DigitalTimeDate,
        "clock-weekday" => SmallMode.DigitalTimeWeekday,
        "clock-full" => SmallMode.DigitalDateTimeWeekday,
        _ => SmallMode.DigitalTime,
    };

    void GoTo(string page)
    {
        if (PageKeys(page) == null) { Log.Info($"No page '{page}'"); return; }
        _history.Push(_page);
        Load(page);
    }

    void Back()
    {
        if (_history.Count > 0) Load(_history.Pop());
        else if (_config != null) Load(_config.StartPage);
    }

    // ---------- drawing ----------

    void Draw(bool force)
    {
        var (cpu, mem, gpu) = _sensors.NativeStats();
        _device.SetNativeStats(cpu, mem, gpu);
        long now = _clock.ElapsedMilliseconds;
        var ctxTime = DateTime.Now;
        for (int i = 0; i < Slots; i++)
        {
            var tile = _tiles[i];
            if (tile == null)
            {
                if (force && i < KeyCount) _device.SetKey(i, _blank);
                continue;
            }
            if (!force && (tile.RefreshMs == 0 || now - _lastDraw[i] < tile.RefreshMs)) continue;
            DrawSlot(i, ctxTime);
            _lastDraw[i] = now;
        }
    }

    void DrawSlot(int i, DateTime now)
    {
        var tile = _tiles[i];
        if (tile == null) return;
        var ctx = new TileContext(_sensors, _batteries, now, _pressed[i]);
        if (i == SmallScreenIndex) _device.SetSmallImage(tile.Render(SmallWidth, SmallHeight, ctx));
        else _device.SetKey(i, tile.Render(KeySize, KeySize, ctx));
    }

    // ---------- input ----------

    void OnButton(int i, bool down)
    {
        if (i < 0 || i >= Slots) return;
        var key = _keys[i];
        // A "batteries" tile without its own action opens the battery page.
        var tap = key?.Action ?? (_tiles[i] is MultiBatteryTile ? new ActionConfig { Type = "page", Page = BatteryPageName } : null);
        if (down)
        {
            _pressed[i] = true;
            _pressStart[i] = _clock.ElapsedMilliseconds;
            DrawSlot(i, DateTime.Now);
            if (key?.Hold == null && tap != null) Fire(tap); // no hold: act on press, it feels faster
        }
        else
        {
            if (!_pressed[i]) return; // release after a page change
            _pressed[i] = false;
            DrawSlot(i, DateTime.Now);
            if (key?.Hold != null)
            {
                bool held = _clock.ElapsedMilliseconds - _pressStart[i] >= HoldMs;
                var action = held ? key.Hold : tap;
                if (action != null) Fire(action);
            }
        }
    }

    void Fire(ActionConfig a)
    {
        switch (a.Type.ToLowerInvariant())
        {
            case "page": if (a.Page != null) GoTo(a.Page); break;
            case "back": Back(); break;
            default: ThreadPool.QueueUserWorkItem(_ => ActionRunner.Run(a)); break;
        }
    }

    public void Dispose()
    {
        _tick.Dispose();
        _queue.CompleteAdding();
        _thread.Join(1000);
    }
}
