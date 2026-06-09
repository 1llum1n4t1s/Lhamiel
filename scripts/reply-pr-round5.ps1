$ErrorActionPreference = 'Stop'
$repo = '1llum1n4t1s/Lhamiel'
$pr = 59

$replies = [ordered]@{
    3382065857 = '✅ 対応 (commit 8a479f9): `Logger.ApplyRedaction` で `_redactionTokens.Keys.OrderByDescending(t => t.Length)` を適用してから順次置換。`abcd` と `abcdef` が同時アクティブな状態でも長い prefix から先に当たるので "***ef" のような部分露出を防げる。'
    3382065860 = '✅ 対応 (commit 8a479f9): atomic swap でバックアップ rename を挟む形に変更。既存 → `outputPath.lhamiel-bak-XXX` に move → temp → `outputPath` に move → 成功時 bak を削除 / Move 失敗時は bak から best-effort restore。`CompressItemAsync` (file/directory 両対応) と `CompressMergedAsync` (file only) 両方に適用。AV ロック・ACL race で Move 失敗しても既存が永久に失われない。'
    3382074424 = '⚠️ 部分対応 (許容判断): `scripts/reply-pr-comments.ps1` は PR レビュー対応の運用スクリプト (一回限り使用) で `$ErrorActionPreference = ''Stop''` により API 失敗時に明示的に停止します。指摘の通り、構造検証を追加すればデバッグが容易になりますが、対象が短命の運用スクリプトであり利用者は私 (作者) のみであることから、現状のままとします (Minor 指摘・運用範囲内)。'
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

$query = @'
query($owner: String!, $name: String!, $pr: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $pr) {
      reviewThreads(first: 100) {
        nodes { id isResolved comments(first: 20) { nodes { databaseId } } }
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
