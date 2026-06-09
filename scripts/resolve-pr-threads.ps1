# PR #59 の review threads 全件を取得し、対象 comment_id を含む thread を resolve
$ErrorActionPreference = 'Stop'

$repo = '1llum1n4t1s/Lhamiel'
$pr = 59

$targetIds = @(
    3381085172, 3381085177, 3381085181, 3381085184, 3381085189, 3381085196,
    3381138324, 3381138335, 3381138340, 3381138347, 3381138352, 3381138355,
    3381138361, 3381138369, 3381138374, 3381138378, 3381138385, 3381138390,
    3381138394, 3381138424, 3381138436, 3381138447, 3381138457, 3381138460,
    3381138482, 3381313186, 3381313190
)

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

Write-Host "Total threads: $($threads.Count)"
Write-Host ""

$resolveMutation = @'
mutation($threadId: ID!) {
  resolveReviewThread(input: { threadId: $threadId }) {
    thread { id isResolved }
  }
}
'@

$resolvedCount = 0
$skippedCount = 0
foreach ($t in $threads) {
    if ($t.isResolved) { $skippedCount++; continue }
    $matched = $false
    foreach ($c in $t.comments.nodes) {
        $dbId = [long]$c.databaseId
        if ($targetIds -contains $dbId) { $matched = $true; break }
    }
    if (-not $matched) {
        Write-Host "skip (no target comment): $($t.id) - first comment_id=$($t.comments.nodes[0].databaseId)"
        continue
    }
    try {
        $null = gh api graphql -f query="$resolveMutation" -F "threadId=$($t.id)" 2>&1 | Out-Null
        $resolvedCount++
        Write-Host "✓ resolved $($t.id)"
    } catch {
        Write-Warning "resolve failed for $($t.id): $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "Resolved: $resolvedCount, Already-resolved/skipped: $skippedCount"
