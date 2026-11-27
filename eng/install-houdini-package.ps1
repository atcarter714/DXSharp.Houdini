[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PreferenceDirectory,
    [string]$HoudiniRoot,
    [string]$HoudiniVersion,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
if (-not $PreferenceDirectory -and -not $HoudiniRoot -and -not $HoudiniVersion) {
    $PreferenceDirectory=$env:HOUDINI_USER_PREF_DIR
}
if (-not $PreferenceDirectory) {
    . (Join-Path $PSScriptRoot 'HoudiniDiscovery.ps1')
    $HoudiniRoot=Resolve-DXHoudiniRoot -Root $HoudiniRoot -Version $HoudiniVersion
    $version=Get-DXHoudiniVersion $HoudiniRoot
    if ($version.Major -eq 0) { throw 'Unable to determine Houdini preference version. Specify -PreferenceDirectory.' }
    $PreferenceDirectory=Join-Path ([Environment]::GetFolderPath('MyDocuments')) "houdini$($version.Major).$($version.Minor)"
    Write-Host "Houdini: $version ($HoudiniRoot)"
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
    $iconPath = Join-Path $root 'config/Icons/dxsharp_studio.png'
    [IO.File]::WriteAllBytes($iconPath, [Convert]::FromBase64String((Get-Content -LiteralPath "$iconPath.base64" -Raw)))
    @{ enable = $true; path = $root } | ConvertTo-Json | Set-Content -LiteralPath $target -Encoding utf8
    Write-Host "Installed: $target"
    Write-Host 'Restart Houdini and add the DXSharp shelf from the shelf + menu.'
}
