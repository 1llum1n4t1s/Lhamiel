#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorkRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$helperDirectories = @(
    (Join-Path $repo 'scripts'), (Join-Path $repo '.github/scripts'), (Join-Path $repo 'desktop/scripts')
) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'R2Transfer.Core.cs') -PathType Leaf }
if (@($helperDirectories).Count -ne 1) { throw 'Expected exactly one R2 helper directory.' }
$helpers = @($helperDirectories)[0]
$core = Join-Path $helpers 'R2Transfer.Core.cs'
# 既存データ・リンクへ書かず、各検証は新規の明示した外部ディレクトリで実行する。
if (![IO.Path]::IsPathFullyQualified($WorkRoot) -or (Test-Path -LiteralPath $WorkRoot)) { throw 'WorkRoot must be an unused absolute path.' }
for ($ancestor = Split-Path -Parent $WorkRoot; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and (Get-Item -LiteralPath $ancestor -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'WorkRoot has a linked ancestor.' }
}
[void][IO.Directory]::CreateDirectory($WorkRoot)
Add-Type -Path @($core, (Join-Path $PSScriptRoot 'TransferTests.cs')) -ErrorAction Stop
$networkGuard = [Kagayoi.R2.Tests.Probe+NetworkGuard]::new()
try {
$networkGuard.Verify()
[Kagayoi.R2.Transfer]::RegisterSourceHash((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant())
$hasArtifact = Test-Path -LiteralPath (Join-Path $helpers 'R2Artifact.Common.ps1')
$entry = Join-Path $helpers $(if ($hasArtifact) { 'R2Artifact.Common.ps1' } else { 'R2Distribution.Common.ps1' })
. $entry
. $entry
[Kagayoi.R2.Tests.Probe]::RunBoundaries($WorkRoot)
function New-R2DistributionClient { return [Kagayoi.R2.Tests.Probe]::NewClient() }
$inputDirectory = [Kagayoi.R2.Tests.Probe]::PrepareInput($WorkRoot)
$identity = @{ AccountId = [Kagayoi.R2.Tests.Probe]::Account; Bucket = [Kagayoi.R2.Tests.Probe]::Bucket
    Name = 'deps.fixture'; Repository = [Kagayoi.R2.Tests.Probe]::Repository; Commit = [Kagayoi.R2.Tests.Probe]::Commit; RunId = '1'; RunAttempt = '1'
    AccessKeyId = [Kagayoi.R2.Tests.Probe]::Access; SecretAccessKey = [Kagayoi.R2.Tests.Probe]::Secret }
$published = Publish-R2Object -AccountId $identity.AccountId -Bucket $identity.Bucket -FilePath (Join-Path $inputDirectory 'first.txt') -Immutable -AccessKeyId $identity.AccessKeyId -SecretAccessKey $identity.SecretAccessKey
[Kagayoi.R2.Tests.Probe]::Check(($published.Changed -and $published.Size -eq 5), 'PowerShell Publish-R2Object result')
if ($hasArtifact) {
# Set-Locationと.NET process cwdを意図的に分け、PS利用者の現在位置で相対pathを解決する。
$savedLocation = Get-Location
try {
Set-Location -LiteralPath $WorkRoot
$selected = @(Get-R2ArtifactFiles 'input/**/*')
[Kagayoi.R2.Tests.Probe]::Check(($selected.Count -eq 2 -and $selected[0].FullName.StartsWith($inputDirectory)), 'PowerShell current-location wildcard lookup')
foreach ($pattern in @('!input/**/*', 'input/[ab].txt')) {
    $invalidPattern = $false
    try { Get-R2ArtifactFiles $pattern | Out-Null } catch { $invalidPattern = $_.Exception.Message.Contains('code=unsupported-pattern') }
    [Kagayoi.R2.Tests.Probe]::Check($invalidPattern, 'pattern rejection precedes normalization')
    $invalidPublish = $false
    try { Publish-R2Artifact @identity -Path $pattern | Out-Null } catch { $invalidPublish = $_.Exception.Message.Contains('code=unsupported-pattern') }
    [Kagayoi.R2.Tests.Probe]::Check($invalidPublish, 'publish pattern rejection precedes normalization')
}
$artifact = Publish-R2Artifact @identity -Path 'input/**/*'
[Kagayoi.R2.Tests.Probe]::Check(($artifact.Count -eq 2 -and $artifact.ManifestKey.StartsWith('ci-evidence/')), 'PowerShell artifact result and deps prefix')
[Kagayoi.R2.Tests.Probe]::AssertManifest($artifact.ManifestKey)
$restored = Receive-R2Artifact @identity -Destination 'restored'
[Kagayoi.R2.Tests.Probe]::Check(($restored.Count -eq 2), 'PowerShell Receive-R2Artifact result')
[Kagayoi.R2.Tests.Probe]::Check(($restored.Destination -eq (Join-Path $WorkRoot 'restored')), 'PowerShell current-location receive destination')
[Kagayoi.R2.Tests.Probe]::AssertRestored($restored.Destination)
New-PSDrive -Name R2Fixture -PSProvider FileSystem -Root $WorkRoot -Scope Script | Out-Null
Set-Location R2Fixture:/
$driveSelected = @(Get-R2ArtifactFiles 'input/**/*')
[Kagayoi.R2.Tests.Probe]::Check(($driveSelected.Count -eq 2), 'filesystem PSDrive relative wildcard lookup')
$driveArtifact = Publish-R2Artifact @identity -Path 'input/**/*'
[Kagayoi.R2.Tests.Probe]::Check(($driveArtifact.Count -eq 2), 'filesystem PSDrive relative publish')
$driveRestored = Receive-R2Artifact @identity -Destination 'restored-drive'
[Kagayoi.R2.Tests.Probe]::Check(($driveRestored.Destination -eq (Join-Path $WorkRoot 'restored-drive')), 'filesystem PSDrive relative destination')
[Kagayoi.R2.Tests.Probe]::AssertRestored($driveRestored.Destination)
# Variable providerは全OSで存在する。最後のFS位置へ相対入力を転送しない。
Set-Location Variable:
$requestsBefore = [Kagayoi.R2.Tests.Probe]::Current.Requests.Count
$nonFsRejected = $false
try { Get-R2ArtifactFiles 'input/**/*' | Out-Null } catch { $nonFsRejected = $_.Exception.Message.Contains('code=non-filesystem-location') }
[Kagayoi.R2.Tests.Probe]::Check($nonFsRejected, 'non-filesystem relative lookup rejected')
$nonFsRejected = $false
try { Publish-R2Artifact @identity -Path 'input/**/*' | Out-Null } catch { $nonFsRejected = $_.Exception.Message.Contains('code=non-filesystem-location') }
[Kagayoi.R2.Tests.Probe]::Check($nonFsRejected, 'non-filesystem relative publish rejected')
$nonFsRejected = $false
try { Receive-R2Artifact @identity -Destination 'must-not-create' | Out-Null } catch { $nonFsRejected = $_.Exception.Message.Contains('code=non-filesystem-location') }
[Kagayoi.R2.Tests.Probe]::Check(($nonFsRejected -and ![IO.Directory]::Exists((Join-Path $WorkRoot 'must-not-create'))), 'non-filesystem relative receive rejected before write')
[Kagayoi.R2.Tests.Probe]::Check(([Kagayoi.R2.Tests.Probe]::Current.Requests.Count -eq $requestsBefore), 'non-filesystem relative operations made no requests')
$absoluteSelected = @(Get-R2ArtifactFiles "$inputDirectory/**/*")
[Kagayoi.R2.Tests.Probe]::Check(($absoluteSelected.Count -eq 2), 'absolute filesystem lookup remains valid from other provider')
$absoluteArtifact = Publish-R2Artifact @identity -Path "$inputDirectory/**/*"
[Kagayoi.R2.Tests.Probe]::Check(($absoluteArtifact.Count -eq 2), 'absolute filesystem publish remains valid from other provider')
$absoluteRestored = Receive-R2Artifact @identity -Destination (Join-Path $WorkRoot 'restored-absolute')
[Kagayoi.R2.Tests.Probe]::AssertRestored($absoluteRestored.Destination)
} finally { Set-Location -LiteralPath $savedLocation.Path }
}
# API token を明示した経路では、無関係な AWS 環境変数を採用しない。
[Kagayoi.R2.Tests.Probe]::Reset()
[Kagayoi.R2.Tests.Probe]::Current.ExpectedAccess = '11111111111111111111111111111111'
$savedAccess = $env:AWS_ACCESS_KEY_ID
$savedSecret = $env:AWS_SECRET_ACCESS_KEY
try {
    $env:AWS_ACCESS_KEY_ID = 'UNRELATEDACCESS'
    $env:AWS_SECRET_ACCESS_KEY = 'UNRELATEDSECRET'
    $tokenResult = Publish-R2Object -AccountId $identity.AccountId -Bucket $identity.Bucket -FilePath (Join-Path $inputDirectory 'first.txt') -ApiToken 'dummy-api-token' -Immutable
    [Kagayoi.R2.Tests.Probe]::Check($tokenResult.Changed, 'explicit API token wins over unrelated AWS environment')
} finally { $env:AWS_ACCESS_KEY_ID = $savedAccess; $env:AWS_SECRET_ACCESS_KEY = $savedSecret }
[Kagayoi.R2.Tests.Probe]::Reset()
$partialRejected = $false
try { Publish-R2Object -AccountId $identity.AccountId -Bucket $identity.Bucket -FilePath (Join-Path $inputDirectory 'first.txt') -AccessKeyId $identity.AccessKeyId | Out-Null }
catch { $partialRejected = $_.Exception.Message.Contains('code=invalid-input') }
[Kagayoi.R2.Tests.Probe]::Check(($partialRejected -and [Kagayoi.R2.Tests.Probe]::Current.Requests.Count -eq 0), 'partial explicit credentials rejected before network')
# リンクの祖先も拒否する。リンクは利用者が指定した検証出力内にのみ作成し、追跡して残す。
$link = Join-Path $WorkRoot 'linked-input'
$linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
New-Item -ItemType $linkType -Path $link -Target $inputDirectory | Out-Null
$linkRejected = $false
try {
    if ($hasArtifact) { Assert-R2ArtifactNoLinks (Join-Path $link 'first.txt') }
    else { [Kagayoi.R2.Transfer]::AssertNoLinks((Join-Path $link 'first.txt')) }
} catch { $linkRejected = $_.Exception.Message.Contains('code=link-rejected') }
[Kagayoi.R2.Tests.Probe]::Check($linkRejected, 'linked ancestor rejected')
# 版が異なる同名型は再利用しない。実型はそのまま、異なるsourceを持つコピーの入口で拒否を検証。
$conflict = Join-Path $WorkRoot 'conflict'
[void][IO.Directory]::CreateDirectory($conflict)
Copy-Item -LiteralPath (Join-Path $helpers 'R2Distribution.Common.ps1') -Destination $conflict
[IO.File]::WriteAllText((Join-Path $conflict 'R2Transfer.Core.cs'), ([IO.File]::ReadAllText($core) + [Environment]::NewLine + "// distinct implementation"))
$rejected = $false
try { . (Join-Path $conflict 'R2Distribution.Common.ps1') } catch { $rejected = $_.Exception.Message.Contains('code=core-version-conflict') }
[Kagayoi.R2.Tests.Probe]::Check($rejected, 'different loaded implementation rejected')
"PowerShell public entry / repeat import / version conflict: PASS; total checks=$([Kagayoi.R2.Tests.Probe]::Checks)"
} finally { $networkGuard.Dispose() }
