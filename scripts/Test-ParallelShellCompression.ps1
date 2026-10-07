param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [int]$TimeoutSeconds = 90,
    [ValidateSet('Automatic', 'Explicit', 'FormatOnly', 'Shortcut', 'AutomaticShortcut')][string]$FirstRoute = 'Explicit',
    [ValidateSet('Automatic', 'Explicit', 'FormatOnly', 'Shortcut', 'AutomaticShortcut')][string]$SecondRoute = 'Explicit'
)

$ErrorActionPreference = 'Stop'
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $ArtifactDirectory) { throw '未使用の成果物ディレクトリを指定してください。' }
if (Get-Process Lhamiel -ErrorAction SilentlyContinue) { throw '既存の Lhamiel を閉じてから実行してください。' }
$settings = Get-Content -LiteralPath "$env:LOCALAPPDATA\Lhamiel\settings.json" -Raw | ConvertFrom-Json
if (-not $settings.CompressionOutputToSameDirectory -or $settings.IsPasswordProtectionEnabled) {
    throw '元と同じ場所への保存とパスワードなしの設定が必要です。設定は変更しません。'
}
if (($FirstRoute -in @('Automatic', 'Shortcut', 'AutomaticShortcut') -or $SecondRoute -in @('Automatic', 'Shortcut', 'AutomaticShortcut')) -and
    $settings.CompressionFormat -ne 'ZIP') {
    throw '自動判定・圧縮ショートカットの検証には保存済みの圧縮形式 ZIP が必要です。設定は変更しません。'
}
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
$dataDirectory = Join-Path $ArtifactDirectory 'data'
New-Item -ItemType Directory -Path $dataDirectory | Out-Null
$started = Get-Date
$result = [ordered]@{
    started = $started.ToString('o')
    appPath = $AppPath
    assemblySha256 = if (Test-Path ([IO.Path]::ChangeExtension($AppPath, '.dll'))) {
        (Get-FileHash ([IO.Path]::ChangeExtension($AppPath, '.dll'))).Hash
    } else { (Get-FileHash $AppPath).Hash }
    dotnet = (& dotnet --version)
    passed = $false
    firstRoute = $FirstRoute
    secondRoute = $SecondRoute
    requests = @()
    compressors = @()
    concurrentProcessIds = @()
    secondCompletedBeforeFirst = $false
    firstCompressorAliveWhenSecondCompleted = $false
    compressionOverlapObserved = $false
    passwordNotificationAccepted = $false
    selectionConsumed = $false
    allProcessesSelfExited = $false
    archives = @()
}
$firstEvidence = $null
$secondEvidence = $null
$observedProcesses = @{}

function Start-Request([string[]]$Arguments) {
    $launch = [Diagnostics.ProcessStartInfo]::new($AppPath)
    $launch.UseShellExecute = $false
    foreach ($argument in $Arguments) { $launch.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($launch)
}

function Start-RouteRequest([string]$Route, [string]$Source, [string]$Name) {
    $arguments = switch ($Route) {
        'Automatic' { @($Source) }
        'Explicit' { @('--compress', '--format', 'ZIP', $Source) }
        'FormatOnly' { @('--format', 'ZIP', $Source) }
        'Shortcut' { @('--compress', $Source) }
        'AutomaticShortcut' { @($Source) }
    }
    $isShortcut = $Route -in @('Shortcut', 'AutomaticShortcut')
    $shortcutPath = Join-Path $ArtifactDirectory "$Route.lnk"
    if ($isShortcut) {
        if (-not (Test-Path -LiteralPath $shortcutPath)) {
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $null
            try {
                $shortcut = $shell.CreateShortcut($shortcutPath)
                $shortcut.TargetPath = $AppPath
                $shortcut.Arguments = if ($Route -eq 'Shortcut') { '--compress' } else { '' }
                $shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($AppPath)
                $shortcut.Save()
            } finally {
                if ($shortcut) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null }
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
            }
        }
        # 保存済み引数へ入力パスを追加する、実際の .lnk のシェル起動経路。
        $launch = [Diagnostics.ProcessStartInfo]::new($shortcutPath)
        $launch.UseShellExecute = $true
        $launch.Arguments = '"' + $Source + '"'
        $process = [Diagnostics.Process]::Start($launch)
    } else {
        $process = Start-Request $arguments
    }
    $result.requests += [ordered]@{
        name = $Name; route = $Route; arguments = @($arguments)
        processId = if ($process) { $process.Id } else { $null }
        shortcutPath = if ($isShortcut) { $shortcutPath } else { $null }
        launchedThroughShortcut = $isShortcut
        processIdUnavailableReason = if (-not $process) { 'ShellExecute がプロセスハンドルを返しませんでした。' } else { $null }
    }
    if ($process) { $observedProcesses[$process.Id] = $process }
    return $process
}

