param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidateSet('Explicit','Automatic','Shortcut','AutomaticShortcut','Ui','UiBatch','Batch','Snapshot')][string]$Mode = 'Explicit',
    [string]$ProbePath = "$PSScriptRoot\ExtractionE2E\bin\e2e\Lhamiel.Tests.Unit.exe",
    [int]$TimeoutSeconds = 180,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$ProbePath = (Resolve-Path -LiteralPath $ProbePath).Path
function Assert-NoActiveProduct {
    $active = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('Lhamiel.exe', 'Lhamiel.Tests.Unit.exe') })
    if ($active.Count -gt 0) { throw "別の製品/probe が稼働中です。実 IPC の混線を避けるため終了後に逐次実行してください。PID: $($active.ProcessId -join ', ')" }
}
if (-not $VerifyOnly) { Assert-NoActiveProduct }
if ((Test-Path -LiteralPath $ArtifactDirectory) -and -not $VerifyOnly) { throw '未使用の成果物ディレクトリを指定してください。' }
if (-not $VerifyOnly) { New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null }
$env:LHAMIEL_EXTRACTION_TEST_ROOT = $ArtifactDirectory
function Start-Probe([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($ProbePath)
    $info.UseShellExecute = $false
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    [Diagnostics.Process]::Start($info)
}
function Read-Shared([string]$Path) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try { $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}
function Start-Route([string]$Source) {
    switch ($Mode) {
        'Explicit' { Start-Probe @('--extract', $Source) }
        'Automatic' { Start-Probe @($Source) }
        default {
            $link = Join-Path $ArtifactDirectory "$Mode.lnk"
            if (-not (Test-Path -LiteralPath $link)) {
                $shell = New-Object -ComObject WScript.Shell
                $shortcut = $shell.CreateShortcut($link)
                $shortcut.TargetPath = $ProbePath
                $shortcut.Arguments = if ($Mode -eq 'Shortcut') { '--extract' } else { '' }
                $shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($ProbePath)
                $shortcut.Save()
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
            }
            $info = [Diagnostics.ProcessStartInfo]::new($link)
            $info.UseShellExecute = $true
            $info.Arguments = '"' + $Source + '"'
            [Diagnostics.Process]::Start($info)
        }
    }
}
if (-not $VerifyOnly) {
$fixtureArgument = if ($Mode -in @('Batch','UiBatch')) { '--fixture-batch' } else { '--fixture' }
$fixture = Start-Probe @($fixtureArgument)
$fixture.WaitForExit()
if ($fixture.ExitCode -ne 0) { throw 'Fixture creation failed.' }
Assert-NoActiveProduct
$fixtureStarted = Get-Date
$until = (Get-Date).AddSeconds($TimeoutSeconds)
$first = Join-Path $ArtifactDirectory 'first.zip'
$second = Join-Path $ArtifactDirectory 'second.zip'
$launches = @()
if ($Mode -eq 'Ui') { $launches += Start-Probe @('--ui') }
elseif ($Mode -eq 'UiBatch') { $launches += Start-Probe @('--ui-batch') }
elseif ($Mode -eq 'Snapshot') { $launches += Start-Probe @('--snapshot') }
elseif ($Mode -eq 'Batch') { $launches += Start-Probe @('--extract', $first, $second) }
else {
    $launches += Start-Route $first
    $firstStarted = $false
    while ((Get-Date) -lt $until) {
        foreach ($log in Get-ChildItem (Join-Path $ArtifactDirectory 'settings') -Filter '*extraction*.log') {
            if ((Read-Shared $log.FullName).Contains('一時ディレクトリへの展開処理開始')) { $firstStarted = $true; break }
        }
        if ($firstStarted) { break }
        Start-Sleep -Milliseconds 40
    }
    if (-not $firstStarted) { throw 'First extraction did not reach native Save.' }
    $launches += Start-Route $second
}
while ((Get-Date) -lt $until) {
    if ((@(Get-ChildItem $ArtifactDirectory -Filter 'worker-*-returned.json')).Count -eq 2) { break }
    if (Test-Path (Join-Path $ArtifactDirectory 'probe-failed.json')) { break }
    Start-Sleep -Milliseconds 100
}
} else {
    $fixtureStarted = (Get-Item (Join-Path $ArtifactDirectory 'fixture-result.json')).CreationTime
    $first = Join-Path $ArtifactDirectory 'first.zip'
    $second = Join-Path $ArtifactDirectory 'second.zip'
    $launches = @()
}
$workers = @(Get-ChildItem $ArtifactDirectory -Filter 'worker-*-started.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
$returns = @(Get-ChildItem $ArtifactDirectory -Filter 'worker-*-returned.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
$firstWorker = $workers | Where-Object { $_.SourcePaths -contains $first } | Select-Object -First 1
$secondWorker = $workers | Where-Object { $_.SourcePaths -contains $second } | Select-Object -First 1
$firstReturn = $returns | Where-Object pid -eq $firstWorker.pid | Select-Object -First 1
$secondReturn = $returns | Where-Object pid -eq $secondWorker.pid | Select-Object -First 1
$secondLog = if ($secondWorker) { Get-ChildItem (Join-Path $ArtifactDirectory 'settings') -Filter "*extraction_$($secondWorker.pid)_*.log" | Select-Object -First 1 }
$firstLog = if ($firstWorker) { Get-ChildItem (Join-Path $ArtifactDirectory 'settings') -Filter "*extraction_$($firstWorker.pid)_*.log" | Select-Object -First 1 }
$nativeStart = if ($secondLog) { (Read-Shared $secondLog.FullName) -split "`n" | Where-Object { $_.Contains('一時ディレクトリへの展開処理開始') } | Select-Object -First 1 }
$firstNativeEnd = if ($firstLog) { (Read-Shared $firstLog.FullName) -split "`n" | Where-Object { $_.Contains('一時ディレクトリから最終展開先へ移動します') } | Select-Object -First 1 }
$firstNativeStart = if ($firstLog) { (Read-Shared $firstLog.FullName) -split "`n" | Where-Object { $_.Contains('一時ディレクトリへの展開処理開始') } | Select-Object -First 1 }
$secondNativeEnd = if ($secondLog) { (Read-Shared $secondLog.FullName) -split "`n" | Where-Object { $_.Contains('一時ディレクトリから最終展開先へ移動します') } | Select-Object -First 1 }
$overlap = $false
$secondNativeStartedAt = $null
if ($nativeStart -match '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)' -and $firstReturn) {
    $secondNativeStartedAt = [datetime]::Parse($Matches.time)
    $overlap = $secondNativeStartedAt -lt ([datetimeoffset]::Parse($firstReturn.returnedAt)).LocalDateTime
}
$nativeIntervalOverlap = $false
if ($firstNativeEnd -match '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)' -and $secondNativeStartedAt) {
    $nativeIntervalOverlap = $secondNativeStartedAt -lt [datetime]::Parse($Matches.time)
}
if ($firstNativeStart -match '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)') {
    $firstNativeStartedAt = [datetime]::Parse($Matches.time)
    if ($secondNativeEnd -match '^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)') {
        $nativeIntervalOverlap = $nativeIntervalOverlap -and $firstNativeStartedAt -lt [datetime]::Parse($Matches.time)
    } else { $nativeIntervalOverlap = $false }
} else { $nativeIntervalOverlap = $false }
$postprocessingOverlap = $false
if ($firstNativeEnd -and $secondNativeEnd -and $firstReturn -and $secondReturn) {
    $firstPostStart = [datetime]::Parse($firstNativeEnd.Substring(0,24))
    $secondPostStart = [datetime]::Parse($secondNativeEnd.Substring(0,24))
    $firstPostEnd = ([datetimeoffset]::Parse($firstReturn.returnedAt)).LocalDateTime
    $secondPostEnd = ([datetimeoffset]::Parse($secondReturn.returnedAt)).LocalDateTime
    $postprocessingOverlap = $firstPostStart -lt $secondPostEnd -and $secondPostStart -lt $firstPostEnd
}
$verification = [Collections.Generic.List[object]]::new()
$manifest = Get-Content (Join-Path $ArtifactDirectory 'manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $outputRoot = if ($Mode -eq 'Snapshot') { Join-Path $ArtifactDirectory "snapshot-$($entry.archive)" } else { Join-Path $ArtifactDirectory 'output' }
    $path = Join-Path (Join-Path $outputRoot $entry.archive) $entry.path
    $exists = Test-Path -LiteralPath $path
    $actualSha = if ($exists) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { $null }
    $verification.Add([pscustomobject]@{ path = $path; expectedLength = $entry.length; expectedSha256 = $entry.sha256; actualSha256 = $actualSha; passed = $exists -and $actualSha -eq $entry.sha256 })
}
$verification | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ArtifactDirectory 'content-verification.json') -Encoding utf8
$selfExited = $workers.Count -eq 2
foreach ($worker in $workers) { if (Get-Process -Id $worker.pid -ErrorAction SilentlyContinue) { $selfExited = $false } }
$launchExited = $true
foreach ($launch in $launches) { if ($launch -and -not $launch.WaitForExit(10000)) { $launchExited = $false } }
$allTokensConsumed = $workers.Count -eq 2 -and @($workers | Where-Object { -not $_.tokenConsumed -or (Test-Path -LiteralPath $_.tokenPath) }).Count -eq 0
$contentPassed = @($verification | Where-Object { -not $_.passed }).Count -eq 0
$allWorkersSucceeded = $returns.Count -eq 2 -and @($returns | Where-Object exitCode -ne 0).Count -eq 0
$settingsSnapshotPassed = $workers.Count -eq 2
foreach ($worker in $workers) {
    $sourceBase = [IO.Path]::GetFileNameWithoutExtension($worker.SourcePaths[0])
    $expectedOutput = if ($Mode -eq 'Snapshot') { Join-Path $ArtifactDirectory "snapshot-$sourceBase" } else { Join-Path $ArtifactDirectory 'output' }
    if (-not $worker.IsExtraction -or $worker.ExtractionOutputDirectory -ne $expectedOutput -or $worker.ExtractionOutputToSameDirectory -or -not $worker.CreateArchiveNameFolder) { $settingsSnapshotPassed = $false }
}
$result = [ordered]@{
    passed = $overlap -and $nativeIntervalOverlap -and $contentPassed -and $selfExited -and $launchExited -and $allTokensConsumed -and $allWorkersSucceeded -and $settingsSnapshotPassed
    mode = $Mode; startedAt = $fixtureStarted.ToString('o'); completedAt = (Get-Date).ToString('o')
    productSha256 = (Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($ProbePath)) 'Lhamiel.dll')).Hash
    firstWorker = $firstWorker; secondWorker = $secondWorker; secondNativeSaveStartLog = $nativeStart
    firstNativeSaveStartLog = $firstNativeStart; firstNativeSaveEndLog = $firstNativeEnd; secondNativeSaveEndLog = $secondNativeEnd; firstReturned = $firstReturn; nativeSaveOverlapObserved = $nativeIntervalOverlap; additionalStartedBeforeFirstExit = $overlap; workersSelfExited = $selfExited
    launchersSelfExited = $launchExited; requestTokensConsumed = $allTokensConsumed
    workerProcessesOverlap = $firstWorker -and $secondWorker -and $firstReturn -and ([datetimeoffset]::Parse($secondWorker.observedAt) -lt [datetimeoffset]::Parse($firstReturn.returnedAt))
    postprocessingOverlapObserved = $postprocessingOverlap
    nativeIntervalDefinition = 'PID log: temporary extraction start through final move start after native reader.Save/Dispose.'
    postprocessingIntervalDefinition = 'Final move start through worker Program.Main return.'
    allWorkersExitCodeZero = $allWorkersSucceeded; settingsSnapshotPassed = $settingsSnapshotPassed
    verifiedEntryCount = $verification.Count; allContentSha256Passed = $contentPassed
    crcVerification = 'Product extraction success validates each ZIP entry CRC; SHA256 independently checked for every output.'
    fixtureIsolation = 'UnsafeAccessor Settings paths applied before actual Program.Main in parent and child.'
    temporaryArtifactsRetained = $ArtifactDirectory
}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ArtifactDirectory 'result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 8
if (-not $result.passed) { throw 'E2E failed; inspect result.json and retained logs.' }
