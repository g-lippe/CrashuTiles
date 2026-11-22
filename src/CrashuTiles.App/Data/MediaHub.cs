using Windows.Media.Control;
using Windows.Storage.Streams;

namespace CrashuTiles.Data;

/// <summary>What's playing right now. Art may be null (not every app provides it).</summary>
sealed record MediaState(
    string App, string Title, string Artist, string Album, bool Playing,
    bool CanPlayPause, bool CanNext, bool CanPrevious,
    TimeSpan Position, TimeSpan Duration, DateTime PositionAt, Bitmap? Art)
{
    /// <summary>Position now, moved forward since the app last reported it while playing.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var p = Playing ? Position + (DateTime.Now - PositionAt) : Position;
            return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
        }
    }
}

/// <summary>
/// The "now playing" session Windows shows in its media flyout (Spotify, browsers, players...),
/// via GlobalSystemMediaTransportControlsSessionManager. Polled once a second on a timer thread;
/// commands go to that same session, so they hit the app that's actually playing.
/// </summary>
sealed class MediaHub : IDisposable
{
    readonly System.Threading.Timer? _timer;
    readonly object _gate = new();
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    MediaState? _state;
    string _artKey = "";
    Bitmap? _art;
    int _artTries;
    int _polling;

    public MediaHub(bool autoPoll = true)
    {
        if (autoPoll) _timer = new System.Threading.Timer(_ => _ = PollAsync(), null, 0, 1000);
    }

    public MediaState? State { get { lock (_gate) return _state; } }

    public async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return; // previous poll still running
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession();
            MediaState? state = null;
            if (session != null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                string app = AppName(session.SourceAppUserModelId);
                string key = $"{app}|{props?.Title}|{props?.Artist}|{props?.AlbumTitle}";
                if (key != _artKey) { _artKey = key; _art = null; _artTries = 0; }
                // Some apps attach the thumbnail a moment after the title: retry a few times.
                if (_art == null && _artTries < 5 && props?.Thumbnail != null)
                {
                    _artTries++;
                    _art = await LoadArt(props.Thumbnail);
                }
                var c = playback.Controls;
                state = new MediaState(
                    app, props?.Title ?? "", props?.Artist ?? "", props?.AlbumTitle ?? "",
                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled, c.IsNextEnabled, c.IsPreviousEnabled,
                    timeline.Position, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime.LocalDateTime, _art);
            }
            lock (_gate) { _session = session; _state = state; }
        }
        catch (Exception e)
        {
            Log.Info("Media poll failed: " + e.Message);
        }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    static async Task<Bitmap?> LoadArt(IRandomAccessStreamReference thumb)
    {
        try
        {
            using var stream = await thumb.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 20_000_000) return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return new Bitmap(img); // detached from the stream
        }
        catch (Exception e)
        {
            Log.Info("Album art unreadable: " + e.Message);
            return null;
        }
    }

    /// <summary>"Spotify.exe" -> "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" -> "ZuneMusic".</summary>
    static string AppName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        string s = id.Split('!').Last();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        s = s.Split('.').Last();
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    /// <summary>playpause, play, pause, next or prev, sent to the current session.</summary>
    public void Command(string? command)
    {
        GlobalSystemMediaTransportControlsSession? s;
        lock (_gate) s = _session;
        if (s == null) { Log.Info("Media command with nothing playing"); return; }
        _ = Run();

        async Task Run()
        {
            try
            {
                bool ok = (command ?? "playpause").ToLowerInvariant() switch
                {
                    "play" => await s.TryPlayAsync(),
                    "pause" => await s.TryPauseAsync(),
                    "next" => await s.TrySkipNextAsync(),
                    "prev" or "previous" => await s.TrySkipPreviousAsync(),
                    _ => await s.TryTogglePlayPauseAsync(),
                };
                if (!ok) Log.Info($"Media command '{command}' was refused by the app");
                await Task.Delay(150);
                await PollAsync(); // show the new state right away
            }
            catch (Exception e) { Log.Info($"Media command '{command}' failed: {e.Message}"); }
        }
    }

    public void Dispose() => _timer?.Dispose();
}
