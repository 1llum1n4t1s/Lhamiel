param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [int]$TimeoutSeconds = 90
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
    concurrentProcessIds = @()
    secondCompletedBeforeFirst = $false
    archives = @()
}

function Start-Request([string[]]$Arguments) {
    $launch = [Diagnostics.ProcessStartInfo]::new($AppPath)
    $launch.UseShellExecute = $false
    foreach ($argument in $Arguments) { $launch.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($launch)
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
    $first = Start-Request @('--compress', '--format', 'ZIP', (Join-Path $dataDirectory 'first.bin'))
    $result.firstProcessId = $first.Id
    Wait-Until { (Read-Log $mainLogPath).Contains("outputPath=$dataDirectory\first.zip") } '先行圧縮が開始しませんでした。'
    $sender = Start-Request @('--compress', '--format', 'ZIP', (Join-Path $dataDirectory 'second.bin'))
    Wait-Until {
        $running = @(Get-Process Lhamiel -ErrorAction SilentlyContinue | Where-Object Path -eq $AppPath)
        if ($running.Count -ge 2) { $result.concurrentProcessIds = @($running.Id); return $true }
        return $false
    } '異なるプロセスでの並行処理を観測できませんでした。'
    Wait-Until { Test-Path (Join-Path $dataDirectory 'second.zip') } '2件目の圧縮が開始しませんでした。'
    $workerLogPath = $null
    Wait-Until {
        foreach ($candidate in Get-ChildItem "$env:LOCALAPPDATA\Lhamiel" -Filter 'Lhamiel_compression_*.log') {
            $text = Read-Log $candidate.FullName
            if ($text.Contains("圧縮完了: $dataDirectory\second.zip")) {
                $script:workerLogPath = $candidate.FullName
                return $true
            }
        }
        return $false
    } '2件目の圧縮完了を観測できませんでした。'
    $workerId = [int]([Regex]::Match([IO.Path]::GetFileName($workerLogPath), 'Lhamiel_compression_(\d+)_').Groups[1].Value)
    if ($workerId -eq $first.Id) { throw '追加圧縮が同一プロセスで実行されました。' }
    $result.concurrentProcessIds = @($first.Id, $workerId)
    $result.secondCompletedBeforeFirst = -not $first.HasExited
    if (-not $result.secondCompletedBeforeFirst) { throw '先行処理の終了後まで2件目が待機していました。' }

    $notification = Start-Request @('--saved-compression-password-changed')
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
    Wait-Until { -not (Test-Path -LiteralPath $tokenPath) } '選択リストが worker に回収されませんでした。'
    $result.selectionConsumed = $true
    Wait-Until { -not (Get-Process Lhamiel -ErrorAction SilentlyContinue | Where-Object Path -eq $AppPath) } '処理プロセスが自己終了しませんでした。'
    if ($first.ExitCode -ne 0 -or $sender.ExitCode -ne 0 -or $selectionSender.ExitCode -ne 0) { throw '異常終了しました。' }

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
    if ($workerLogPath) { Copy-Item $workerLogPath (Join-Path $ArtifactDirectory 'worker.log') }
    if ($mainLogPath) { (Read-Log $mainLogPath) -split "`n" | Where-Object { $_ -match [Regex]::Escape($dataDirectory) -or $_ -match '別プロセスで圧縮' } | Set-Content (Join-Path $ArtifactDirectory 'main.log') }
    # 失敗時のプロセスと入力は再現用に保持し、清掃は呼び出し側が行う。
}
Write-Output "PASS: $ArtifactDirectory\result.json"
