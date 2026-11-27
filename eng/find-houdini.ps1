[CmdletBinding()]
param([string[]]$SearchRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'HoudiniDiscovery.ps1')
$found=@(Get-DXHoudiniInstallations -SearchRoot $SearchRoot)
if (-not $found.Count) { Write-Warning 'No Houdini installations found. Pass -SearchRoot for a custom install location.'; return }
$found | Format-Table Version,HDK,Hython,Husk,Root -AutoSize
