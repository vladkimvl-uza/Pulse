# Stops the running Pulse (even the elevated one, via its quit event), rebuilds, and starts it again
# through the logon task so it comes back elevated without a UAC prompt.
param([switch]$Dev)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
try {
    $ev = [System.Threading.EventWaitHandle]::OpenExisting('Local\Pulse.Quit.v1')
    $ev.Set() | Out-Null
    $ev.Dispose()
} catch { }
# Only the copy in .\app — test profiles run from .\app-test and must survive.
$mine = { Get-Process Pulse -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path -like "$root\app\*" } }
for ($i = 0; $i -lt 40 -and (& $mine); $i++) { Start-Sleep -Milliseconds 250 }
& $mine | Where-Object { $_.Path } | Stop-Process -Force -ErrorAction SilentlyContinue
& "$root\build.ps1"
if ($Dev) {
    # Non-elevated dev run (no CPU temperature) with the dev command channel.
    Start-Process "$root\app\Pulse.exe" -ArgumentList '--dev', '--no-task'
} else {
    schtasks /run /tn "Pulse System Monitor" | Out-Null
}
