param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$dotenvPath = Join-Path $Root ".env"
if (-not (Test-Path $dotenvPath)) {
    return
}

Get-Content $dotenvPath | ForEach-Object {
    $line = $_.Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#") -or -not $line.Contains("=")) {
        return
    }

    $parts = $line.Split("=", 2)
    $name = $parts[0].Trim()
    $value = $parts[1].Trim()

    if ([string]::IsNullOrWhiteSpace($name)) {
        return
    }

    if ($value.Length -ge 2) {
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
    }

    [System.Environment]::SetEnvironmentVariable($name, $value, "Process")
}
