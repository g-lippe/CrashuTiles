using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrashuTiles.Device;

/// <summary>
/// Raw read/write access to one HID interface. Reports are fixed size: one report-ID byte
/// (always 0 here) followed by the payload. Reads run on their own handle (overlapped, so
/// they can be cancelled); writes use a separate synchronous handle so a pending read never
/// blocks a write. A write blocks while the dock is still busy: that's the backpressure
/// the scheduler measures.
/// </summary>
internal sealed class HidChannel : IDisposable
{
    readonly FileStream _read;
    readonly FileStream _write;
    readonly int _reportSize;

    public string Path { get; }

    HidChannel(string path, FileStream read, FileStream write, int reportSize)
    {
        Path = path; _read = read; _write = write; _reportSize = reportSize;
    }

    public static HidChannel Open(string path, int payloadSize)
    {
        uint share = Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE;
        var hr = Native.CreateFile(path, Native.GENERIC_READ, share, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (hr.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Opening the dock for reading failed");
        var hw = Native.CreateFile(path, Native.GENERIC_WRITE, share, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (hw.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            hr.Dispose();
            throw new Win32Exception(err, "Opening the dock for writing failed");
        }
        var read = new FileStream(hr, FileAccess.Read, bufferSize: 0, isAsync: true);
        var write = new FileStream(hw, FileAccess.Write, bufferSize: 0, isAsync: false);
        return new HidChannel(path, read, write, payloadSize + 1);
    }

    /// <summary>Reads one report and returns its payload (report-ID byte stripped).</summary>
    public async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        var buf = new byte[_reportSize];
        int n = await _read.ReadAsync(buf, ct).ConfigureAwait(false);
        if (n <= 0) throw new IOException("The dock closed the connection");
        return buf.AsSpan(1, n - 1).ToArray();
    }

    /// <summary>Writes one payload (exactly payloadSize bytes); blocks while the dock is busy.</summary>
    public void Write(ReadOnlySpan<byte> payload)
    {
        var buf = new byte[_reportSize];
        payload.CopyTo(buf.AsSpan(1));
        _write.Write(buf, 0, buf.Length);
    }

    public void Dispose()
    {
        try { _read.Dispose(); } catch { }
        try { _write.Dispose(); } catch { }
    }
}
