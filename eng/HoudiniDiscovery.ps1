# Shared, read-only Houdini discovery. Dot-source from engineering scripts.
function Get-DXHoudiniInstallations {
    [CmdletBinding()]
    param([string[]]$SearchRoot)
    $candidates=[System.Collections.Generic.List[string]]::new()
    foreach ($hint in @($env:DXSHARP_HOUDINI_ROOT,$env:HFS)) {
        if ($hint) { $candidates.Add($hint) }
    }
    $bases=@($SearchRoot)
    if ($env:ProgramFiles) { $bases+=Join-Path $env:ProgramFiles 'Side Effects Software' }
    foreach ($base in $bases) {
        if (-not $base -or -not (Test-Path -LiteralPath $base -PathType Container)) { continue }
        $candidates.Add($base)
        foreach ($dir in Get-ChildItem -LiteralPath $base -Directory -Filter 'Houdini*' -ErrorAction SilentlyContinue) {
            $candidates.Add($dir.FullName)
        }
    }
    if ($IsWindows) {
        foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
            foreach ($entry in Get-ItemProperty "$key\*" -ErrorAction SilentlyContinue) {
                if ($entry.DisplayName -match '^Houdini(?:\s|$)' -and $entry.InstallLocation) {
                    $candidates.Add([string]$entry.InstallLocation)
                }
            }
        }
    }
    $seen=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $installations=foreach ($candidate in $candidates) {
        try { $root=(Resolve-Path -LiteralPath $candidate -ErrorAction Stop).ProviderPath } catch { continue }
        if (-not $seen.Add($root)) { continue }
        $client=Join-Path $root 'bin/libHAPIL.dll'
        if (-not (Test-Path -LiteralPath $client -PathType Leaf)) { continue }
        $version=Get-DXHoudiniVersion $root
        [pscustomobject]@{
            Root=$root
            Version=$version
            Hython=(Test-Path -LiteralPath (Join-Path $root 'bin/hython.exe') -PathType Leaf)
            Husk=(Test-Path -LiteralPath (Join-Path $root 'bin/husk.exe') -PathType Leaf)
            HDK=(Test-Path -LiteralPath (Join-Path $root 'toolkit/cmake/HoudiniConfig.cmake') -PathType Leaf)
        }
    }
    $installations | Sort-Object -Property @{Expression='Version';Descending=$true},Root
}

function Get-DXHoudiniVersion([string]$Root) {
    # Prefer the installed binary version, not a guessed marketing version.
    $client=Join-Path $Root 'bin/libHAPIL.dll'
    try {
        $info=[Diagnostics.FileVersionInfo]::GetVersionInfo($client)
        if ($info.FileMajorPart -gt 0) {
            return [version]::new($info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart)
        }
    } catch {}
    if ((Split-Path -Leaf $Root) -match '(?<!\d)(\d+\.\d+\.\d+(?:\.\d+)?)(?!\d)') {
        return [version]$Matches[1]
    }
    return [version]'0.0.0.0'
}

function Resolve-DXHoudiniRoot {
    [CmdletBinding()]
    param([string]$Root,[string]$Version,[switch]$RequireHDK,[switch]$RequirePython)
    if (-not $Root -and -not $Version) {
        if ($env:DXSHARP_HOUDINI_ROOT) { $Root=$env:DXSHARP_HOUDINI_ROOT }
        elseif ($env:HFS) { $Root=$env:HFS }
    }
    if ($Root) {
        $resolved=(Resolve-Path -LiteralPath $Root -ErrorAction Stop).ProviderPath
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'bin/libHAPIL.dll') -PathType Leaf)) {
            throw "No Houdini HAPI client at $resolved"
        }
        if ($Version -and -not (Test-DXHoudiniVersion (Get-DXHoudiniVersion $resolved) $Version)) {
            throw "Explicit Houdini root does not match version $Version."
        }
    } else {
        $choices=@(Get-DXHoudiniInstallations | Where-Object {
            (-not $Version -or (Test-DXHoudiniVersion $_.Version $Version)) -and
            (-not $RequireHDK -or $_.HDK) -and (-not $RequirePython -or ($_.Hython -and $_.Husk))
        })
        if (-not $choices.Count) { throw 'No matching Houdini installation found. Run eng/find-houdini.ps1 or specify -HoudiniRoot.' }
        $resolved=$choices[0].Root
    }
    if ($RequireHDK -and -not (Test-Path -LiteralPath (Join-Path $resolved 'toolkit/cmake/HoudiniConfig.cmake') -PathType Leaf)) {
        throw "HDK not installed at $resolved"
    }
    if ($RequirePython -and ((-not (Test-Path -LiteralPath (Join-Path $resolved 'bin/hython.exe') -PathType Leaf)) -or
        (-not (Test-Path -LiteralPath (Join-Path $resolved 'bin/husk.exe') -PathType Leaf)))) {
        throw "hython/husk not installed at $resolved"
    }
    return $resolved
}

function Test-DXHoudiniVersion([version]$Actual,[string]$Requested) {
    $v=[version]$Requested
    return $v.Major -eq $Actual.Major -and $v.Minor -eq $Actual.Minor -and
        ($v.Build -lt 0 -or $v.Build -eq $Actual.Build) -and
        ($v.Revision -lt 0 -or $v.Revision -eq $Actual.Revision)
}
