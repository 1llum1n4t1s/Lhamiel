# PR #59 の CodeRabbit/codex inline コメント 27 件に一括 reply + thread resolve
# 個別の修正内容を 1 行ずつ書き込み、対応済みスレッドをクローズする。

$ErrorActionPreference = 'Stop'

$repo = '1llum1n4t1s/Lhamiel'
$pr = 59

# comment_id → 1 行 reply
$replies = [ordered]@{
    # codex P1/P2 (first batch, 6 件)
    3381085172 = '✅ 対応 (commit 4492703): 上書き削除より前に `TryResolveCompressionPasswordAsync` を呼ぶよう `CompressItemAsync` / `CompressMergedAsync` を修正。キャンセル時に元ファイルを失わない。'
    3381085177 = '✅ 対応 (commit 366633a): `TryResolveCompressionPasswordAsync` に `formatHint` 引数を追加し、TAR では即 password=null で短絡。UI 側 disable に加え CLI/コンテキストメニュー経路でも防御。`#3381313186` と同根なので同じ修正で解消。'
    3381085181 = '✅ 対応 (commit 4492703): `ProcessDroppedPathsAsync` で `SettingsManager.Mutate` により 3 フィールド一括スナップショット同期 (`IsPasswordProtectionEnabled` / `PasswordMode` / `EncryptFileNames`)。圧縮実行中の VM 変更は反映されない。'
    3381085184 = '✅ 対応 (commit 4492703): `MainWindow` で `viewModel.PropertyChanged` を購読し `PasswordMode` 変化時に `InitPasswordModeRadioButtons` を再実行。`#3381138457` と同じ。'
    3381085189 = '✅ 対応 (commit 4492703): `Logger.LogException` でも redaction token を適用するよう修正。例外メッセージ中の平文パスワードもマスクされる。'
    3381085196 = '✅ 対応 (commit 4492703): `ConcurrentDictionary<string,int>` に変更し `AddOrUpdate` / CAS ループで refcount 管理。同一パスワードを batch で再利用するケースでも race 無し。'

    # CodeRabbit ロケール 12 件 (zh_CN, zh_TW, ko_KR, de_DE, fr_FR, es_ES, pt_BR, ru_RU, it_IT, uk_UA, id_ID, ta_IN, sa_IN, la_VA, fil_PH のうち API に出ている 12 件 + 残りも同根)
    3381138324 = '✅ 対応 (commit 0bf2204): `scripts/translate-locales.ps1` で de_DE をドイツ語にネイティブ翻訳。29 キー全て翻訳済み。'
    3381138335 = '✅ 対応 (commit 0bf2204): es_ES をスペイン語にネイティブ翻訳。'
    3381138340 = '✅ 対応 (commit 0bf2204): fil_PH をフィリピノ語にネイティブ翻訳。'
    3381138347 = '✅ 対応 (commit 0bf2204): fr_FR をフランス語にネイティブ翻訳。'
    3381138352 = '✅ 対応 (commit 0bf2204): id_ID をインドネシア語にネイティブ翻訳。'
    3381138355 = '✅ 対応 (commit 0bf2204): it_IT をイタリア語にネイティブ翻訳。'
    3381138361 = '✅ 対応 (commit 0bf2204): ko_KR を韓国語にネイティブ翻訳。'
    3381138369 = '✅ 対応 (commit 0bf2204): la_VA をラテン語にネイティブ翻訳。'
    3381138374 = '✅ 対応 (commit 0bf2204): pt_BR をブラジルポルトガル語にネイティブ翻訳。'
    3381138378 = '✅ 対応 (commit 0bf2204): ru_RU をロシア語にネイティブ翻訳。'
    3381138385 = '✅ 対応 (commit 0bf2204): sa_IN をサンスクリット語にネイティブ翻訳。'
    3381138390 = '✅ 対応 (commit 0bf2204): ta_IN をタミル語にネイティブ翻訳。'

    # CodeRabbit P0/P1/P2 コード指摘
    3381138394 = '✅ 対応 (commit 4492703): `ArchiveCompressor` で `if (addedCount == 0)` に変更し、`ScanSourceFiles` の結果ゼロ / 全除外 / 全アクセス不能を統一して `InvalidOperationException` で中止。テスト 2 件 (`AllFilesDeleted` / `EmptyDirectoryOnly`) を新仕様に追従させた (commit 0bf2204)。'
    3381138424 = '✅ 対応 (commit 4492703): `PasswordResolutionState` を `IDisposable` + `RedactionScope` で囲み、`Logger.RegisterRedactionToken` をパスワード解決直後に発火。`using` で確実に解放。`CompressItemAsync` / `CompressMergedAsync` / バッチ全経路で平文ログ漏洩を防止。'
    3381138436 = '✅ 対応 (commit 4492703): `CompressMergedAsync` で `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressWindow?.GetCancellationToken())` により外部 CT と ProgressWindow CT を統合した `actualCancellationToken` を使用。'
    3381138447 = '✅ 対応 (commit 4492703): `Settings.ResetToDefaults` で `EncryptFileNames = true` を明示。'
    3381138457 = '✅ 対応 (commit 4492703): `MainWindow` コンストラクタで `viewModel.PropertyChanged` を購読し、`PasswordMode` 変化時に `Dispatcher.UIThread.Post` 経由で `InitPasswordModeRadioButtons` を再実行。wipe キャンセル時の VM 巻き戻しでも radio が追従。'
    3381138460 = '✅ 対応 (commit 4492703): `PasswordDialog.axaml.cs:111` で `"Text.Password.SetTitle"` → `"Password.SetTitle"` に変更。`App.Text` の自動プレフィックスと二重指定を解消。'
    3381138482 = '✅ 対応 (commit 4492703): `MainWindowViewModel.ChangeSavedPasswordAsync` で `using var _ = Logger.RegisterRedactionToken(newPassword)` を追加。保存パスワード更新時の平文も後続ログでマスクされる。'

    # codex P2 second batch
    3381313186 = '✅ 対応 (commit 366633a): `TryResolveCompressionPasswordAsync` に `formatHint` 引数を追加し TAR では password=null で短絡。`App.axaml.cs` 経由の CLI/コンテキストメニュー圧縮でも `CreateArchiveWriter` が reject する前に弾く。'
    3381313190 = '✅ 対応 (commit 366633a): `Settings.SanitizeAfterLoad` の Remember + ciphertext 無し → PromptEachTime degrade を撤去。`TryResolveCompressionPasswordAsync` が null ciphertext を「初回プロンプト → 保存」として扱うので Remember 選好を保持する。テストも `PreservesRemember` に更新。'
}

