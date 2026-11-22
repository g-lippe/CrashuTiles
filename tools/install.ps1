# Builds CrashuTiles in Release and installs it for the current user:
#   %LOCALAPPDATA%\Programs\CrashuTiles\CrashuTiles.exe  plus a Start Menu shortcut.
# Re-run it after pulling changes to update. Your config and data in %APPDATA%\CrashuTiles are untouched.
# Usage: powershell -ExecutionPolicy Bypass -File tools\install.ps1 [-NoStart]
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'Programs\CrashuTiles'

# The running app locks its files.
$running = Get-Process CrashuTiles -ErrorAction SilentlyContinue
if ($running) { Write-Host 'Stopping CrashuTiles...'; $running | Stop-Process -Force; Start-Sleep -Seconds 1 }

Write-Host "Publishing to $dest ..."
dotnet publish "$root\src\CrashuTiles.App\CrashuTiles.App.csproj" -c Release -o $dest --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$exe = Join-Path $dest 'CrashuTiles.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'CrashuTiles.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'Custom software for the Ulanzi D200'
$shortcut.Save()
Write-Host "Start Menu shortcut: $lnk"

if (-not $NoStart) {
    # Launch through Explorer so CrashuTiles doesn't belong to this console (or to whatever started it).
    Start-Process explorer.exe -ArgumentList "`"$exe`""
    Write-Host 'CrashuTiles started.'
}
