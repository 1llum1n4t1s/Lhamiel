[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('before','after')][string]$Phase)

# 検証済みの作業対象だけを標準APIで削除する。
function Remove-LocalWorkItem {
    param([string[]]$LiteralPath, [string]$AllowedRoot, [switch]$IgnoreMissing, [switch]$AllowLeafJunction)
    if ([string]::IsNullOrWhiteSpace($AllowedRoot) -or -not [IO.Path]::IsPathRooted($AllowedRoot)) { throw 'Cleanup root must be absolute.' }
    $boundary = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($target in $LiteralPath) {
        if ([string]::IsNullOrWhiteSpace($target) -or -not [IO.Path]::IsPathRooted($target)) { throw 'Cleanup target must be absolute.' }
        $resolved = [IO.Path]::GetFullPath($target)
        if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target is outside the allowed root.' }
        $ancestor = $resolved
        while ($ancestor) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and -not ($AllowLeafJunction -and $ancestor -eq $resolved -and $item.LinkType -eq 'Junction')) { throw 'Cleanup path contains a reparse point.' }
            }
            $ancestor = Split-Path $ancestor -Parent
        }
        if (-not (Test-Path -LiteralPath $resolved)) { if ($IgnoreMissing) { continue }; throw "Cleanup target is missing: $resolved" }
        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        $leafLink = [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
        if ($item.PSIsContainer -and -not $leafLink) {
            if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force -ErrorAction Stop | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Cleanup directory contains a reparse point.' }
        }
        Remove-Item -LiteralPath $resolved -Recurse:($item.PSIsContainer -and -not $leafLink) -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $resolved) { throw "Cleanup target remains: $resolved" }
    }
}

$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.codex/gogo-rere-20261007/evidence'))
$fixtures = @(Get-ChildItem -LiteralPath $evidence -Directory | Where-Object { $_.Name -match ('^boundary-' + $Phase + '-[0-9a-f]{32}$') })
$links = [Collections.Generic.List[string]]::new()
foreach ($fixture in $fixtures) {
    if (($fixture.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Fixture root is a reparse point.' }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($fixture.FullName)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                if ($entry.LinkType -ne 'Junction') { throw 'Unsupported link retained; stop cleanup.' }
                $links.Add($entry.FullName)
            } elseif ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
        }
    }
}
if ($links.Count -gt 0) {
    Remove-LocalWorkItem -LiteralPath $links.ToArray() -AllowedRoot $evidence -AllowLeafJunction
    if (@($links | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0) { throw 'Junction cleanup incomplete.' }
}
if ($fixtures.Count -gt 0) {
    Remove-LocalWorkItem -LiteralPath ([string[]]$fixtures.FullName) -AllowedRoot $evidence
    if (@($fixtures | Where-Object { Test-Path -LiteralPath $_.FullName }).Count -ne 0) { throw 'Fixture cleanup incomplete.' }
}
Write-Output ([pscustomobject]@{ Phase=$Phase; Fixtures=$fixtures.Count; Junctions=$links.Count; Remaining=0 })
