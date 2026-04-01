function Get-WorkspaceRecorderProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot
    )

    $workspaceShellRoot = (Join-Path $WorkspaceRoot "windows-shell")

    Get-Process | Where-Object {
        try {
            $_.ProcessName -like "MeetingRecorder*" -and
            -not [string]::IsNullOrWhiteSpace($_.Path) -and
            $_.Path.StartsWith($workspaceShellRoot, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    }
}

function Stop-WorkspaceRecorderProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot,
        [int]$GracefulWaitMs = 3000
    )

    $processes = @(Get-WorkspaceRecorderProcesses -WorkspaceRoot $WorkspaceRoot)
    foreach ($process in $processes) {
        Write-Host "Stopping $($process.ProcessName) ($($process.Id))..."

        $stopped = $false
        try {
            if ($process.MainWindowHandle -ne 0) {
                $null = $process.CloseMainWindow()
                $stopped = $process.WaitForExit($GracefulWaitMs)
            }
        }
        catch {
            $stopped = $false
        }

        if (-not $stopped) {
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
            }
            catch {
            }
        }
    }

    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) {
                $null = $process.WaitForExit(2000)
            }
        }
        catch {
        }
    }
}

function Get-WorkspaceRecorderProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot
    )

    return @(Get-WorkspaceRecorderProcesses -WorkspaceRoot $WorkspaceRoot) |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
}
