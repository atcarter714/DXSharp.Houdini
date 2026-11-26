[CmdletBinding()]
param(
    [string]$HoudiniRoot = 'C:/Program Files/Side Effects Software/Houdini 22.0.514',
    [switch]$Aov,
    [switch]$Hardware
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$true
$repo=Split-Path -Parent $PSScriptRoot
$HoudiniRoot=(Resolve-Path $HoudiniRoot).Path
$oldPath=$env:PATH
$oldHfs=$env:HFS
Push-Location $repo
try {
    $env:HFS=$HoudiniRoot
    $env:PATH=(Join-Path $HoudiniRoot 'bin')+';'+$oldPath
    $argsList=@((Join-Path $PSScriptRoot 'probe-hydra.py'))
    if($Aov) {
        $publish=Join-Path $repo 'artifacts/hydra-probe'
        dotnet publish samples/HydraProbe -c Release -o $publish
        $argsList+=@('--dll',(Join-Path $publish 'DXSharp.HydraProbe.dll'))
        if($Hardware) { $argsList+='--hardware' }
    }
    & (Join-Path $HoudiniRoot 'bin/hython.exe') @argsList
    & (Join-Path $HoudiniRoot 'bin/husk.exe') --list-renderers
}
finally {
    $env:PATH=$oldPath
    $env:HFS=$oldHfs
    Pop-Location
}
