$repo = '1llum1n4t1s/Lhamiel'
$pr = 59
$query = @'
query($owner: String!, $name: String!, $pr: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $pr) {
      reviewThreads(first: 100) {
        nodes {
          id
          isResolved
          comments(first: 20) { nodes { databaseId author { login } } }
        }
      }
    }
  }
}
'@
$ownerName, $repoName = $repo -split '/'
$threadsJson = gh api graphql -f query="$query" -F "owner=$ownerName" -F "name=$repoName" -F "pr=$pr"
$threads = ($threadsJson | ConvertFrom-Json).data.repository.pullRequest.reviewThreads.nodes
Write-Host "Total threads: $($threads.Count) | Resolved: $(($threads | Where-Object isResolved).Count) | Open: $(($threads | Where-Object { -not $_.isResolved }).Count)"
$threads | Where-Object { -not $_.isResolved } | ForEach-Object {
    $cIds = ($_.comments.nodes | ForEach-Object { "$($_.author.login):$($_.databaseId)" }) -join ","
    Write-Host "OPEN: $($_.id) | $cIds"
}
