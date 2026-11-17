using System.Diagnostics;
using static CrashuTiles.Device.D200Protocol;

namespace CrashuTiles.Device;

/// <summary>
/// High-level driver for one Ulanzi D200. You set the desired key images, small-screen
/// content and brightness at any time, from any thread. A worker thread connects (and
/// reconnects), batches every change into one ZIP per update, and paces the updates.
///
/// Pacing comes from measurements on real hardware (see CLAUDE.md). All 13 keys sent flat
/// out makes the dock skip frames and blank the last keys, while all 13 every 150 ms is clean.
/// So the gap after an update scales with how much it carried, and grows further whenever
/// the dock pushes back (a write that blocks).
/// </summary>
public sealed class D200Device : IDisposable
{
    /// <summary>Raised on a background thread. Index 0..12 = keys, 13 = small screen.</summary>
    public event Action<int, bool>? Button;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? Log;

    public bool IsConnected { get; private set; }
    public int UpdatesSent { get; private set; }
    public double LastBlockMs { get; private set; }

    readonly object _gate = new();
    readonly byte[]?[] _desired = new byte[KeyCount][];
    readonly byte[]?[] _shown = new byte[KeyCount][];
    byte[]? _smallDesired, _smallShown;
    SmallMode _smallMode = SmallMode.DigitalTime;
    bool _smallModeDirty = true;
    int _brightness = 80;
    bool _brightnessDirty = true;
    bool _needFull = true;
    int _cpu, _mem, _gpu;
    bool _twelveHour;

    readonly AutoResetEvent _wake = new(false);
    readonly CancellationTokenSource _cts = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Thread? _worker;
    volatile bool _lost;

