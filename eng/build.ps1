[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$HoudiniRoot,
    [switch]$Native,
    [switch]$Locked
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $restoreArgs = @('restore','DXSharp.Houdini.slnx')
    if ($Locked) { $restoreArgs += '--locked-mode' }
    & dotnet @restoreArgs
    dotnet build DXSharp.Houdini.slnx -c $Configuration --no-restore
    dotnet run --project tests/Verification -c $Configuration --no-build
    if ($Native) {
        if (-not $HoudiniRoot) { $HoudiniRoot = $env:DXSHARP_HOUDINI_ROOT }
        if (-not $HoudiniRoot) { $HoudiniRoot = $env:HFS }
        if (-not $HoudiniRoot) { $HoudiniRoot = Join-Path $env:ProgramFiles 'Side Effects Software/Houdini 22.0.514' }
        $HoudiniRoot = (Resolve-Path $HoudiniRoot).Path
        $env:HFS = $HoudiniRoot
        $env:PATH = (Join-Path $HoudiniRoot 'bin') + ';' + $env:PATH
        $configDir = Join-Path $HoudiniRoot 'toolkit/cmake'
        if (-not (Test-Path (Join-Path $configDir 'HoudiniConfig.cmake'))) { throw 'HDK CMake configuration not found.' }
        $cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
        if (-not $cmake) {
            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
            if (Test-Path $vswhere) {
                $cmake = & $vswhere -latest -products '*' -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
            }
        }
        if (-not $cmake) { throw 'CMake not found. Install the Visual Studio C++ CMake tools component or CMake and add it to PATH.' }
        & $cmake -S . -B artifacts/native -A x64 "-DHoudini_DIR=$configDir"
        & $cmake --build artifacts/native --config $Configuration
        $probe = Get-ChildItem artifacts/native -Recurse -Filter hapi_abi_probe.exe | Where-Object { $_.Directory.Name -eq $Configuration } | Select-Object -First 1
        if (-not $probe) { throw 'Native ABI probe not found.' }
        & $probe.FullName | Tee-Object -FilePath artifacts/hapi-abi.json
        dotnet run --project samples/Diagnostics -c $Configuration --no-build -- --root $HoudiniRoot
    }
}
finally { Pop-Location }
