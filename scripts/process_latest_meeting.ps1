param(
    [string]$SessionDir
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "Import-DotEnv.ps1") -Root $workspaceRoot

function Resolve-PythonExecutable {
    param([string]$Root)

    $explicitOverride = $env:MEETING_RECORDER_PYTHON
    if (-not [string]::IsNullOrWhiteSpace($explicitOverride)) {
        return $explicitOverride
    }

    $candidates = @(
        (Join-Path $Root ".venv\Scripts\python.exe"),
        (Join-Path $Root "venv\Scripts\python.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return "python"
}

function Resolve-LatestSessionDirectory {
    $meetingsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) "Meetings"
    if (-not (Test-Path $meetingsRoot)) {
        throw "Meetings root not found at $meetingsRoot"
    }

    $latest = Get-ChildItem $meetingsRoot -Directory -Recurse |
        Where-Object { Test-Path (Join-Path $_.FullName "metadata.json") } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        throw "No meeting session folders with metadata.json were found under $meetingsRoot"
    }

    return $latest.FullName
}

if ([string]::IsNullOrWhiteSpace($SessionDir)) {
    $SessionDir = Resolve-LatestSessionDirectory
}

$pythonExecutable = Resolve-PythonExecutable -Root $workspaceRoot
$processingLog = Join-Path $SessionDir "processing.log"

Write-Host "Processing meeting session:"
Write-Host $SessionDir

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$output = & $pythonExecutable -m companion.meeting_companion process-session --session-dir $SessionDir 2>&1
$exitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference

$output | Tee-Object -FilePath $processingLog -Append

if ($exitCode -ne 0) {
    throw "Meeting processing failed with exit code $exitCode"
}
