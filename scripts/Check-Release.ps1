param(
    [string]$Repo = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$bin = Join-Path $Repo 'bin'
$pack = Join-Path $bin 'RailroaderModSettings'

foreach ($file in @('RailroaderModSettings.dll', '0Harmony.dll', 'Info.json')) {
    $path = Join-Path $pack $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file missing: $path"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $pack 'Info.json') -Raw | ConvertFrom-Json
if ($manifest.version -match '^__' -or $manifest.assemblies -notcontains 'RailroaderModSettings.dll') {
    throw 'Manifest placeholders were not rendered correctly.'
}

$zip = Get-ChildItem -LiteralPath $bin -Filter 'RailroaderModSettings_*.zip' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $zip) {
    throw "Versioned release ZIP missing in $bin"
}

$entries = @(& tar.exe -tf $zip.FullName | ForEach-Object { $_.Replace('\', '/') -replace '^\./', '' })
if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $($zip.FullName)" }
foreach ($entry in @('RailroaderModSettings/Info.json', 'RailroaderModSettings/RailroaderModSettings.dll', 'RailroaderModSettings/0Harmony.dll')) {
    if (-not ($entries -contains $entry)) {
        throw "ZIP missing entry: $entry"
    }
}

Write-Host "Looks good: $($zip.FullName)"
Write-Host 'Contains RailroaderModSettings/ with the manifest, main DLL, and Harmony.'
