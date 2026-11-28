[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$HoudiniRoot,
    [string]$HoudiniVersion,
    [switch]$Native,
    [switch]$HydraProbe,
    [string]$HoudiniPythonLibrary,
    [switch]$Locked
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'HoudiniDiscovery.ps1')
$oldHfs=$env:HFS
$oldPath=$env:PATH
Push-Location $root
try {
    $restoreArgs = @('restore','DXSharp.Houdini.slnx')
    if ($Locked) { $restoreArgs += '--locked-mode' }
    & dotnet @restoreArgs
    dotnet build DXSharp.Houdini.slnx -c $Configuration --no-restore
    dotnet run --project tests/Verification -c $Configuration --no-build
    if ($Native -or $HydraProbe) {
        $HoudiniRoot=Resolve-DXHoudiniRoot -Root $HoudiniRoot -Version $HoudiniVersion -RequireHDK
        $version=Get-DXHoudiniVersion $HoudiniRoot
        $rootHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($HoudiniRoot.ToUpperInvariant()))).Substring(0,12)
        $nativeDir="artifacts/native/$version-$rootHash"
        Write-Host "Houdini: $version ($HoudiniRoot)"
        Write-Host "Native build directory: $nativeDir"
        $env:HFS = $HoudiniRoot
        $env:PATH = (Join-Path $HoudiniRoot 'bin') + ';' + $env:PATH
        $configDir = Join-Path $HoudiniRoot 'toolkit/cmake'
        if (-not (Test-Path (Join-Path $configDir 'HoudiniConfig.cmake'))) { throw 'HDK CMake configuration not found.' }
        $cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        if (-not $cmake) {
            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
            if (Test-Path $vswhere) {
                $cmake = & $vswhere -latest -prerelease -products '*' -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
            }
        }
        if (-not $cmake) { throw 'CMake not found. Install the Visual Studio C++ CMake tools component or CMake and add it to PATH.' }
        $hydra = if ($HydraProbe) { "ON" } else { "OFF" }
        $pythonLib = if ($HoudiniPythonLibrary) { (Resolve-Path -LiteralPath $HoudiniPythonLibrary).Path } else { "" }
        & $cmake -S . -B $nativeDir -A x64 "-DHoudini_DIR=$configDir" "-DDXSHARP_HYDRA_PROBE=$hydra" "-DDXSHARP_HOUDINI_PYTHON_LIBRARY=$pythonLib"
        & $cmake --build $nativeDir --config $Configuration
        $probe = Get-ChildItem $nativeDir -Recurse -Filter hapi_abi_probe.exe | Where-Object { $_.Directory.Name -eq $Configuration } | Select-Object -First 1
        if (-not $probe) { throw 'Native ABI probe not found.' }
        & $probe.FullName | Tee-Object -FilePath $nativeDir/hapi-abi.json
        dotnet run --project samples/Diagnostics -c $Configuration --no-build -- --root $HoudiniRoot
        if ($HydraProbe) {
            $h = Get-ChildItem $nativeDir -Recurse -Filter hydra_abi_probe.exe | Where-Object { $_.Directory.Name -eq $Configuration } | Select-Object -First 1
            if (-not $h) { throw "Hydra ABI probe not found." }
            & $h.FullName | Tee-Object -FilePath $nativeDir/hydra-abi.json
        }
    }
}
finally { $env:HFS=$oldHfs; $env:PATH=$oldPath; Pop-Location }
