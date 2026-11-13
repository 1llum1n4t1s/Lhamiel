# release-local.ps1 — ローカル署名付き Velopack リリース
#
# 署名は Sigilune (Certum SimplySign のクラウド鍵を signtool /dlib で使う CLI) で行う。
# 通常のリリースは GitHub Actions の .github/workflows/release.yml (手動起動・Environment "release") が
# 本スクリプトを実行する。ローカル実行も同じ手順で動き、検証や再開に使う。
#
# 前提:
#   - Sigilune を使えること (scripts/Sigilune.Common.ps1 参照)。
#     ローカルは `sigilune setup` で Certum アカウントを登録済み、CI は SIGILUNE_EMAIL / SIGILUNE_TOTP_URI を渡す
#   - Directory.Build.props の <Version> がリリースしたいバージョンになっていること (/vava 済み)
#   - 環境変数 CLOUDFLARE_API_TOKEN (CI)、なければ D:\Dev\Secret\secrets.json の cloudflare.api_token (ローカル)
#
# 使い方:
#   pwsh scripts/release-local.ps1                # フルリリース (build + sign + upload + cleanup)
#   pwsh scripts/release-local.ps1 -SkipUpload    # ビルド + 署名のみ (アップロードしない動作確認用)
#   pwsh scripts/release-local.ps1 -UploadOnly -WorkDirectory <絶対Tempパス>  # 同じ版の完成済みpackageを検証して配信から再開
#   pwsh scripts/release-local.ps1 -ReusePublish -WorkDirectory <絶対Tempパス>  # 同じ版の署名済みpublishを検証して再利用し、未完了RIDをビルド
#   pwsh scripts/release-local.ps1 -Runtimes win-x64   # 対象 RID を絞る (テスト用)

[CmdletBinding()]
param(
    [switch]$SkipUpload,
    [switch]$ReusePublish,
    [switch]$UploadOnly,
    [string[]]$Runtimes = @('win-x64', 'win-arm64'),
    [string]$WorkDirectory
)



# 作業出力は絶対パスの外部 Temp に限定し、リンク経由の書込みも拒否する。
function Resolve-ExternalWorkDirectory {
    param([string]$Path, [string]$RepositoryRoot)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path)) { throw 'WorkDirectory は絶対パスで指定してください。' }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
    if (-not $resolved.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'WorkDirectory はユーザーの Temp 配下に指定してください。' }
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

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'R2Distribution.Common.ps1')

# ---- 定数 ----
# Velopack (vpk) は常に最新安定版を使う (ゆろ君ルール): NuGet から実行時に最新を解決して pin する
# 製品側 Velopack は固定版 VelopackUpdateDialog.Avalonia の推移依存で、解決版は packages.lock.json に従う
$Bucket = 'lhamiel-updates'
$BaseUrl = 'https://lhamiel.kagayoi.com'
$ZoneName = 'kagayoi.com'           # Cloudflare zone (apex)。$BaseUrl の Host から正規表現で推測すると
                                     # apex / co.jp 等の複合 TLD で誤判定するため定数で固定する
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'D:\Dev\Secret\secrets.json'
# 署名者の CN。証明書の年次更新で thumbprint が変わっても同じ値で照合できる。
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'