function Find-Compressor([string]$OutputPath, [Diagnostics.Process]$RequestProcess) {
    foreach ($candidate in Get-ChildItem "$env:LOCALAPPDATA\Lhamiel" -Filter 'Lhamiel_compression_*.log') {
        if ((Read-Log $candidate.FullName).Contains("outputPath=$OutputPath")) {
            $match = [Regex]::Match($candidate.Name, '^Lhamiel_compression_(\d+)_')
            if (-not $match.Success) { continue }
            $processId = [int]$match.Groups[1].Value
            $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if (-not $process -or $process.Path -ne $AppPath -or $process.StartTime -lt $started) { continue }
            return @{ ProcessId = $processId; Process = $process; ProcessPath = $process.Path; LogPath = $candidate.FullName; Identification = 'worker-log' }
        }
    }
    # v1.1.4 の初回要求は worker を作らず、起動要求自身で圧縮する。
    if ($RequestProcess -and -not $RequestProcess.HasExited -and
        (Read-Log $mainLogPath).Contains("outputPath=$OutputPath")) {
        $process = Get-Process -Id $RequestProcess.Id -ErrorAction SilentlyContinue
        if ($process -and $process.Path -eq $AppPath) {
            return @{ ProcessId = $process.Id; Process = $RequestProcess; ProcessPath = $process.Path; LogPath = $mainLogPath; Identification = 'request-main-log' }
        }
    }
    return $null
}

function Save-CompressorEvidence([string]$Name, [hashtable]$Evidence) {
    # 終了後も実 worker の終了コードを読めるよう、生存中にハンドルを保持する。
    $null = $Evidence.Process.Handle
    $observedProcesses[$Evidence.ProcessId] = $Evidence.Process
    $result.compressors += [ordered]@{
        name = $Name; processId = $Evidence.ProcessId; processPath = $Evidence.ProcessPath
        logPath = $Evidence.LogPath; identification = $Evidence.Identification
        startObservedAt = (Get-Date).ToString('o')
    }
}

function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (& $Condition)) {
        if ((Get-Date) -gt $deadline) { throw $Failure }
        Start-Sleep -Milliseconds 50
    }
}

