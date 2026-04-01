param(
    [switch]$Release,
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$configuration = if ($Release) { "Release" } else { "Debug" }
$shellPath = Join-Path $workspaceRoot "windows-shell\\MeetingRecorder.Windows\\bin\\$configuration\\net10.0-windows\\MeetingRecorder.Windows.exe"

. (Join-Path $PSScriptRoot "RecorderProcessTools.ps1")

function Test-RecorderBuildIsStale {
    param(
        [string]$Root,
        [string]$ExecutablePath
    )

    if (-not (Test-Path $ExecutablePath)) {
        return $true
    }

    $executableTime = (Get-Item $ExecutablePath).LastWriteTimeUtc
    $sourceRoots = @(
        (Join-Path $Root "windows-shell"),
        (Join-Path $Root "companion"),
        (Join-Path $Root "scripts")
    )

    foreach ($sourceRoot in $sourceRoots) {
        $newerSource = Get-ChildItem $sourceRoot -Recurse -File |
            Where-Object {
                $_.LastWriteTimeUtc -gt $executableTime -and
                $_.FullName -notmatch '\\(bin|obj|meetings-data|logs|\.tmp|tmp-tests|tmp)\\' -and
                $_.Extension -in @('.cs', '.csproj', '.json', '.ps1', '.py', '.config', '.xml')
            } |
            Select-Object -First 1

        if ($null -ne $newerSource) {
            return $true
        }
    }

    return $false
}

$runningProcess = Get-WorkspaceRecorderProcess -WorkspaceRoot $workspaceRoot
if ($null -ne $runningProcess -and -not $runningProcess.HasExited) {
    Write-Host "Meeting Recorder is already running. Bringing the existing dashboard to the front..."
    try {
        Add-Type -AssemblyName Microsoft.VisualBasic
        [Microsoft.VisualBasic.Interaction]::AppActivate($runningProcess.Id) | Out-Null
    } catch {
    }
    exit 0
}

. (Join-Path $PSScriptRoot "Import-DotEnv.ps1") -Root $workspaceRoot

if ($Rebuild -or (Test-RecorderBuildIsStale -Root $workspaceRoot -ExecutablePath $shellPath)) {
    Write-Host "Recorder build not found or rebuild requested. Building first..."
    & (Join-Path $PSScriptRoot "run_windows_recorder.ps1") @PSBoundParameters
    exit $LASTEXITCODE
}

Write-Host "Launching Meeting Recorder (fast path)..."
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
