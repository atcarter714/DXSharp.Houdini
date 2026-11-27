[CmdletBinding()]
param(
    [string]$HoudiniRoot,
    [string]$HoudiniVersion,
    [switch]$Aov,
    [switch]$Hardware,
    [switch]$Standalone,
    [string]$Python
)
$ErrorActionPreference='Stop'
# Check every native process explicitly, including on hosts where the automatic
# native-error preference is disabled or has different behavior.
$PSNativeCommandUseErrorActionPreference=$false
if (($Standalone -or $Hardware) -and -not $Aov) {
    throw '-Standalone and -Hardware require -Aov.'
}
if ($Python -and -not $Standalone) { throw '-Python is only used with -Standalone.' }
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'HoudiniDiscovery.ps1')

function Import-MsvcEnvironment {
    $existing=Get-Command link.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existing -and $env:VCToolsInstallDir -and $env:WindowsSdkDir -and $env:LIB -and
        $env:VSCMD_ARG_TGT_ARCH -eq 'x64' -and $existing.Source.StartsWith($env:VCToolsInstallDir,[StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "MSVC linker: $($existing.Source)"
        return
    }
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'vswhere.exe not found. Install Visual Studio or Build Tools with Desktop development with C++ (default components, including Windows SDK).'
    }
    $vs=@(& $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    if ($LASTEXITCODE -ne 0 -or -not $vs.Count) {
        Write-Host 'Visual Studio installations (including previews):'
        & $vswhere -all -prerelease -products '*' -property installationPath | ForEach-Object { Write-Host $_ }
        throw 'No Visual Studio instance reports the x64/x86 MSVC component. Install Desktop development with C++ (Visual Studio or Build Tools), then retry.'
    }
    $dev=Join-Path $vs[0] 'Common7/Tools/VsDevCmd.bat'
    if (-not (Test-Path $dev)) { throw "Missing developer environment: $dev" }
    $envLines=@(& cmd.exe /d /c ('call "{0}" -arch=x64 -host_arch=x64 >nul && set' -f $dev))
    if ($LASTEXITCODE -ne 0) { throw "VsDevCmd failed (exit $LASTEXITCODE)." }
    foreach ($line in $envLines) {
        if ($line -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1],$Matches[2],'Process')
        }
    }
    $link=Get-Command link.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $link -or -not $env:VCToolsInstallDir -or -not $env:WindowsSdkDir -or -not $env:LIB) {
        throw 'MSVC developer environment is incomplete. Check the x64 C++ toolset and Windows SDK in Visual Studio Installer.'
    }
    Write-Host "MSVC linker: $($link.Source)"
}

function Invoke-ProbePython([string]$Exe,[string[]]$Arguments,[string]$Marker) {
    $lines=@(& $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_; "$_" })
    $code=$LASTEXITCODE
    if ($code -ne 0 -or -not ($lines | Where-Object { $_ -like "$Marker*" })) {
        throw "Python qualification did not complete (exit $code, missing or failed $Marker). Inspect the preceding output."
    }
}

# Restore process environment, including the variables imported from VsDevCmd.
$original=@{}
Get-ChildItem Env: | ForEach-Object { $original[$_.Name]=$_.Value }
Push-Location $repo
try {
    $argsList=@((Join-Path $PSScriptRoot 'probe-hydra.py'))
    if ($Aov) {
        Import-MsvcEnvironment
        $publish=Join-Path $repo 'artifacts/hydra-probe'
        $dll=Join-Path $publish 'DXSharp.HydraProbe.dll'
        # Remove only the expected prior binary so a failed publish cannot load stale code.
        if (Test-Path $dll) { Remove-Item -LiteralPath $dll -Force }
        dotnet publish samples/HydraProbe -c Release -o $publish
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dll)) {
            throw 'NativeAOT publish failed. No Houdini or renderer probe will be launched.'
        }
        $argsList+=@('--dll',$dll)
        if ($Hardware) { $argsList+='--hardware' }
    }
    if ($Standalone) {
        $argsList+='--standalone'
        if ($Python) {
            $exe=(Get-Command $Python -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        } else {
            $launcher=Get-Command py.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($launcher) {
                $exe=$launcher.Source
                $argsList=@('-3')+$argsList
            } else {
                $exe=(Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
            }
        }
        Invoke-ProbePython $exe $argsList 'PASS: DXSharp NativeAOT D3D12'
    } else {
        $HoudiniRoot=Resolve-DXHoudiniRoot -Root $HoudiniRoot -Version $HoudiniVersion -RequirePython
        Write-Host "Houdini: $(Get-DXHoudiniVersion $HoudiniRoot) ($HoudiniRoot)"
        $env:HFS=$HoudiniRoot
        $env:PATH=(Join-Path $HoudiniRoot 'bin')+';'+$env:PATH
        $marker=if ($Aov) { 'PASS: DXSharp NativeAOT D3D12' } else { 'PASS: Houdini USD discovery' }
        Invoke-ProbePython (Join-Path $HoudiniRoot 'bin/hython.exe') $argsList $marker
        if (-not $Aov) {
            Write-Host '--- husk renderer enumeration (separate from Python qualification) ---'
            $lines=@(& (Join-Path $HoudiniRoot 'bin/husk.exe') --list-renderers 2>&1 | ForEach-Object { Write-Host $_; "$_" })
            $code=$LASTEXITCODE
            if ($code -ne 0 -or ($lines -match 'No licenses could be found')) {
                throw "husk reported a failure or license diagnostic (exit $code); renderer listing does not establish render capability."
            }
        }
    }
}
finally {
    Get-ChildItem Env: | Where-Object { -not $original.ContainsKey($_.Name) } | ForEach-Object {
        [Environment]::SetEnvironmentVariable($_.Name,$null,'Process')
    }
    foreach ($name in $original.Keys) { [Environment]::SetEnvironmentVariable($name,$original[$name],'Process') }
    Pop-Location
}
