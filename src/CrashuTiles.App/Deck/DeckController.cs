using System.Collections.Concurrent;
using System.Diagnostics;
using CrashuTiles.Actions;
using CrashuTiles.Config;
using CrashuTiles.Data;
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
    readonly SystemStats _stats;
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

    public DeckController(D200Device device, SystemStats stats)
    {
        _device = device;
        _stats = stats;
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

    void Load(string page)
    {
        if (_config == null || !_config.Pages.TryGetValue(page, out var keys)) { Log.Info($"No page '{page}'"); return; }
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
        if (_config == null || !_config.Pages.ContainsKey(page)) { Log.Info($"No page '{page}'"); return; }
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
        _device.SetNativeStats(_stats.Cpu, _stats.Ram, 0);
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
        var ctx = new TileContext(_stats, now, _pressed[i]);
        if (i == SmallScreenIndex) _device.SetSmallImage(tile.Render(SmallWidth, SmallHeight, ctx));
        else _device.SetKey(i, tile.Render(KeySize, KeySize, ctx));
    }

    // ---------- input ----------

    void OnButton(int i, bool down)
    {
        if (i < 0 || i >= Slots) return;
        var key = _keys[i];
        if (down)
        {
            _pressed[i] = true;
            _pressStart[i] = _clock.ElapsedMilliseconds;
            DrawSlot(i, DateTime.Now);
            if (key?.Hold == null && key?.Action != null) Fire(key.Action); // no hold: act on press, it feels faster
        }
        else
        {
            if (!_pressed[i]) return; // release after a page change
            _pressed[i] = false;
            DrawSlot(i, DateTime.Now);
            if (key?.Hold != null)
            {
                bool held = _clock.ElapsedMilliseconds - _pressStart[i] >= HoldMs;
                var action = held ? key.Hold : key.Action;
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
