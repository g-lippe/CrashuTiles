using System.Runtime.InteropServices;

namespace CrashuTiles.Data;

sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Default output (volume, mute, name), default microphone mute, and the list of outputs.</summary>
sealed record AudioState(AudioDevice? Output, int Volume, bool Muted, AudioDevice? Mic, bool MicMuted, IReadOnlyList<AudioDevice> Outputs);

/// <summary>
/// Speakers, headsets and microphones through Windows Core Audio. Polled twice a second (cheap)
/// so tiles follow changes made elsewhere: the keyboard's volume wheel, the tray flyout, other apps.
/// </summary>
sealed class AudioHub : IDisposable
{
    const uint DeviceStateActive = 1, ClsCtxAll = 23;
    static Guid AudioEndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;
    static PROPERTYKEY FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    static Guid EventContext = Guid.Empty;

    readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
    readonly System.Threading.Timer? _timer;
    readonly object _gate = new();
    AudioState? _state;
    IReadOnlyList<AudioDevice> _outputs = [];
    long _lastListAt;

    public AudioHub(bool autoPoll = true)
    {
        if (autoPoll) _timer = new System.Threading.Timer(_ => Poll(), null, 0, 500);
    }

    public AudioState? State { get { lock (_gate) return _state; } }

    public void Poll()
    {
        try
        {
            lock (_gate)
            {
                var output = Default(EDataFlow.Render);
                var mic = Default(EDataFlow.Capture);
                int volume = 0; bool muted = false, micMuted = false;
                if (output != null)
                {
                    var v = Volume(output.Id);
                    v.GetMasterVolumeLevelScalar(out float level);
                    v.GetMute(out muted);
                    volume = (int)Math.Round(level * 100);
                }
                if (mic != null) Volume(mic.Id).GetMute(out micMuted);

                // The device list changes rarely: refresh it every 5 s (or when the default changed).
                if (Environment.TickCount64 - _lastListAt > 5000 || _state?.Output?.Id != output?.Id)
                {
                    _outputs = List(EDataFlow.Render, output?.Id);
                    _lastListAt = Environment.TickCount64;
                }
                _state = new AudioState(output, volume, muted, mic, micMuted, _outputs);
            }
        }
        catch (Exception e) { Log.Info("Audio poll failed: " + e.Message); }
    }

    AudioDevice? Default(EDataFlow flow)
    {
        try
        {
            _enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var dev);
            dev.GetId(out string id);
            return new AudioDevice(id, Name(dev), true);
        }
        catch (COMException) { return null; } // no device of that kind
    }

    List<AudioDevice> List(EDataFlow flow, string? defaultId)
    {
        var list = new List<AudioDevice>();
        _enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out var coll);
        coll.GetCount(out uint n);
        for (uint i = 0; i < n; i++)
        {
            coll.Item(i, out var dev);
            dev.GetId(out string id);
            list.Add(new AudioDevice(id, Name(dev), id == defaultId));
        }
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Name(IMMDevice dev)
    {
        dev.OpenPropertyStore(0, out var store); // STGM_READ
        store.GetValue(ref FriendlyName, out var pv);
        try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.ptr) ?? "" : ""; } // VT_LPWSTR
        finally { CoreAudioNative.PropVariantClear(ref pv); }
    }

    IAudioEndpointVolume Volume(string id)
    {
        _enumerator.GetDevice(id, out var dev);
        dev.Activate(ref AudioEndpointVolumeIid, ClsCtxAll, IntPtr.Zero, out object o);
        return (IAudioEndpointVolume)o;
    }

    // ---------- commands (any thread) ----------

    public void ToggleMute() => Change(EDataFlow.Render, v => { v.GetMute(out bool m); v.SetMute(!m, ref EventContext); });

    public void ToggleMicMute() => Change(EDataFlow.Capture, v => { v.GetMute(out bool m); v.SetMute(!m, ref EventContext); });

    /// <summary>Moves the output volume by delta percentage points (and unmutes when going up).</summary>
    public void Nudge(int delta) => Change(EDataFlow.Render, v =>
    {
        v.GetMasterVolumeLevelScalar(out float level);
        v.SetMasterVolumeLevelScalar(Math.Clamp(level + delta / 100f, 0f, 1f), ref EventContext);
        if (delta > 0) v.SetMute(false, ref EventContext);
    });

    /// <summary>Makes a device the default output for apps and for calls, like picking it in Sound settings.</summary>
    public void SetDefaultOutput(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClientCom();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                int hr = policy.SetDefaultEndpoint(id, role);
                if (hr != 0) Log.Info($"SetDefaultEndpoint({role}) returned 0x{hr:X8}");
            }
        }
        catch (Exception e) { Log.Info("Switching the output failed: " + e.Message); }
        Poll();
    }

    void Change(EDataFlow flow, Action<IAudioEndpointVolume> change)
    {
        try
        {
            lock (_gate)
            {
                var dev = Default(flow);
                if (dev == null) return;
                change(Volume(dev.Id));
            }
        }
        catch (Exception e) { Log.Info("Audio change failed: " + e.Message); }
        Poll();
    }

    public void Dispose() => _timer?.Dispose();
}
