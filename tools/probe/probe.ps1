param([int]$Listen = 30)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.IO.Compression
Add-Type -TypeDefinition (Get-Content -Raw "$PSScriptRoot\D200Probe.cs") -ReferencedAssemblies System.Drawing, System.IO.Compression
$dev = Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^HID\\VID_2207&PID_0019&MI_00' } | Select-Object -First 1
if (-not $dev) { throw 'D200 interface 0 not found' }
$path = '\\?\' + ($dev.InstanceId -replace '\\', '#') + '#{4d1e55b2-f16f-11cf-88cb-001111000030}'
if ($Listen -eq -2) { [D200Probe]::Bench2($path) } elseif ($Listen -lt 0) { [D200Probe]::Bench($path) } else { [D200Probe]::Run($path, $Listen) }
