param(
    [string]$SessionDir,
    [switch]$AllSessions,
    [switch]$DeleteLegacyFiles
)

$ErrorActionPreference = "Stop"

function Get-MeetingsRoot {
    return Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) "Meetings"
}

function Get-SessionTargets {
    param(
        [string]$Root,
        [string]$ExplicitSessionDir,
        [switch]$MigrateAll
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitSessionDir)) {
        if (-not (Test-Path (Join-Path $ExplicitSessionDir "metadata.json"))) {
            throw "metadata.json not found in $ExplicitSessionDir"
        }

        return @((Resolve-Path $ExplicitSessionDir).Path)
    }

    if (-not $MigrateAll) {
        throw "Provide -SessionDir or use -AllSessions."
    }

    if (-not (Test-Path $Root)) {
        throw "Meetings root not found at $Root"
    }

    return Get-ChildItem -Path $Root -Directory -Recurse |
        Where-Object { Test-Path (Join-Path $_.FullName "metadata.json") } |
        Select-Object -ExpandProperty FullName
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Add-PropertyIfPresent {
    param(
        [hashtable]$Target,
        [string]$Name,
        $Value
    )

    if ($null -ne $Value) {
        $Target[$Name] = $Value
    }
}

function ConvertTo-Hashtable {
    param($InputObject)

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [hashtable]) {
        return $InputObject
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $hash = @{}
        foreach ($key in $InputObject.Keys) {
            $hash[$key] = ConvertTo-Hashtable -InputObject $InputObject[$key]
        }
        return $hash
    }

    if ($InputObject -is [System.Collections.IEnumerable] -and -not ($InputObject -is [string])) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += ,(ConvertTo-Hashtable -InputObject $item)
        }
        return $items
    }

    if ($InputObject.PSObject -and $InputObject.PSObject.Properties.Count -gt 0) {
        $hash = @{}
        foreach ($property in $InputObject.PSObject.Properties) {
            $hash[$property.Name] = ConvertTo-Hashtable -InputObject $property.Value
        }
        return $hash
    }

    return $InputObject
}

function Migrate-SessionInsights {
    param(
        [string]$Root,
        [switch]$DeleteFiles
    )

    $metadataPath = Join-Path $Root "metadata.json"
    $insightsPath = Join-Path $Root "insights.json"
    $metadata = Read-JsonFile -Path $metadataPath
    if ($null -eq $metadata) {
        Write-Warning "Skipping $Root because metadata.json could not be read."
        return
    }

    $existingInsights = ConvertTo-Hashtable -InputObject (Read-JsonFile -Path $insightsPath)
    if ($null -eq $existingInsights) {
        $existingInsights = @{}
    }

    Add-PropertyIfPresent -Target $existingInsights -Name "smartTitle" -Value $metadata.smartTitle
    Add-PropertyIfPresent -Target $existingInsights -Name "meetingType" -Value (ConvertTo-Hashtable -InputObject $metadata.meetingType)

    $legacyMap = @{
        "meeting-type.json" = "meetingType"
        "sentiment.json"    = "sentiment"
        "agenda.json"       = "agenda"
        "highlights.json"   = "highlights"
        "commitments.json"  = "commitments"
    }

    foreach ($legacyFile in $legacyMap.Keys) {
        $legacyPath = Join-Path $Root $legacyFile
        $payload = ConvertTo-Hashtable -InputObject (Read-JsonFile -Path $legacyPath)
        if ($null -ne $payload) {
            $existingInsights[$legacyMap[$legacyFile]] = $payload
        }
    }

    if ($existingInsights.Count -eq 0) {
        Write-Host "Skipping $Root because no legacy insight artifacts were found."
        return
    }

    $existingInsights | ConvertTo-Json -Depth 100 | Set-Content -Path $insightsPath -Encoding UTF8

    $metadataHash = ConvertTo-Hashtable -InputObject $metadata
    if ($null -eq $metadataHash) {
        $metadataHash = @{}
    }

    $metadataHash["smartTitle"] = $existingInsights["smartTitle"]
    if ($existingInsights.ContainsKey("meetingType")) {
        $metadataHash["meetingType"] = $existingInsights["meetingType"]
    }

    if (-not $metadataHash.ContainsKey("paths") -or $null -eq $metadataHash["paths"]) {
        $metadataHash["paths"] = @{}
    }
    $metadataHash["insightsPath"] = $insightsPath

    if ($DeleteFiles) {
        foreach ($legacyFile in $legacyMap.Keys) {
            $legacyPath = Join-Path $Root $legacyFile
            if (Test-Path $legacyPath) {
                Remove-Item -LiteralPath $legacyPath -Force
            }
        }

        foreach ($legacyKey in @("meetingTypePath", "sentimentPath", "agendaPath", "highlightsPath", "commitmentsPath")) {
            if ($metadataHash.ContainsKey($legacyKey)) {
                $metadataHash.Remove($legacyKey) | Out-Null
            }
        }
    }

    $metadataHash | ConvertTo-Json -Depth 100 | Set-Content -Path $metadataPath -Encoding UTF8
    Write-Host "Migrated insights for $Root"
}

$meetingsRoot = Get-MeetingsRoot
$targets = Get-SessionTargets -Root $meetingsRoot -ExplicitSessionDir $SessionDir -MigrateAll:$AllSessions

foreach ($target in $targets) {
    try {
        Migrate-SessionInsights -Root $target -DeleteFiles:$DeleteLegacyFiles
    }
    catch {
        Write-Warning "Failed to migrate $target. $($_.Exception.Message)"
    }
}