function Read-Log([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

try {
    $buffer = [byte[]]::new(1MB)
    [Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    foreach ($payloadSpec in @(@{ Name = 'first.bin'; Size = 512 }, @{ Name = 'second.bin'; Size = 64 })) {
        $stream = [IO.File]::Create((Join-Path $dataDirectory $payloadSpec.Name))
        try { 1..$payloadSpec.Size | ForEach-Object { $stream.Write($buffer) } } finally { $stream.Dispose() }
    }
    $mainLogPath = Join-Path "$env:LOCALAPPDATA\Lhamiel" ("Lhamiel_{0:yyyyMMdd}.log" -f (Get-Date))
    $firstOutput = Join-Path $dataDirectory 'first.zip'
    $secondOutput = Join-Path $dataDirectory 'second.zip'
    $first = Start-RouteRequest $FirstRoute (Join-Path $dataDirectory 'first.bin') 'first'
    $result.firstProcessId = if ($first) { $first.Id } else { $null }
    $result.firstRequestProcessId = $result.firstProcessId
    Wait-Until {
        $script:firstEvidence = Find-Compressor $firstOutput $first
        return $null -ne $script:firstEvidence
    } '先行圧縮の開始と実プロセスを観測できませんでした。'
    Save-CompressorEvidence 'first' $firstEvidence
    $result.firstCompressorProcessId = $firstEvidence.ProcessId
    $sender = Start-RouteRequest $SecondRoute (Join-Path $dataDirectory 'second.bin') 'second'
    $result.secondRequestProcessId = if ($sender) { $sender.Id } else { $null }
    Wait-Until {
        $script:secondEvidence = Find-Compressor $secondOutput $sender
        return $null -ne $script:secondEvidence
    } '2件目の圧縮の開始と実プロセスを観測できませんでした。'
    Save-CompressorEvidence 'second' $secondEvidence
    $result.secondCompressorProcessId = $secondEvidence.ProcessId
    if ($secondEvidence.ProcessId -eq $firstEvidence.ProcessId) { throw '追加圧縮が同一プロセスで実行されました。' }
    $result.concurrentProcessIds = @($firstEvidence.ProcessId, $secondEvidence.ProcessId)
    $result.compressionOverlapObserved = -not $firstEvidence.Process.HasExited -and
        -not $secondEvidence.Process.HasExited -and
        -not (Read-Log $firstEvidence.LogPath).Contains("圧縮完了: $firstOutput")
    $result.overlapObservedAt = (Get-Date).ToString('o')
    if (-not $result.compressionOverlapObserved) { throw '実際の圧縮処理の重なりを観測できませんでした。' }
    Wait-Until {
        (Read-Log $secondEvidence.LogPath).Contains("圧縮完了: $secondOutput")
    } '2件目の圧縮完了を観測できませんでした。'
    $result.secondCompletionObservedAt = (Get-Date).ToString('o')
    $result.firstCompressorAliveWhenSecondCompleted = -not $firstEvidence.Process.HasExited
    $result.secondCompletedBeforeFirst = $result.firstCompressorAliveWhenSecondCompleted -and
        -not (Read-Log $firstEvidence.LogPath).Contains("圧縮完了: $firstOutput")
    if (-not $result.secondCompletedBeforeFirst) { throw '先行処理の終了後まで2件目が待機していました。' }

    $notification = Start-Request @('--saved-compression-password-changed')
    $observedProcesses[$notification.Id] = $notification
    $result.passwordNotificationProcessId = $notification.Id
    Wait-Until { $notification.HasExited } '保存状態の通知が完了しませんでした。'
    if ($notification.ExitCode -ne 0) { throw '保存状態の通知が異常終了しました。' }
    $result.passwordNotificationAccepted = $true

    # IPC がトークンを消費せず、独立 worker が一度だけ読み込むことも検証する。
    Set-Content (Join-Path $dataDirectory 'selection-a.txt') 'Selection A 日本語' -Encoding utf8
    Set-Content (Join-Path $dataDirectory 'selection-b.txt') 'Selection B' -Encoding utf8
    $token = [Guid]::NewGuid().ToString('N')
    $tokenPath = Join-Path ([IO.Path]::GetTempPath()) "Lhamiel-selection-$token.bin"
    $paths = @((Join-Path $dataDirectory 'selection-a.txt'), (Join-Path $dataDirectory 'selection-b.txt'))
    [IO.File]::WriteAllText($tokenPath, (($paths -join "`0") + "`0"), [Text.UnicodeEncoding]::new($false, $false))
    $selectionSender = Start-Request @('--compress', '--format', 'ZIP', '--shell-selection', $token)
    $observedProcesses[$selectionSender.Id] = $selectionSender
    $result.selectionToken = $token
    $result.selectionTokenPath = $tokenPath
    $result.selectionRequestProcessId = $selectionSender.Id
    Wait-Until { -not (Test-Path -LiteralPath $tokenPath) } '選択リストが worker に回収されませんでした。'
    $result.selectionConsumed = $true
    Wait-Until { -not (Get-Process Lhamiel -ErrorAction SilentlyContinue | Where-Object Path -eq $AppPath) } '処理プロセスが自己終了しませんでした。'
    $result.processExitCodes = @($observedProcesses.Values | ForEach-Object {
        if (-not $_.HasExited) { throw '観測済みプロセスが自己終了しませんでした。' }
        @{ processId = $_.Id; exitCode = $_.ExitCode }
    })
    $result.allProcessesSelfExited = $true
    if ($result.processExitCodes | Where-Object exitCode -ne 0) { throw '異常終了しました。' }
    if (-not (Read-Log $firstEvidence.LogPath).Contains("圧縮完了: $firstOutput")) { throw '先行圧縮の完了ログがありません。' }

    $zipNames = @('first.zip', 'second.zip', 'selection-a.zip')
    if (-not $settings.CompressMultipleAsOne) { $zipNames += 'selection-b.zip' }
    foreach ($zipName in $zipNames) {
        $zipPath = Join-Path $dataDirectory $zipName
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $expectedCount = if ($zipName -eq 'selection-a.zip' -and $settings.CompressMultipleAsOne) { 2 } else { 1 }
            if ($archive.Entries.Count -ne $expectedCount) { throw "ZIPの項目数が不正です: $zipName" }
            foreach ($entry in $archive.Entries) {
                $source = Join-Path $dataDirectory $entry.FullName
                $stream = $entry.Open()
                try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
                if ($actual -ne (Get-FileHash -LiteralPath $source).Hash) { throw "ZIP内容が一致しません: $zipName" }
                $result.archives += @{ zip = $zipName; entry = $entry.FullName; sha256 = $actual; length = $entry.Length }
            }
        } finally { $archive.Dispose() }
    }
    $result.passed = $true
}
catch { $result.error = $_.Exception.Message; throw }
finally {
    $result.finished = (Get-Date).ToString('o')
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ArtifactDirectory 'result.json') -Encoding utf8
    if ($firstEvidence) { Copy-Item $firstEvidence.LogPath (Join-Path $ArtifactDirectory 'first-compressor.log') }
    if ($secondEvidence) { Copy-Item $secondEvidence.LogPath (Join-Path $ArtifactDirectory 'worker.log') }
    if ($mainLogPath) { (Read-Log $mainLogPath) -split "`n" | Where-Object { $_ -match [Regex]::Escape($dataDirectory) -or $_ -match '別プロセスで圧縮' } | Set-Content (Join-Path $ArtifactDirectory 'main.log') }
    # 失敗時のプロセスと入力は再現用に保持し、清掃は呼び出し側が行う。
}
Write-Output "PASS: $ArtifactDirectory\result.json"