$RuntimeMatrix = @{
    'win-x64'   = @{ PlatformTarget = 'x64';   Channel = 'win' }
    'win-arm64' = @{ PlatformTarget = 'ARM64'; Channel = 'win-arm64' }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot
$versionNode = ([xml](Get-Content (Join-Path $RepoRoot 'Directory.Build.props') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
if (-not $version -or $version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Directory.Build.props の <Version> が不正です' }
if (-not $WorkDirectory) {
    if ($ReusePublish -or $UploadOnly) { throw '再開時は既存の絶対 Temp パスを -WorkDirectory に指定してください。' }
    $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) "Codex\Lhamiel\release-$version-$([guid]::NewGuid().ToString('N'))"
}
$WorkDir = Resolve-ExternalWorkDirectory -Path $WorkDirectory -RepositoryRoot $RepoRoot
if (-not $UploadOnly -and -not $ReusePublish -and (Test-Path -LiteralPath $WorkDir) -and @(Get-ChildItem -LiteralPath $WorkDir -Force).Count) {
    throw "WorkDirectory が空ではありません: $WorkDir。PRUNLET.md に従い Prunlet CLI を直接 CMD から実行して清掃するか、新しいディレクトリを指定してください。"
}
$ArtifactsDir = Join-Path $WorkDir 'packages'
Write-Host "作業ディレクトリ: $WorkDir"
# vpk / AOT / 署名ツールの一時出力も同じ作業ルートに置く。
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$toolTemp = Join-Path $WorkDir 'temp'
New-Item -ItemType Directory -Path $toolTemp -Force | Out-Null
$env:TEMP = $toolTemp
$env:TMP = $toolTemp
try {
$VpkVersion = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/vpk/index.json' -TimeoutSec 30).versions |
    Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
if (-not $VpkVersion) { throw 'vpk の最新安定版バージョンの取得に失敗しました (NuGet API)' }
Write-Host "vpk 最新安定版: $VpkVersion"


function Invoke-Native {
    param([string]$Description, [scriptblock]$Block)
    & $Block
    if ($LASTEXITCODE -ne 0) { throw "$Description が失敗しました (exit $LASTEXITCODE)" }
}

# ---- 0. プリフライト ----
Write-Host '== プリフライト ==' -ForegroundColor Cyan

# Git Bash (MSYS) 経由で起動すると括弧入り環境変数が落ちて、Native AOT の
# リンク段 (Microsoft.NETCore.Native.targets) の vswhere.exe 解決が壊れるため補完する
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)' }

# VS 2026 の vcvarsall は PATH 上の vswhere.exe を呼ぶ (GitHub ランナーは PATH 済み)。
# ローカルでは VS Installer ディレクトリが PATH に無いので AOT リンクが落ちる → 追加
$vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ($env:PATH -notlike "*$vsInstallerDir*") { $env:PATH = "$env:PATH;$vsInstallerDir" }

# vpk (dotnet tool) は 1.2.161 時点で net8/9/10 向けビルドを同梱し、通常は導入済みランタイムで
# そのまま動く。将来の vpk が未導入のメジャーだけを要求した場合に備え、保険として上位メジャーへの
# ロールフォワードを許可しておく。
$env:DOTNET_ROLL_FORWARD = 'Major'

# コンポーネント登録の照合が失敗する環境でも、実在する既存の VS ツールを利用する。
# RID 切替ごとに Developer Shell を初期化し、AOT にその linker / LIB を渡す。
$vswhere = Join-Path $vsInstallerDir 'vswhere.exe'
$visualStudioPath = & $vswhere -latest -prerelease -products * -property installationPath |
    Select-Object -First 1
if (-not $visualStudioPath) { throw 'Visual Studio のインストール先が見つかりません' }
$developerShell = Join-Path $visualStudioPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
if (-not (Test-Path -LiteralPath $developerShell)) { throw "Developer Shell が見つかりません: $developerShell" }
Import-Module $developerShell -ErrorAction Stop

Write-Host "バージョン: $version"

# 署名の準備 (Sigilune の場所とクラウドへのログインを、ビルド前に確認して早く落とす)。
# 配信だけを再開する -UploadOnly では署名しないためログインしない。
. (Join-Path $PSScriptRoot 'Sigilune.Common.ps1')
if (-not $UploadOnly) {
    $Sigilune = Resolve-SigiluneExe
    Connect-Sigilune -SigiluneExe $Sigilune
    # Velopack はファイルごとに別プロセスで署名テンプレートを実行する。Sigilune はログインを共有し、
    # 署名後に有効性・署名者・タイムスタンプを検証して失敗なら非0で終わる。
    $SignTemplate = "`"$Sigilune`" sign {{file}}"
}

# vpk を固定バージョンで用意
$vpkInstalled = (dotnet tool list --global | Select-String -SimpleMatch 'vpk') -match [regex]::Escape($VpkVersion)
if (-not $vpkInstalled) {
    Write-Host "vpk $VpkVersion をインストールします..."
    dotnet tool uninstall --global vpk 2>$null | Out-Null
    Invoke-Native 'vpk のインストール' { dotnet tool install --global vpk --version $VpkVersion }
}

# Cloudflare トークン (アップロード時のみ必要)
# zone 解決もここで行う: トークンに zone:read / cache purge 権限が無い場合に
# R2 アップロード後の途中失敗 (新ファイルだけ R2 に乗ってパージ・クリーンアップが
# 走らない半端なリリース) を避け、何もアップロードしていない時点で fail fast する
if (-not $SkipUpload) {
    # CI では Environment "release" の Secret が環境変数で渡る。ローカルは secrets.json から必要なキーだけ読む。
    if (-not $env:CLOUDFLARE_API_TOKEN) {
        $secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
        if (-not $secrets.cloudflare.api_token) { throw "secrets.json に cloudflare.api_token が見つかりません" }
        $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
    }
    $env:CLOUDFLARE_ACCOUNT_ID = $AccountId

    $cfHeaders = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }
    $zoneResp = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones?name=$ZoneName" -Headers $cfHeaders -TimeoutSec 30
    if (-not $zoneResp.success -or @($zoneResp.result).Count -eq 0) { throw "Cloudflare zone '$ZoneName' の取得に失敗しました (トークンの zone:read 権限を確認してください)" }
    $zoneId = $zoneResp.result[0].id
    Write-Host "Cloudflare zone: $ZoneName ($zoneId)"
}

New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

# ---- 1. ビルド + 署名付きパッケージング (RID ごと) ----
if (-not $UploadOnly) {
foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    if (-not $config) { throw "未知の runtime: $runtime" }
    $publishDir = Join-Path $WorkDir "publish-$runtime"
    # ProjectReference 先も RID ごとに出力を分離する。共通の bin/obj を使うと、先に
    # ビルドした x64 の参照アセンブリが ARM64 publish で再利用され CS8012 になる。
    $buildArtifactsDir = Join-Path $WorkDir "build-$runtime"

    if ($ReusePublish -and (Test-Path -LiteralPath (Join-Path $publishDir 'Lhamiel.exe'))) {
        $existingVersion = (Get-Item -LiteralPath (Join-Path $publishDir 'Lhamiel.exe')).VersionInfo.ProductVersion.Split('+')[0]
        if ($existingVersion -ne $version) { throw "既存 publish の version が異なります: $existingVersion" }
        foreach ($name in @('Lhamiel.ShellExtension.dll', 'Lhamiel.ContextMenu.msix')) {
            $sig = Get-AuthenticodeSignature -LiteralPath (Join-Path $publishDir $name)
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "CN=$CertSubjectName*") { throw "再利用できない署名です: $name" }
        }
        Write-Host "== 署名済み publish を再利用: $runtime =="
    } else {
        Write-Host "== publish: $runtime ==" -ForegroundColor Cyan
        Enter-VsDevShell -VsInstallPath $visualStudioPath -SkipAutomaticLocation `
            -DevCmdArguments "-arch=$($config.PlatformTarget.ToLowerInvariant()) -host_arch=x64"
        Invoke-Native "dotnet publish ($runtime)" {
            dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r $runtime `
                -p:PlatformTarget=$($config.PlatformTarget) -p:OS=Windows_NT -p:IlcUseEnvironmentalTools=true `
                --artifacts-path $buildArtifactsDir -o $publishDir
        }

        Invoke-Native "Windows 11 Shell 統合の生成 ($runtime)" {
            pwsh scripts/build-shell-integration.ps1 `
                -Runtime $runtime `
                -PublishDir $publishDir `
                -WorkDirectory (Join-Path $WorkDir "shell-$runtime") `
                -CertificateSubjectName $CertSubjectName
        }

        # README.txt 生成 (CI 版の Markdown 除去ロジックを移植)
        $content = Get-Content 'README.md' -Raw -Encoding utf8
        $content = $content -replace '!\[.*?\]\(.*?\)\r?\n?', ''
        $content = $content -replace '<img[^>]*/?>\r?\n?', ''
        $content = $content -replace '\[([^\]]+)\]\(([^\)]+)\)', '$1 ($2)'
        $content = $content -replace '(?m)^#{1,6}\s+', ''
        $content = $content -replace '\*\*([^*]+)\*\*', '$1'
        $content = $content -replace '`([^`]+)`', '$1'
        $content = $content -replace '(?m)^\|(?:[ \t]*:?-+:?[ \t]*\|)+[ \t]*\r?\n?', ''
        $content = $content -replace '(?m)^\|[ \t]*(.*?)[ \t]*\|[ \t]*\r?$', '$1'
        $content = $content -replace '(?m)^>\s*', ''
        $content = $content -replace '\r?\n{3,}', "`n`n"
        [System.IO.File]::WriteAllText((Join-Path $publishDir 'README.txt'), $content.Trim(), [System.Text.Encoding]::UTF8)

    }
    foreach ($required in @(
        'Lhamiel.exe',
        '7z.dll',
        'unlhare.dll',
        'licenses\Kagayoi.UnLhaRe\LICENSE',
        'licenses\Kagayoi.UnLhaRe\THIRD_PARTY_NOTICES.md'
    )) {
        if (-not (Test-Path (Join-Path $publishDir $required))) {
            throw "$required が publish 出力にありません ($runtime)"
        }
    }

    Write-Host "== vpk pack + 署名: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "vpk pack ($runtime)" {
        vpk pack `
            --packId Lhamiel `
            --packVersion $version `
            --packTitle 'Lhamiel' `
            --packAuthors 'Kagayoi' `
            --mainExe Lhamiel.exe `
            --icon (Join-Path 'src' 'Lhamiel' 'icon' 'app.ico') `
            --packDir $publishDir `
            --outputDir $ArtifactsDir `
            --channel $config.Channel `
            --runtime $runtime `
            --shortcuts 'StartMenuRoot,Desktop' `
            --signTemplate $SignTemplate
    }
}

}

