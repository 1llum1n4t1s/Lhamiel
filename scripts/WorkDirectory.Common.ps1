# 作業出力の保存先検査の共通関数。release-local.ps1 と build-shell-integration.ps1 から dot-source する。

# 作業出力は絶対パスの外部ディレクトリに限定し、リンク経由の書込みも拒否する。
# -RequireUserTemp を付けると、ユーザーの Temp 配下であることも要求する。
function Resolve-ExternalWorkDirectory {
    param([string]$Path, [string]$RepositoryRoot, [switch]$RequireUserTemp)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path)) { throw 'WorkDirectory は絶対パスで指定してください。' }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    if ($RequireUserTemp) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
        if (-not $resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory はユーザーの Temp 配下に指定してください。' }
    }
    $forbiddenRoots = @($RepositoryRoot, (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'))
    if ($env:CODEX_HOME) { $forbiddenRoots += $env:CODEX_HOME }
    foreach ($root in $forbiddenRoots) {
        $boundary = [IO.Path]::GetFullPath($root).TrimEnd('\','/')
        if ($resolved.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "WorkDirectory はリポジトリ / Codex ディレクトリの外に指定してください: $resolved" }
    }
    $ancestor = $resolved
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "WorkDirectory の祖先は通常のディレクトリに限定します: $ancestor" }
        }
        $ancestor = Split-Path $ancestor -Parent
    }
    if (Test-Path -LiteralPath $resolved) {
        if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force -ErrorAction Stop | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "WorkDirectory 内のリンクを除いてください: $resolved" }
    }
    return $resolved
}
