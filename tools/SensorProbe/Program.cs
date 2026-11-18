// Dumps every sensor LibreHardwareMonitor can see, so we know what's readable
// with the current privileges and drivers. Usage: dotnet run --project tools/SensorProbe
using System.Diagnostics;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"Elevated: {admin}");

var computer = new Computer
{
    IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true,
    IsStorageEnabled = true, IsNetworkEnabled = true, IsControllerEnabled = true, IsBatteryEnabled = true,
    IsPsuEnabled = true,
};

var sw = Stopwatch.StartNew();
computer.Open();
Console.WriteLine($"Open: {sw.ElapsedMilliseconds} ms");

void UpdateAll(IHardware h) { h.Update(); foreach (var s in h.SubHardware) UpdateAll(s); }
foreach (var h in computer.Hardware) UpdateAll(h);
Thread.Sleep(1000);
sw.Restart();
foreach (var h in computer.Hardware) UpdateAll(h);
Console.WriteLine($"Update (all): {sw.ElapsedMilliseconds} ms");

int total = 0, withValue = 0;
void Dump(IHardware h, string indent)
{
    Console.WriteLine($"{indent}[{h.HardwareType}] {h.Name}");
    foreach (var s in h.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
    {
        total++;
        if (s.Value.HasValue) withValue++;
        string v = s.Value.HasValue ? s.Value.Value.ToString("0.##") : "-";
        Console.WriteLine($"{indent}    {s.SensorType,-12} {s.Name,-34} {v}");
    }
    foreach (var sub in h.SubHardware) Dump(sub, indent + "    ");
}
foreach (var h in computer.Hardware) Dump(h, "");
Console.WriteLine($"Sensors: {total}, with a value: {withValue}");

// The CPU stats this probe cares about most:
var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
var temps = cpu?.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
Console.WriteLine($"CPU temperature sensors: {temps?.Count ?? 0}, with values: {temps?.Count(s => s.Value.HasValue) ?? 0}");
computer.Close();