# 保存済み成果物だけを配信するときも、版・実体・署名を確認する。
foreach ($runtime in $Runtimes) {
    $channel = $RuntimeMatrix[$runtime].Channel
    $manifest = Get-Content -LiteralPath (Join-Path $ArtifactsDir "releases.$channel.json") -Raw | ConvertFrom-Json
    if (@($manifest.Assets).Count -eq 0) { throw "配信manifestが空です: $channel" }
    foreach ($asset in $manifest.Assets) {
        if ($asset.Version -ne $version -or [IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName) { throw "再開できないmanifestです: $channel" }
        $package = Get-Item -LiteralPath (Join-Path $ArtifactsDir $asset.FileName)
        if ($package.Length -ne $asset.Size -or (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $asset.SHA256) { throw "保存packageが一致しません: $channel" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $ArtifactsDir "Lhamiel-$channel-Setup.exe"))) { throw "Setupがありません: $channel" }
}

# 署名検証 (Setup.exe が正しく署名されているかリリース前に確認)
Write-Host '== 署名検証 ==' -ForegroundColor Cyan
foreach ($exe in Get-ChildItem $ArtifactsDir -Filter '*.exe') {
    $sig = Get-AuthenticodeSignature $exe.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "CN=$CertSubjectName*") {
        throw "署名検証失敗: $($exe.Name) → $($sig.Status)"
    }
    Write-Host "  ✅ $($exe.Name): Valid ($($sig.SignerCertificate.Subject -replace ',.*$'))"
}