    public void Start()
    {
        if (_worker != null) return;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "D200 worker" };
        _worker.Start();
    }

    // ---------- desired state (any thread) ----------

    /// <summary>Sets a key's image (196x196 PNG). Identical bytes are ignored.</summary>
    public void SetKey(int index, byte[] png)
    {
        if (index < 0 || index >= KeyCount) return;
        lock (_gate)
        {
            if (_desired[index] is { } cur && cur.AsSpan().SequenceEqual(png)) return;
            _desired[index] = png;
        }
        _wake.Set();
    }

    /// <summary>Shows our own 458x196 PNG on the small screen (switches it to Background mode).</summary>
    public void SetSmallImage(byte[] png)
    {
        lock (_gate)
        {
            if (_smallMode != SmallMode.Background) { _smallMode = SmallMode.Background; _smallModeDirty = true; }
            if (_smallDesired is { } cur && cur.AsSpan().SequenceEqual(png)) return;
            _smallDesired = png;
        }
        _wake.Set();
    }

    /// <summary>Lets the firmware draw the small screen itself (clock, dial or stats).</summary>
    public void SetSmallNative(SmallMode mode, bool twelveHour = false)
    {
        if (mode == SmallMode.Background) throw new ArgumentException("Use SetSmallImage for Background", nameof(mode));
        lock (_gate)
        {
            if (_smallMode == mode && _twelveHour == twelveHour) return;
            _smallMode = mode; _twelveHour = twelveHour; _smallModeDirty = true; _smallDesired = null;
        }
        _wake.Set();
    }

    /// <summary>Numbers shown by the firmware's own Stats mode.</summary>
    public void SetNativeStats(int cpu, int mem, int gpu)
    {
        lock (_gate) { _cpu = cpu; _mem = mem; _gpu = gpu; }
    }

    public void SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (_brightness == percent) return;
            _brightness = percent; _brightnessDirty = true;
        }
        _wake.Set();
    }

    // ---------- worker ----------

    void WorkerLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            string? path = FindDock();
            if (path == null) { ct.WaitHandle.WaitOne(2000); continue; }

            HidChannel? ch = null;
            try
            {
                ch = HidChannel.Open(path, PacketSize);
                _lost = false;
                lock (_gate)
                {
                    _needFull = true; _brightnessDirty = true; _smallModeDirty = true;
                    Array.Clear(_shown); _smallShown = null;
                }
                SetConnected(true);
                Log?.Invoke("Connected to the dock");
                _ = ReadLoopAsync(ch, ct);
                RunSession(ch, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Log?.Invoke("Dock connection error: " + e.Message);
            }
            finally
            {
                ch?.Dispose();
                if (IsConnected) { SetConnected(false); Log?.Invoke("Disconnected from the dock"); }
            }
            ct.WaitHandle.WaitOne(1000);
        }
    }

    static string? FindDock()
    {
        string id = $"vid_{VendorId:x4}&pid_{ProductId:x4}";
        foreach (var p in Native.HidInterfacePaths())
        {
            string lower = p.ToLowerInvariant();
            if (lower.Contains(id) && lower.Contains("&mi_00")) return p; // interface 0 = the deck protocol
        }
        return null;
    }

    async Task ReadLoopAsync(HidChannel ch, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                byte[] p = await ch.ReadAsync(ct).ConfigureAwait(false);
                if (ParseButton(p) is { } ev)
                {
                    try { Button?.Invoke(ev.Index, ev.Pressed); }
                    catch (Exception e) { Log?.Invoke("Button handler failed: " + e); }
                }
                else if (CommandOf(p) is { } cmd && cmd != Command.InAck)
                {
                    Log?.Invoke($"Dock sent command 0x{(ushort)cmd:x4}");
                }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke("Read stopped: " + e.Message);
        }
        finally
        {
            _lost = true;
            _wake.Set();
        }
    }

    void RunSession(HidChannel ch, CancellationToken ct)
    {
        long nextAllowed = 0, nextKeepAlive = 0;
        while (!ct.IsCancellationRequested && !_lost)
        {
            long now = _clock.ElapsedMilliseconds;

            int? brightness = null;
            bool full, smallChanged;
            var batch = new Dictionary<int, byte[]>();
            SmallMode mode;
            byte[]? smallImage;
            lock (_gate)
            {
                if (_brightnessDirty) { brightness = _brightness; _brightnessDirty = false; }
                full = _needFull;
                for (int i = 0; i < KeyCount; i++)
                    if (_desired[i] is { } img && (full || !ReferenceEquals(img, _shown[i]))) batch[i] = img;
                mode = EffectiveMode();
                smallImage = mode == SmallMode.Background ? _smallDesired : null;
                smallChanged = _smallModeDirty || !ReferenceEquals(_smallDesired, _smallShown);
            }

            if (brightness is int b)
                SendSimple(ch, Command.SetBrightness, System.Text.Encoding.ASCII.GetBytes(b.ToString()));

            if (full || batch.Count > 0 || smallChanged)
            {
                if (now < nextAllowed)
                {
                    _wake.WaitOne((int)(nextAllowed - now));
                    continue;
                }

                // In Background mode every update carries the small image, so the slot
                // never ends up without one.
                byte[] zip = ButtonZip.Build(batch, mode, smallImage);
                var cmd = full ? Command.SetButtons : Command.PartialUpdateButtons;
                double t0 = _clock.Elapsed.TotalMilliseconds;
                foreach (var p in Packets(cmd, zip)) ch.Write(p);
                double blocked = _clock.Elapsed.TotalMilliseconds - t0;
                LastBlockMs = blocked;
                UpdatesSent++;

                lock (_gate)
                {
                    foreach (var (i, img) in batch) _shown[i] = img;
                    _smallShown = _smallDesired;
                    if (full) _needFull = false;
                    if (_smallModeDirty) { _smallModeDirty = false; nextKeepAlive = 0; } // tell the firmware the mode now
                }

                int cost = batch.Count + (smallImage != null ? 2 : 0);
                double gap = 30 + 9 * cost + (blocked > 25 ? blocked : 0);
                if (full) gap = Math.Max(gap, 250);
                nextAllowed = _clock.ElapsedMilliseconds + (long)gap;
            }

            now = _clock.ElapsedMilliseconds;
            if (now >= nextKeepAlive)
            {
                int cpu, mem, gpu; bool twelve;
                lock (_gate) { cpu = _cpu; mem = _mem; gpu = _gpu; twelve = _twelveHour; mode = EffectiveMode(); }
                SendSimple(ch, Command.SetSmallWindowData, SmallWindowData(mode, cpu, mem, gpu, DateTime.Now, twelve));
                nextKeepAlive = now + (mode == SmallMode.Background ? 2000 : 1000);
            }

            long wait = Math.Max(1, nextKeepAlive - _clock.ElapsedMilliseconds);
            _wake.WaitOne((int)Math.Min(wait, 1000));
        }
    }

    // Background without an image yet falls back to the firmware clock.
    SmallMode EffectiveMode() =>
        _smallMode == SmallMode.Background && _smallDesired == null ? SmallMode.DigitalTime : _smallMode;

    static void SendSimple(HidChannel ch, Command cmd, byte[] payload) => ch.Write(Frame(cmd, payload.Length, payload));

    void SetConnected(bool value)
    {
        IsConnected = value;
        try { ConnectionChanged?.Invoke(value); } catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _wake.Set();
        _worker?.Join(2000);
        _cts.Dispose();
    }
}