# Step 1: 各 comment に reply を投稿
$replied = @{}
foreach ($commentId in $replies.Keys) {
    $body = $replies[$commentId]
    Write-Host "→ reply to comment $commentId"
    try {
        $result = gh api "repos/$repo/pulls/$pr/comments/$commentId/replies" `
            -X POST `
            -f body=$body `
            --jq '.id' 2>&1 | Out-String
        $replied[$commentId] = $result.Trim()
    } catch {
        Write-Warning "reply failed for $commentId : $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "=== Reply phase complete ==="
Write-Host "Replied: $($replied.Count) / $($replies.Count)"

# Step 2: GraphQL で全 review thread を取得し、対応した comment_id を含む thread を resolve
Write-Host ""
Write-Host "=== Resolving threads ==="

$query = @'
query($owner: String!, $name: String!, $pr: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $pr) {
      reviewThreads(first: 100) {
        nodes {
          id
          isResolved
          comments(first: 10) { nodes { databaseId } }
        }
      }
    }
  }
}
'@

$ownerName, $repoName = $repo -split '/'
$threadsJson = gh api graphql -f query="$query" -F "owner=$ownerName" -F "name=$repoName" -F "pr=$pr"
$threads = ($threadsJson | ConvertFrom-Json).data.repository.pullRequest.reviewThreads.nodes

$resolveMutation = @'
mutation($threadId: ID!) {
  resolveReviewThread(input: { threadId: $threadId }) {
    thread { id isResolved }
  }
}
'@

$resolvedCount = 0
foreach ($t in $threads) {
    if ($t.isResolved) { continue }
    $hasReplied = $false
    foreach ($c in $t.comments.nodes) {
        if ($replies.Contains([long]$c.databaseId)) { $hasReplied = $true; break }
    }
    if (-not $hasReplied) { continue }

    try {
        $null = gh api graphql -f query="$resolveMutation" -F "threadId=$($t.id)" 2>&1
        $resolvedCount++
        Write-Host "✓ resolved thread $($t.id)"
    } catch {
        Write-Warning "resolve failed for $($t.id): $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "=== Resolve phase complete ==="
Write-Host "Resolved: $resolvedCount threads"