if ($SkipUpload) {
    Write-Host "`n✅ -SkipUpload 指定のためここで終了。成果物: $ArtifactsDir" -ForegroundColor Green
    Get-ChildItem $ArtifactsDir | Format-Table Name, @{n='Size(MB)'; e={[math]::Round($_.Length/1MB,1)}}
    return
}

# ---- 2. R2 アップロード ----
# - releases.{channel}.json (manifest) は同 channel の旧版を上書き
# - *.nupkg は put のみ (過去版は cleanup ステップが manifest 基準で削除)
Write-Host '== R2 アップロード ==' -ForegroundColor Cyan
$uploaded = 0
$uploadFiles = @(Get-ChildItem $ArtifactsDir -File)
$ordered = @($uploadFiles | Where-Object { $_.Name -notmatch '^(?:releases\..*\.json|assets\..*\.json|RELEASES(?:-.*)?)$' }) +
    @($uploadFiles | Where-Object { $_.Name -match '^(?:releases\..*\.json|assets\..*\.json|RELEASES(?:-.*)?)$' })
foreach ($f in $ordered) {
    Write-Host "  ↑ $($f.Name)"
    $immutable = $f.Name -match '(?:\.nupkg$|[-_.]\d+\.\d+\.\d+(?:[-_.]|$))'
    $cacheControl = if ($immutable) { 'public, max-age=31536000, immutable' } else { 'no-cache, must-revalidate' }
    $contentType = switch -Regex ($f.Name) { '\.json$' { 'application/json'; break }; '^RELEASES' { 'text/plain'; break }; '\.(zip|nupkg)$' { 'application/zip'; break }; default { 'application/octet-stream' } }
    Publish-R2Object -AccountId $AccountId -Bucket $Bucket -FilePath $f.FullName -Immutable:$immutable -CacheControl $cacheControl -ContentType $contentType | Out-Null
    $uploaded++
}
Write-Host "✅ R2 アップロード完了: $uploaded ファイル"

