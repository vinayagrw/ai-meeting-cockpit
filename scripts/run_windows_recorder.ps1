param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$configuration = if ($Release) { "Release" } else { "Debug" }

. (Join-Path $PSScriptRoot "Import-DotEnv.ps1") -Root $workspaceRoot
. (Join-Path $PSScriptRoot "RecorderProcessTools.ps1")

& (Join-Path $PSScriptRoot "build_windows_shell.ps1") @PSBoundParameters

$shellPath = Join-Path $workspaceRoot "windows-shell\\MeetingRecorder.Windows\\bin\\$configuration\\net10.0-windows\\MeetingRecorder.Windows.exe"
if (-not (Test-Path $shellPath)) {
    throw "Shell executable not found at $shellPath"
}

Write-Host "Launching Meeting Recorder shell..."
$process = Start-Process -FilePath $shellPath -WorkingDirectory $workspaceRoot -PassThru
Start-Sleep -Seconds 2

if ($process.HasExited) {
    $startupErrorPath = Join-Path (Split-Path $shellPath -Parent) "startup-error.log"
    if (Test-Path $startupErrorPath) {
        Write-Error ("Meeting Recorder exited during startup.`n`n" + (Get-Content $startupErrorPath -Raw))
    }

    throw "Meeting Recorder exited during startup before the dashboard became visible."
}

try {
    Add-Type -AssemblyName Microsoft.VisualBasic
    [Microsoft.VisualBasic.Interaction]::AppActivate($process.Id) | Out-Null
} catch {
}

Write-Host "Meeting Recorder launched. If the dashboard is behind other windows, use Alt+Tab and select 'Meeting Recorder'."
