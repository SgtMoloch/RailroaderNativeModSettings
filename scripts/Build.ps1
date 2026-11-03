param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'RailroaderModSettings\RailroaderModSettings.csproj'

$arguments = @('build', $project, '-c', $Configuration)
if ($NoDeploy) {
    $arguments += '-p:PublishMod=false'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if ($NoDeploy) {
    Write-Host 'Build succeeded; deployment/packaging was skipped.'
} elseif ($Configuration -eq 'Release') {
    Write-Host "Release folder: $(Join-Path $repo 'bin\RailroaderModSettings')"
    Write-Host "Release ZIPs:   $(Join-Path $repo 'bin\RailroaderModSettings_*.zip')"
} else {
    Write-Host 'Debug build deployed to Railroader/ModDev (see MSBuild output for exact path).'
}
