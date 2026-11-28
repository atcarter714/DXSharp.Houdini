[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'HoudiniDiscovery.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ("dxsharp-discovery-"+[guid]::NewGuid().ToString('N'))
try {
    foreach ($version in @('22.0.99','22.0.508','22.0.514')) {
        $bin=Join-Path $temp "Houdini $version/bin"
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $bin 'libHAPIL.dll'),[byte[]]@())
    }
    $found=@(Get-DXHoudiniInstallations -SearchRoot $temp | Where-Object { $_.Root.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) })
    if ($found.Count -ne 3 -or $found[0].Version.Build -ne 514 -or $found[1].Version.Build -ne 508) {
        throw 'Numeric version discovery failed.'
    }
    if (-not (Test-DXHoudiniVersion $found[1].Version '22.0.508')) { throw 'Exact selector failed.' }
    if (Test-DXHoudiniVersion $found[0].Version '22.0.508') { throw 'Different version matched.' }
    if ((Resolve-DXHoudiniRoot -Root $found[1].Root) -ne $found[1].Root) { throw 'Explicit root failed.' }
    $normalized=ConvertTo-DXHoudiniApplicationVersion ([version]'22.0.0.508')
    if ($normalized -ne [version]'22.0.508.0' -or -not (Test-DXHoudiniVersion $normalized '22.0.508')) { throw 'SideFX PE version normalization failed.' }
    if ((ConvertTo-DXHoudiniApplicationVersion ([version]'22.0.514.0')) -ne [version]'22.0.514.0') { throw 'Canonical version changed.' }
    if ((ConvertTo-DXHoudiniApplicationVersion ([version]'22.0.514.2')) -ne [version]'22.0.514.2') { throw 'Nonzero patch lost.' }
    Write-Host 'PASS: Houdini discovery, numeric ordering and explicit root selection.'
}
finally { if(Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