# ---- 2.5 Cloudflare エッジキャッシュのパージ ----
# 固定名ファイル (Setup.exe / Portable.zip / RELEASES / releases.*.json / assets.*.json) は
# 毎リリースで中身が変わるため、配信内容を取得せず固定 URL を全件パージする。
Write-Host '== Cloudflare キャッシュパージ ==' -ForegroundColor Cyan
# $zoneId / $cfHeaders はプリフライト (Cloudflare トークン取得時) で解決・検証済み
$purgeUrls = @(Get-ChildItem $ArtifactsDir -File | Where-Object { $_.Name -notlike '*.nupkg' } | ForEach-Object { "$BaseUrl/$($_.Name)" })
if ($purgeUrls.Count -gt 0) {
    try {
        # purge_cache は 1 リクエストあたり最大 30 URL までのため分割送信する
        for ($i = 0; $i -lt $purgeUrls.Count; $i += 30) {
            $batch = $purgeUrls[$i..[Math]::Min($i + 29, $purgeUrls.Count - 1)]
            $purgeBody = [PSCustomObject]@{ files = $batch } | ConvertTo-Json -Compress
            $purgeResp = Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
                -Headers $cfHeaders -ContentType 'application/json' -Body $purgeBody -TimeoutSec 30
            if (-not $purgeResp.success) { throw "Cloudflare キャッシュパージに失敗しました: $($purgeResp.errors | ConvertTo-Json -Compress)" }
        }
        Write-Host "  ✅ パージ: $($purgeUrls.Count) URL"
        $purgeUrls | ForEach-Object { Write-Host "     $_" }
    } catch {
        throw "アップロード済みですが、キャッシュパージの受付を確認できません: $($_.Exception.Message)"
    }
} else {
    Write-Host '  パージ対象なし'
}

# ---- 4. 旧バージョン nupkg のクリーンアップ (Aggressive 戦略) ----
# keep set は「ローカル packages の manifest (= 今アップロードしたもの)」と
# 「R2 バケット上の全 releases.*.json」が参照するファイルの和集合で作る。
# -Runtimes で一部 RID だけをリリースした場合でも、今回対象外の RID の manifest が
# 参照する nupkg を消さないため (消すとその RID の自動更新が壊れる)。
# バケット上の manifest はいずれか 1 つでも取得・解析できなければ、保持集合が不完全なまま
# 削除する危険があるのでクリーンアップ全体を中止する (アップロード済みのリリースは成功扱い)。
# 固定ファイル名 (Setup.exe / Portable.zip / RELEASES* / assets.*.json / releases.*.json) は
# バージョン文字列を含まないため削除対象外。
Write-Host '== 旧 nupkg クリーンアップ ==' -ForegroundColor Cyan
$keep = @{}
$manifests = Get-ChildItem $ArtifactsDir -Filter 'releases.*.json'
if (-not $manifests) { throw 'packages に releases.*.json が見つかりません' }
foreach ($m in $manifests) {
    foreach ($asset in (Get-Content $m.FullName -Raw | ConvertFrom-Json).Assets) {
        if ($asset.FileName) { $keep[$asset.FileName] = $true }
    }
}

