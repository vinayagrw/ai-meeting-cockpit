param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$configuration = if ($Release) { "Release" } else { "Debug" }

. (Join-Path $PSScriptRoot "RecorderProcessTools.ps1")

$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot ".dotnet-cli"
$env:APPDATA = Join-Path $workspaceRoot ".appdata"

$nugetDirectory = Join-Path $env:APPDATA "NuGet"
New-Item -ItemType Directory -Path $nugetDirectory -Force | Out-Null
Copy-Item (Join-Path $workspaceRoot "windows-shell\\NuGet.Config") (Join-Path $nugetDirectory "NuGet.Config") -Force

Stop-WorkspaceRecorderProcesses -WorkspaceRoot $workspaceRoot

$projects = @(
    "windows-shell\\MeetingRecorder.Shared\\MeetingRecorder.Shared.csproj",
    "windows-shell\\MeetingRecorder.CaptureWorker\\MeetingRecorder.CaptureWorker.csproj",
    "windows-shell\\MeetingRecorder.Windows\\MeetingRecorder.Windows.csproj"
)

foreach ($project in $projects) {
    Write-Host "Building $project ($configuration)..."
    dotnet build (Join-Path $workspaceRoot $project) -c $configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $project"
    }
}

Write-Host ""
Write-Host "Windows shell build completed successfully."
