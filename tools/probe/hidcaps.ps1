Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
public static class H {
  [StructLayout(LayoutKind.Sequential)] public struct CAPS {
    public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] R;
    public ushort NLinkColl, NInBtn, NInVal, NInData, NOutBtn, NOutVal, NOutData, NFBtn, NFVal, NFData;
  }
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  public static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, out CAPS c);
  [DllImport("hid.dll", CharSet=CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder b, int l);
  [DllImport("hid.dll", CharSet=CharSet.Unicode)] public static extern bool HidD_GetManufacturerString(SafeFileHandle h, StringBuilder b, int l);
  [DllImport("hid.dll", CharSet=CharSet.Unicode)] public static extern bool HidD_GetSerialNumberString(SafeFileHandle h, StringBuilder b, int l);
  public static string Info(string path) {
    var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (h.IsInvalid) return "open failed " + Marshal.GetLastWin32Error();
    var sb = new StringBuilder(256); var r = new StringBuilder();
    if (HidD_GetManufacturerString(h, sb, 512)) r.Append("mfr=" + sb + " "); sb.Clear();
    if (HidD_GetProductString(h, sb, 512)) r.Append("product=" + sb + " "); sb.Clear();
    if (HidD_GetSerialNumberString(h, sb, 512)) r.Append("serial=" + sb + " ");
    IntPtr p; if (HidD_GetPreparsedData(h, out p)) { CAPS c; HidP_GetCaps(p, out c);
      r.AppendFormat("UsagePage=0x{0:X4} Usage=0x{1:X4} InLen={2} OutLen={3} FeatLen={4}", c.UsagePage, c.Usage, c.InLen, c.OutLen, c.FeatLen);
      HidD_FreePreparsedData(p); }
    h.Close(); return r.ToString();
  }
}
"@
$guid = '{4d1e55b2-f16f-11cf-88cb-001111000030}'
Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^HID\\VID_2207&PID_0019' } | ForEach-Object {
  $path = '\\?\' + ($_.InstanceId -replace '\\','#') + '#' + $guid
  "$($_.FriendlyName)`n  $([H]::Info($path))"
}
