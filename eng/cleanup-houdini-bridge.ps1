[CmdletBinding(SupportsShouldProcess)]
param([switch]$RemoveStale, [switch]$IncludeUnregistered, [int]$MinimumAgeHours = 24)
$ErrorActionPreference = 'Stop'
$cutoff = (Get-Date).AddHours(-[Math]::Max(1,$MinimumAgeHours))
$houdiniRunning = @(Get-Process -Name 'houdini*' -ErrorAction SilentlyContinue).Count -gt 0
Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'dxsharp-houdini-*' |
    ForEach-Object {
        $dir = $_
        if (($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return }
        $manifest = Join-Path $dir.FullName 'session.json'
        $state = 'Unregistered'
        $canRemove = $false
        if (Test-Path -LiteralPath $manifest) {
            try {
                $m = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
                if ($m.protocol -ne 1 -or [int]$m.pid -le 0) { throw 'Invalid manifest' }
                $process = Get-Process -Id ([int]$m.pid) -ErrorAction SilentlyContinue
                if ($process) { $state = 'Process exists; keep' }
                else { $state = 'Stale registered session'; $canRemove = $true }
            } catch { $state = 'Invalid manifest; keep' }
        } elseif ($IncludeUnregistered -and -not $houdiniRunning) {
            $state = 'Old unregistered candidate; Houdini not running'
            $canRemove = $true
        }
        $old = $dir.LastWriteTime -lt $cutoff
        [pscustomobject]@{ Directory=$dir.FullName; LastWrite=$dir.LastWriteTime; State=$state; Eligible=($canRemove -and $old) }
        if ($RemoveStale -and $canRemove -and $old -and $PSCmdlet.ShouldProcess($dir.FullName,'Remove stale DXSharp bridge directory')) {
            # Restrict deletion to bridge-owned file patterns. Unknown files are retained.
            $children = @(Get-ChildItem -LiteralPath $dir.FullName -Force)
            if (@($children | Where-Object { $_.PSIsContainer -or $_.Attributes -band [IO.FileAttributes]::ReparsePoint -or $_.Name -notmatch '^(session\.json|[0-9a-f]{32}\.(request|response)\.(json|tmp)(\.tmp)?)$' }).Count -eq 0) {
                Remove-Item -LiteralPath $dir.FullName -Recurse -Force
            } else { Write-Warning "Unexpected contents; preserved $($dir.FullName)" }
        }
    }
