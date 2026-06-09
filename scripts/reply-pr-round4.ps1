$ErrorActionPreference = 'Stop'
$repo = '1llum1n4t1s/Lhamiel'
$pr = 59

$replies = [ordered]@{
    3381905948 = '✅ 対応 (commit 6d320a2): `RegisterRedactionToken` の最小文字長 4 制約を撤去。`PasswordDialog` (CompressNew) は空入力のみ拒否し短いパスワードを許容するため、1〜3 文字でもマスクが効くようにする。短い token は正常ログ中の偶然一致を起こしやすいが、パスワード保護が有効なシナリオに限定された redaction なので副作用は限定的、「短いパスワードだけ平文露出」の方が遥かに悪い。'
    3381905952 = '✅ 対応 (commit 6d320a2): `CompressItemAsync` / `CompressItemsAsync` / `CompressMergedAsync` で `PasswordResolutionState` の `using` を try 内から外し、try-外スコープに `passwordStateForCleanup` / `batchPasswordForCleanup` / `mergedPasswordForCleanup` を宣言、`finally` で Dispose する形に変更。catch 内の `LogException` 実行中も redaction が有効に保たれる。'
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
