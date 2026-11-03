param(
    [Parameter(Mandatory=$true)][string]$Template,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$AssemblyName
)

$ErrorActionPreference = 'Stop'

$contents = [System.IO.File]::ReadAllText($Template)
$contents = $contents.Replace('__MOD_VERSION__', $Version)
$contents = $contents.Replace('__ASSEMBLY_NAME__', $AssemblyName)

# Fail before deployment if the manifest is invalid.
$null = ConvertFrom-Json -InputObject $contents

$directory = [System.IO.Path]::GetDirectoryName($Destination)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
[System.IO.File]::WriteAllText($Destination, $contents, [System.Text.UTF8Encoding]::new($false))