$api = "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket"
$headers = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }

$allKeys = [System.Collections.Generic.List[string]]::new()
$cursor = ''
while ($true) {
    $uri = "$api/objects?per_page=1000" + $(if ($cursor) { "&cursor=$cursor" })
    $resp = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 30
    foreach ($obj in $resp.result) { $allKeys.Add($obj.key) }
    # 全件 1 ページに収まると result_info が省略される (StrictMode 下では直接参照が throw)
    $info = $resp.PSObject.Properties['result_info']
    if (-not $info -or -not $info.Value) { break }
    $truncated = $info.Value.PSObject.Properties['is_truncated']
    if (-not $truncated -or -not $truncated.Value) { break }
    $cursorProp = $info.Value.PSObject.Properties['cursor']
    $cursor = if ($cursorProp) { $cursorProp.Value } else { '' }
    if (-not $cursor) { break }
}

# バケット上の全 channel の manifest を、公開 CDN ではなく認証付き R2 API (既存の
# R2Transfer.Core.cs の GetArtifactObject) で取得し、参照ファイルを保持集合へ加える。
# 公開 URL からの取得は CDN キャッシュの影響を受けるうえ、公開後の配信内容取得を行わない
# 運用方針 (AGENTS.md の公開完了判定) にも反するため使わない。
$cleanupAborted = $false
$remoteManifestKeys = @($allKeys | Where-Object { $_ -match '^releases\.[^/]+\.json$' })
$manifestClient = $null
try {
    $manifestClient = New-R2DistributionClient
    # 直前にアップロードした manifest が一覧に無い場合は、一覧が不完全 (取得失敗・反映遅延) とみなして
    # 削除を中止する。不完全な一覧から作った保持集合で削除すると、他 RID の参照ファイルを消しうるため。
    foreach ($localManifest in $manifests) {
        if ($remoteManifestKeys -notcontains $localManifest.Name) {
            throw "アップロード済みの $($localManifest.Name) がバケット一覧にありません"
        }
    }
    $manifestCredentials = Get-R2DistributionCredentials -Client $manifestClient -AccountId $AccountId -ApiToken $env:CLOUDFLARE_API_TOKEN
    foreach ($manifestKey in $remoteManifestKeys) {
        $manifestBuffer = [IO.MemoryStream]::new()
        try {
            # manifest は数十 KB 程度。異常に大きい応答は 16 MiB で打ち切って失敗扱いにする。
            [Kagayoi.R2.Transfer]::GetArtifactObject($manifestClient, $manifestCredentials, $AccountId, $Bucket, $manifestKey,
                $manifestBuffer, 16MB, -1, $null)
            $manifestBuffer.Position = 0
            # BOM 付きでも解析できるよう StreamReader の BOM 検出を使う。
            $reader = [IO.StreamReader]::new($manifestBuffer, [Text.UTF8Encoding]::new($false), $true)
            try { $manifestJson = $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally {
            $manifestBuffer.Dispose()
        }
        # StrictMode 下では Assets / FileName が欠けた manifest は例外になり、下の catch で中止扱いになる。
        $remoteManifest = $manifestJson | ConvertFrom-Json
        if ($null -eq $remoteManifest.Assets) { throw "Assets がありません: $manifestKey" }
        foreach ($asset in @($remoteManifest.Assets)) {
            if ([string]::IsNullOrWhiteSpace([string]$asset.FileName)) { throw "FileName が空の asset があります: $manifestKey" }
            $keep[[string]$asset.FileName] = $true
        }
    }
} catch {
    $cleanupAborted = $true
    Write-Warning "  R2 上の manifest を取得・解析できないため、旧版クリーンアップを中止します (リリース自体は完了しています) — $($_.Exception.Message)"
} finally {
    if ($manifestClient) { $manifestClient.Dispose() }
}

if (-not $cleanupAborted) {
Write-Host "  保持対象 (manifest 参照): $($keep.Count) 件 (R2 上の manifest: $($remoteManifestKeys.Count) 件)"

# 全プロジェクト共通の保持ポリシー: 直近 2 バージョン。
# 旧実装は '*.nupkg' だけを削除対象にしていたため、バージョン付きの配布物
# (zip / deb / rpm / AppImage 等) が R2 に永久に溜まっていた (Ferry で 351 個 7.2GB)。
$KeepVersionCount = 2
$versionPattern = '(\d+\.\d+\.\d+)'
$allVersions = @(
    $allKeys | ForEach-Object {
        $m = [regex]::Match($_, $versionPattern)
        if ($m.Success) { $m.Groups[1].Value }
    } | Sort-Object -Property { [version]$_ } -Unique
)
$keepVersions = @($allVersions | Select-Object -Last $KeepVersionCount)
Write-Host "  保持バージョン: $($keepVersions -join ', ') (全 $($allVersions.Count) 世代)"

$toDelete = $allKeys | Where-Object {
    # manifest が参照するファイルは絶対保持 (消すと自動更新が壊れる)
    if ($keep.ContainsKey($_)) { return $false }
    # 固定ファイル名はバージョン文字列を含まない = 毎リリース上書きなので保持
    $m = [regex]::Match($_, $versionPattern)
    if (-not $m.Success) { return $false }
    return $keepVersions -notcontains $m.Groups[1].Value
}
if (-not $toDelete) {
    Write-Host '  ✅ 削除対象なし'
} else {
    $deleted = 0; $failed = 0
    foreach ($key in $toDelete) {
        $encoded = [uri]::EscapeDataString($key)
        try {
            Invoke-RestMethod -Method Delete -Uri "$api/objects/$encoded" -Headers $headers -TimeoutSec 30 | Out-Null
            Write-Host "  🗑️  $key"
            $deleted++
        } catch {
            Write-Warning "  削除失敗: $key — $($_.Exception.Message)"
            $failed++
        }
    }
    Write-Host "  🧹 クリーンアップ: $deleted 削除 / $failed 失敗"
    # 全件失敗は token 権限等の異常なので fail (一部失敗は次回リリースで再試行される)
    if ($failed -gt 0 -and $deleted -eq 0) { throw '旧 nupkg の削除がすべて失敗しました。API token の権限を確認してください。' }
}
} # if (-not $cleanupAborted)

# ---- 5. packages.lock.json のクリーンアップ (リリース後の working tree clean 化) ----
# RID 付き publish (dotnet publish -r <rid>) は packages.lock.json を汚染する:
# RID 固有セクション (net11.0/win-arm64 等) や Native AOT 用の依存
# (Microsoft.DotNet.ILCompiler / Microsoft.NET.ILLink.Tasks) が書き足され、Debug 専用
# パッケージが落ちることがある。この汚染 lock は RID なしの locked-mode 復元
# (dotnet restore --locked-mode) で NU1004 になり、
# lockfile の supply-chain diff 検知も publish churn で無意味化する。
# --force-evaluate で RID なし clean 状態に戻し、毎回 working tree が clean な状態でリリースを終える
# (/vava のリリース後 lock drift 手動 commit/revert が不要になる)。
# 成果物は既にアップロード済みなので、ここでの失敗はリリースには影響しない → warning に留めて継続する。
Write-Host '== packages.lock.json クリーンアップ ==' -ForegroundColor Cyan
try {
    Invoke-Native 'dotnet restore --force-evaluate' {
        dotnet restore Lhamiel.slnx --force-evaluate --artifacts-path (Join-Path $WorkDir 'restore')
    }
    # clean lock の検証: locked-mode 復元が NU1004 ゼロで通れば RID なし clean 状態
    Invoke-Native 'dotnet restore --locked-mode (clean lock 検証)' {
        dotnet restore Lhamiel.slnx --locked-mode --artifacts-path (Join-Path $WorkDir 'restore')
    }
    Write-Host '  ✅ packages.lock.json は RID なし clean 状態 (locked-mode 検証 OK)'
} catch {
    Write-Warning "  packages.lock.json のクリーンアップに失敗しました (アップロード済みリリースには影響なし)。外部作業ディレクトリを --artifacts-path に指定して手動 restore してください — $($_.Exception.Message)"
}

Write-Host "`n🎉 リリース完了: v$version → $BaseUrl" -ForegroundColor Green

} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
