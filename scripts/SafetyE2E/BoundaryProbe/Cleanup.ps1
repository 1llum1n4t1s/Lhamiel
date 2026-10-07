[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('before','after')][string]$Phase)
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
    $ledger = Join-Path $evidence ('boundary-' + $Phase + '-junction-cleanup-' + [guid]::NewGuid().ToString('N') + '.json')
    & 'C:/Users/IMT/.codex/scripts/Remove-CodexItem.ps1' -LiteralPath $links.ToArray() -AllowedRoot $evidence -LedgerPath $ledger -AllowLeafJunction
    if (@($links | Where-Object { Test-Path -LiteralPath $_ }).Count -ne 0) { throw 'Junction cleanup incomplete.' }
}
if ($fixtures.Count -gt 0) {
    $ledger = Join-Path $evidence ('boundary-' + $Phase + '-fixture-cleanup-' + [guid]::NewGuid().ToString('N') + '.json')
    & 'C:/Users/IMT/.codex/scripts/Remove-CodexItem.ps1' -LiteralPath ([string[]]$fixtures.FullName) -AllowedRoot $evidence -LedgerPath $ledger
    if (@($fixtures | Where-Object { Test-Path -LiteralPath $_.FullName }).Count -ne 0) { throw 'Fixture cleanup incomplete.' }
}
Write-Output ([pscustomobject]@{ Phase=$Phase; Fixtures=$fixtures.Count; Junctions=$links.Count; Remaining=0 })
