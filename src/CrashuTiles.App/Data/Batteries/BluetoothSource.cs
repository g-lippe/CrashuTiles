using System.Runtime.InteropServices;

namespace CrashuTiles.Data.Batteries;

/// <summary>
/// Battery levels Windows already knows for connected Bluetooth devices (the same number the
/// Settings app shows), read from the DEVPKEY_Bluetooth_Battery device property.
/// Only present (connected) devices are listed.
/// </summary>
sealed class BluetoothSource : IBatterySource
{
    public string Kind => "bluetooth";

    static DEVPROPKEY BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    static DEVPROPKEY FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public IReadOnlyList<BatteryReading> Poll()
    {
        var byName = new Dictionary<string, BatteryReading>(StringComparer.OrdinalIgnoreCase);
        foreach (string enumerator in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM" })
        {
            foreach (string id in DeviceIds(enumerator))
            {
                if (CM_Locate_DevNodeW(out uint node, id, 0) != 0) continue;
                byte[]? battery = Property(node, ref BatteryKey);
                if (battery is not { Length: >= 1 }) continue;
                string name = PropertyString(node, ref FriendlyNameKey) ?? id;
                // One device has several nodes (one per service); keep one entry per name.
                byName[name] = new BatteryReading(Kind, name, battery[0], false, true, DateTime.Now);
            }
        }
        return byName.Values.ToList();
    }

    /// <summary>Product ids of Apple devices currently connected over Bluetooth (from "VID&amp;0001004C_PID&amp;xxxx").</summary>
    public static HashSet<int> ConnectedApplePids()
    {
        var pids = new HashSet<int>();
        foreach (string id in DeviceIds("BTHENUM"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(id, @"VID&0001004C_PID&([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) pids.Add(Convert.ToInt32(m.Groups[1].Value, 16));
        }
        return pids;
    }

    static List<string> DeviceIds(string enumerator)
    {
        const uint Flags = 0x1 | 0x100; // CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT
        if (CM_Get_Device_ID_List_SizeW(out uint len, enumerator, Flags) != 0 || len == 0) return [];
        var buf = new char[len];
        if (CM_Get_Device_ID_ListW(enumerator, buf, len, Flags) != 0) return [];
        return new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    static byte[]? Property(uint node, ref DEVPROPKEY key)
    {
        uint size = 0;
        CM_Get_DevNode_PropertyW(node, ref key, out _, null, ref size, 0);
        if (size == 0) return null;
        var buf = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out _, buf, ref size, 0) == 0 ? buf : null;
    }

    static string? PropertyString(uint node, ref DEVPROPKEY key) =>
        Property(node, ref key) is { } b ? System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0') : null;

    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY(Guid fmtid, uint pid) { public Guid Fmtid = fmtid; public uint Pid = pid; }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, uint len, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_PropertyW(uint node, ref DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);

    public void Dispose() { }
}
