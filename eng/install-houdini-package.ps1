[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PreferenceDirectory = $env:HOUDINI_USER_PREF_DIR,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
if (-not $PreferenceDirectory) {
    $PreferenceDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'houdini22.0'
}
$directory = Join-Path $PreferenceDirectory 'packages'
$target = Join-Path $directory 'DXSharp.Houdini.json'
if ($Uninstall) {
    if ((Test-Path $target) -and $PSCmdlet.ShouldProcess($target,'Remove package registration')) {
        Remove-Item -LiteralPath $target
    }
    return
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '../houdini')).Path.Replace('\','/')
if ($PSCmdlet.ShouldProcess($target,'Register DXSharp Houdini package')) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    @{ enable = $true; path = $root } | ConvertTo-Json | Set-Content -LiteralPath $target -Encoding utf8
    Write-Host "Installed: $target"
    Write-Host 'Restart Houdini and add the DXSharp shelf from the shelf + menu.'
}
