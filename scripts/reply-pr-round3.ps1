$ErrorActionPreference = 'Stop'
$repo = '1llum1n4t1s/Lhamiel'
$pr = 59

$replies = [ordered]@{
    3381582647 = '✅ 対応 (commit a87afc6): `CompressItemAsync` / `CompressMergedAsync` で `outputPath.lhamiel-tmp-XXXXXXXX` に対して圧縮を行い、`writer.Save` 成功時に既存削除→`File.Move` で atomic swap する設計に変更。`addedCount==0` 早期 throw や任意の中途失敗でも既存アーカイブを失わない。例外/finally 経路で temp ファイルを best-effort 削除。'
    3381582652 = '✅ 対応 (commit a87afc6): `ProcessDroppedPathsAsync` の `_settingsManager.Mutate(s => { ... })` で `s.CompressionFormat = SelectedCompressionFormat` を同時に書き込むよう変更。`isTar` 計算と settings スナップショットの値ズレを完全に排除。'
    3381597780 = '✅ 対応 (commit a87afc6): `scripts/translate-locales.ps1` を UTF-8 BOM 付き (EF BB BF) で保存し直し。Windows PowerShell 5.x で日本語コメント・翻訳辞書を ANSI 解釈して parse error になる事故を防止。同じ理由で `scripts/resolve-pr-threads.ps1` / `scripts/reply-pr-comments.ps1` にも BOM を付与。'
    3381597792 = '✅ 対応 (commit a87afc6): `Logger.LogException` で通常経路 (構造化 Error) / redaction 経路 (1 行 Error) の両方で `MaskUserPath(ApplyRedaction(...)) + GetCorrelationSuffix()` を統一適用。例外側 (`exception.ToString()`) も同じく `MaskUserPath(ApplyRedaction(...))` を通す。`WriteEmergencyLog` 側は冪等な再マスクなのでメッセージは元の生メッセージを渡す。'
}

foreach ($commentId in $replies.Keys) {
    $body = $replies[$commentId]
    Write-Host "→ reply to $commentId"
    try {
        $null = gh api "repos/$repo/pulls/$pr/comments/$commentId/replies" -X POST -f body=$body 2>&1
    } catch {
        Write-Warning "reply failed for $commentId : $($_.Exception.Message)"
    }
}

# Thread resolve
$query = @'
query($owner: String!, $name: String!, $pr: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $pr) {
      reviewThreads(first: 100) {
        nodes {
          id
          isResolved
          comments(first: 20) { nodes { databaseId } }
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
$targetIds = $replies.Keys | ForEach-Object { [long]$_ }
foreach ($t in $threads) {
    if ($t.isResolved) { continue }
    $matched = $false
    foreach ($c in $t.comments.nodes) {
        if ($targetIds -contains ([long]$c.databaseId)) { $matched = $true; break }
    }
    if (-not $matched) { continue }
    try {
        $null = gh api graphql -f query="$resolveMutation" -F "threadId=$($t.id)" 2>&1 | Out-Null
        $resolvedCount++
        Write-Host "✓ resolved $($t.id)"
    } catch {
        Write-Warning "resolve failed for $($t.id): $($_.Exception.Message)"
    }
}
Write-Host ""
Write-Host "Resolved: $resolvedCount / $($replies.Count)"
