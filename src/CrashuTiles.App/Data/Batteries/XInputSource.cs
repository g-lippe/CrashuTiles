using System.Runtime.InteropServices;

namespace CrashuTiles.Data.Batteries;

/// <summary>
/// Xbox-compatible controllers through XInput. XInput only gives four steps (empty, low, medium,
/// full), shown as 5 / 30 / 65 / 100 %. Controllers that report "wired" (including 8BitDo
/// dongles) have no readable battery and are skipped.
/// </summary>
sealed class XInputSource : IBatterySource
{
    public string Kind => "xinput";

    public IReadOnlyList<BatteryReading> Poll()
    {
        var result = new List<BatteryReading>();
        for (int user = 0; user < 4; user++)
        {
            if (XInputGetCapabilities(user, 0, out _) != 0) continue;
            if (XInputGetBatteryInformation(user, 0, out var b) != 0) continue;
            if (b.Type is not (2 or 3)) continue; // 2 alkaline, 3 NiMH; skip wired / unknown
            int percent = b.Level switch { 0 => 5, 1 => 30, 2 => 65, _ => 100 };
            result.Add(new BatteryReading(Kind, $"Controller {user + 1}", percent, false, true, DateTime.Now));
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BATTERY { public byte Type, Level; }

    [StructLayout(LayoutKind.Sequential)]
    struct CAPS { public byte Type, SubType; public ushort Flags; public ushort Buttons, LT, RT; public short LX, LY, RX, RY; public ushort LMotor, RMotor; }

    [DllImport("xinput1_4.dll")] static extern int XInputGetBatteryInformation(int user, byte devType, out BATTERY b);
    [DllImport("xinput1_4.dll")] static extern int XInputGetCapabilities(int user, int flags, out CAPS c);

    public void Dispose() { }
}
