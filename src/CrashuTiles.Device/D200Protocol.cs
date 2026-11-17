using System.Buffers.Binary;
using System.Text;

namespace CrashuTiles.Device;

/// <summary>
/// Wire format of the Ulanzi D200, based on redphx/strmdck and
/// jcalado/companion-surface-d200 (both MIT) and checked on real hardware.
///
/// Every packet is 1024 bytes. The first packet of a message is
///   [7C 7C] [command: u16 big-endian] [total length: u32 little-endian] [data, up to 1016 bytes]
/// and a longer payload (a ZIP of images) continues in raw 1024-byte packets with no header.
/// </summary>
public static class D200Protocol
{
    public const ushort VendorId = 0x2207;
    public const ushort ProductId = 0x0019;
    public const int PacketSize = 1024;
    public const int HeaderSize = 8;
    public const int FirstChunkData = PacketSize - HeaderSize; // 1016

    public const int KeyCount = 13;          // physical LCD keys, index = row * 5 + col
    public const int SmallScreenIndex = 13;  // the wide screen is also pressable and reports as 13
    public const int Columns = 5;
    public const int KeySize = 196;          // key images are 196 x 196
    public const int SmallWidth = 458, SmallHeight = 196;

    public enum Command : ushort
    {
        SetButtons = 0x0001,          // replace the whole layout (ZIP)
        SetSmallWindowData = 0x0006,  // "mode|cpu|mem|HH:mm:ss|gpu|24H|suffix", also the keep-alive
        SetBrightness = 0x000a,       // ASCII "0".."100"
        SetLabelStyle = 0x000b,
        PartialUpdateButtons = 0x000d,// update only the slots in the ZIP
        LockScreen = 0x000f,
        UnlockScreen = 0x0010,

        InButton = 0x0101,
        InAck = 0x010b,               // documented by others; never seen on our test unit
        InDeviceInfo = 0x0303,
    }

    /// <summary>Modes of the small screen. Every mode except Background is drawn by the firmware itself.</summary>
    public enum SmallMode
    {
        Stats = 0,       // CPU / RAM / GPU bars fed by the PC
        Dial = 1,        // analog clock
        Background = 2,  // our own 458x196 image
        DigitalDateTimeWeekday = 200,
        DigitalTimeWeekday = 201,
        DigitalTimeDate = 202,
        DigitalTime = 203,
    }

    public static byte[] Frame(Command cmd, int totalLength, ReadOnlySpan<byte> firstData)
    {
        if (firstData.Length > FirstChunkData) throw new ArgumentException("First chunk too large", nameof(firstData));
        var p = new byte[PacketSize];
        p[0] = 0x7c; p[1] = 0x7c;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), (ushort)cmd);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), totalLength);
        firstData.CopyTo(p.AsSpan(HeaderSize));
        return p;
    }

    /// <summary>Splits a payload into the packets that carry it.</summary>
    public static List<byte[]> Packets(Command cmd, byte[] payload)
    {
        var list = new List<byte[]> { Frame(cmd, payload.Length, payload.AsSpan(0, Math.Min(FirstChunkData, payload.Length))) };
        for (int off = FirstChunkData; off < payload.Length; off += PacketSize)
        {
            var p = new byte[PacketSize];
            payload.AsSpan(off, Math.Min(PacketSize, payload.Length - off)).CopyTo(p);
            list.Add(p);
        }
        return list;
    }

    /// <summary>
    /// Firmware bug: the first byte of every raw continuation packet (payload offset
    /// 1016 + 1024*N) must not be 0x00 or 0x7C, or the upload is corrupted.
    /// </summary>
    public static bool IsPayloadSafe(ReadOnlySpan<byte> payload)
    {
        for (int i = FirstChunkData; i < payload.Length; i += PacketSize)
            if (payload[i] == 0x00 || payload[i] == 0x7c) return false;
        return true;
    }

    public static byte[] SmallWindowData(SmallMode mode, int cpu, int mem, int gpu, DateTime now, bool twelveHour)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string date = now.ToString("yyyy/MM/dd", inv), weekday = now.ToString("ddd", inv);
        string suffix = mode switch
        {
            SmallMode.DigitalDateTimeWeekday => date + " " + weekday,
            SmallMode.DigitalTimeWeekday => weekday,
            SmallMode.DigitalTimeDate => date,
            _ => "",
        };
        string s = $"{(int)mode}|{cpu}|{mem}|{now.ToString("HH:mm:ss", inv)}|{gpu}|{(twelveHour ? "12H" : "24H")}|{suffix}";
        return Encoding.UTF8.GetBytes(s);
    }

    public readonly record struct ButtonEvent(int Index, bool Pressed, int State);

    /// <summary>Parses one incoming packet. Returns null for anything that isn't a button event.</summary>
    public static ButtonEvent? ParseButton(ReadOnlySpan<byte> p)
    {
        if (p.Length < HeaderSize + 4 || p[0] != 0x7c || p[1] != 0x7c) return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(p[2..]) != (ushort)Command.InButton) return null;
        // data: [state] [index] [type: 1 = key] [action: 1 = press, 0 = release]
        return new ButtonEvent(p[HeaderSize + 1], p[HeaderSize + 3] == 1, p[HeaderSize]);
    }

    public static Command? CommandOf(ReadOnlySpan<byte> p) =>
        p.Length >= 4 && p[0] == 0x7c && p[1] == 0x7c ? (Command)BinaryPrimitives.ReadUInt16BigEndian(p[2..]) : null;
}
