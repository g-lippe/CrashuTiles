using System.Diagnostics;
using CrashuTiles.Actions;
using CrashuTiles.Config;
using CrashuTiles.Data;
using CrashuTiles.Data.Batteries;
using CrashuTiles.Deck;
using CrashuTiles.Device;

namespace CrashuTiles;

/// <summary>The tray icon and its menu. Owns the device, the sensor hub and the deck.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _status, _closeStudio;
    readonly D200Device _device = new();
    readonly SensorHub _sensors = new();
    readonly BatteryHub _batteries = new();
    readonly DeckController _deck;
    readonly FileSystemWatcher _watcher;
    readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _studioCheck = new() { Interval = 5000 };
    readonly SynchronizationContext _ui;
    bool _studioWarned;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _status = new ToolStripMenuItem("Looking for the dock...") { Enabled = false };
        _closeStudio = new ToolStripMenuItem("Close Ulanzi Studio (it fights for the dock)", null, (_, _) => CloseStudio()) { Visible = false };
        var brightness = new ToolStripMenuItem("Brightness");
        foreach (int level in new[] { 10, 25, 50, 75, 100 })
            brightness.DropDownItems.Add($"{level}%", null, (_, _) => _deck!.SetBrightness(level));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(_closeStudio);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(brightness);
        menu.Items.Add("Edit tiles (config.json)", null, (_, _) => ActionRunner.OpenConfig());
        menu.Items.Add("Reload config", null, (_, _) => LoadConfig());
        menu.Items.Add("Open data folder", null, (_, _) => Process.Start("explorer.exe", $"\"{AppPaths.Folder}\""));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Icon = TrayIcon.Create(), Text = "CrashuTiles", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ActionRunner.OpenConfig();

        _device.Log += Log.Info;
        _device.ConnectionChanged += connected => _ui.Post(_ => SetStatus(connected), null);
        _deck = new DeckController(_device, _sensors, _batteries);

        LoadConfig();
        _device.Start();

        _watcher = new FileSystemWatcher(AppPaths.Folder, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += (_, _) => _ui.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
        _watcher.Created += (_, _) => _ui.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
        _watcher.Renamed += (_, _) => _ui.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
        _watcher.EnableRaisingEvents = true;
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); LoadConfig(); };

        _studioCheck.Tick += (_, _) => CheckStudio();
        _studioCheck.Start();
        CheckStudio();
        Log.Info("CrashuTiles started");
    }

    void LoadConfig()
    {
        try
        {
            var cfg = AppConfig.LoadOrCreate(AppPaths.Config);
            _deck.Apply(cfg);
            Log.Info("Config loaded");
        }
        catch (Exception e)
        {
            // Keep showing the last good config; say what's wrong.
            Log.Info("Config error: " + e.Message);
            _tray.ShowBalloonTip(8000, "CrashuTiles: config not loaded", e.Message, ToolTipIcon.Warning);
        }
    }

    void SetStatus(bool connected)
    {
        _status.Text = connected ? "Dock connected" : "Dock not connected";
        _tray.Text = connected ? "CrashuTiles: dock connected" : "CrashuTiles: dock not connected";
    }

    void CheckStudio()
    {
        bool running = Process.GetProcessesByName("UlanziDeck").Length > 0;
        _closeStudio.Visible = running;
        if (running && !_studioWarned)
        {
            _studioWarned = true;
            _tray.ShowBalloonTip(8000, "Ulanzi Studio is running",
                "It talks to the dock too, so presses may fire twice. Close it from the CrashuTiles menu.", ToolTipIcon.Warning);
        }
        if (!running) _studioWarned = false;
    }

    void CloseStudio()
    {
        foreach (var p in Process.GetProcessesByName("UlanziDeck"))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception e) { Log.Info("Closing Ulanzi Studio failed: " + e.Message); }
        }
        CheckStudio();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _watcher.Dispose();
        _studioCheck.Dispose();
        _deck.Dispose();
        _device.Dispose();
        _sensors.Dispose();
        _batteries.Dispose();
        _tray.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>Draws the tray icon: a little 3x3 grid of coloured keys.</summary>
static class TrayIcon
{
    public static Icon Create()
    {
        Color[] colors = [Color.FromArgb(63, 169, 245), Color.FromArgb(181, 124, 255), Color.FromArgb(245, 166, 35)];
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    using var b = new SolidBrush(colors[(row + col) % 3]);
                    g.FillRectangle(b, 1 + col * 10, 1 + row * 10, 9, 9);
                }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
