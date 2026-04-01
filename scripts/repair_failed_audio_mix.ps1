param(
    [string]$SessionDir,
    [switch]$AllFailed
)

$ErrorActionPreference = "Stop"
$global:PSNativeCommandUseErrorActionPreference = $false

$workspaceRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Import-DotEnv.ps1") -Root $workspaceRoot

function Resolve-FfmpegPath {
    $configPath = Join-Path $workspaceRoot "meeting-recorder.config.json"
    if (Test-Path $configPath) {
        try {
            $config = Get-Content $configPath -Raw | ConvertFrom-Json
            if ($config.app.ffmpegPath) {
                return [string]$config.app.ffmpegPath
            }
        }
        catch {
        }
    }

    $bundled = Join-Path $workspaceRoot "companion\\bin\\ffmpeg\\ffmpeg.exe"
    if (Test-Path $bundled) {
        return $bundled
    }

    if ($env:FFMPEG_PATH) {
        return $env:FFMPEG_PATH
    }

    $command = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "ffmpeg was not found. Put ffmpeg on PATH or set app.ffmpegPath in meeting-recorder.config.json."
}

function Get-FailedSessionDirectories {
    $meetingsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) "Meetings"
    if (-not (Test-Path $meetingsRoot)) {
        throw "Meetings root not found at $meetingsRoot"
    }

    Get-ChildItem -Path $meetingsRoot -Recurse -Filter app.log |
        Where-Object {
            Select-String -Path $_.FullName -Pattern 'ffmpeg could not mix system audio and microphone' -Quiet
        } |
        ForEach-Object { Split-Path -Parent (Split-Path -Parent $_.FullName) } |
        Sort-Object -Unique
}

function Repair-SessionAudioMix {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$FfmpegPath
    )

    $sessionRoot = (Resolve-Path $Root).Path
    $internalDir = Join-Path $sessionRoot "_session"
    $metadataPath = Join-Path $sessionRoot "metadata.json"
    $recordingPath = Join-Path $sessionRoot "recording.mp4"
    $systemAudioPath = Join-Path $internalDir "system-audio.wav"
    $micPath = Join-Path $internalDir "mic.wav"
    $mixedPath = Join-Path $internalDir "mixed-audio.wav"
    $repairedPath = Join-Path $sessionRoot "recording.repaired.mp4"

    if (-not (Test-Path $recordingPath)) {
        throw "Recording file not found: $recordingPath"
    }

    if (-not (Test-Path $systemAudioPath) -or -not (Test-Path $micPath)) {
        throw "Both system-audio.wav and mic.wav are required to repair $sessionRoot"
    }

    & $FfmpegPath -y `
        -i $systemAudioPath `
        -i $micPath `
        -filter_complex "[0:a]volume=1.10[a0];[1:a]volume=1.35,highpass=f=120,lowpass=f=7800,acompressor=threshold=-20dB:ratio=3:attack=15:release=200[a1];[a0][a1]amix=inputs=2:weights='1 1.25':normalize=0,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,dynaudnorm=f=181:g=12:p=0.9,alimiter=limit=0.95[a]" `
        -map "[a]" `
        -ar 48000 `
        -ac 2 `
        $mixedPath | Out-Null

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $mixedPath)) {
        throw "ffmpeg failed while building the mixed audio track for $sessionRoot"
    }

    $hasVideo = $true
    if (Test-Path $metadataPath) {
        try {
            $metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
            $videoCaptureMode = [string]$metadata.videoCaptureMode
            if ([string]::Equals($videoCaptureMode, "none", [System.StringComparison]::OrdinalIgnoreCase)) {
                $hasVideo = $false
            }
        }
        catch {
        }
    }

    if ($hasVideo) {
        & $FfmpegPath -y `
            -i $recordingPath `
            -i $mixedPath `
            -map 0:v:0 `
            -map 1:a:0 `
            -c:v copy `
            -c:a aac `
            -b:a 192k `
            -ar 48000 `
            -ac 2 `
            -movflags +faststart `
            -shortest `
            $repairedPath | Out-Null
    }
    else {
        & $FfmpegPath -y `
            -i $mixedPath `
            -c:a aac `
            -b:a 192k `
            -ar 48000 `
            -ac 2 `
            -movflags +faststart `
            $repairedPath | Out-Null
    }

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $repairedPath)) {
        throw "ffmpeg failed while remuxing the repaired recording for $sessionRoot"
    }

    Move-Item -Force -LiteralPath $repairedPath -Destination $recordingPath

    $logPath = Join-Path $internalDir "app.log"
    Add-Content -Path $logPath -Value ("[{0}] Repaired final recording audio mix and remuxed recording.mp4" -f ([DateTimeOffset]::Now.ToString("O")))
    Write-Host "Repaired audio mix for $sessionRoot"
}

$ffmpegPath = Resolve-FfmpegPath

$targets = @()
if ($AllFailed) {
    $targets = Get-FailedSessionDirectories
}
elseif (-not [string]::IsNullOrWhiteSpace($SessionDir)) {
    $targets = @($SessionDir)
}
else {
    throw "Provide -SessionDir or use -AllFailed."
}

foreach ($target in $targets) {
    Repair-SessionAudioMix -Root $target -FfmpegPath $ffmpegPath
}
