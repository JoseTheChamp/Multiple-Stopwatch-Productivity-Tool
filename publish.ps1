# Builds Stopwatch into .\dist and (re)creates the "Stopwatch" shortcut on the desktop.
# Usage:  powershell -ExecutionPolicy Bypass -File publish.ps1

$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
$exe = Join-Path $dist 'Stopwatch.exe'

if (Get-Process -Name Stopwatch -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
    throw 'Stopwatch is running from dist - close it first (your timers keep counting while it is closed).'
}

dotnet publish (Join-Path $PSScriptRoot 'Stopwatch.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None -o $dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Stopwatch.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dist
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Multi-timer stopwatch'
$shortcut.Save()

"Published:  $exe"
"Shortcut:   $shortcutPath"
