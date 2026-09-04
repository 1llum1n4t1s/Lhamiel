# release-local.ps1 — ローカル署名付き Velopack リリース
#
# SimplySign (Certum クラウド署名) は Desktop 接続 + スマホトークンが必要で
# GitHub Actions からは署名できないため、リリースは本スクリプトでローカル実行する。
# 旧 CI リリース (.github/workflows/velopack-release.yml) はこのスクリプトに置換済み。
#
# 前提:
#   - SimplySign Desktop が接続済み (証明書が CurrentUser\My に見えていること)
#   - Directory.Build.props の <Version> がリリースしたいバージョンになっていること (/vava 済み)
#   - C:\Users\IMT\dev\Secret\secrets.json に cloudflare.api_token があること
#
# 使い方:
#   pwsh scripts/release-local.ps1                # フルリリース (build + sign + upload + cleanup)
#   pwsh scripts/release-local.ps1 -SkipUpload    # ビルド + 署名のみ (アップロードしない動作確認用)
#   pwsh scripts/release-local.ps1 -Runtimes win-x64   # 対象 RID を絞る (テスト用)

[CmdletBinding()]
param(
    [switch]$SkipUpload,
    [string[]]$Runtimes = @('win-x64', 'win-arm64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- 定数 ----
# Velopack (vpk) は常に最新安定版を使う (ゆろ君ルール): NuGet から実行時に最新を解決して pin する
# (NuGet 側 Velopack も VelopackUpdateDialog.Avalonia 経由の transitive で常に最新へ追従する)
$VpkVersion = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/vpk/index.json' -TimeoutSec 30).versions |
    Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
if (-not $VpkVersion) { throw 'vpk の最新安定版バージョンの取得に失敗しました (NuGet API)' }
Write-Host "vpk 最新安定版: $VpkVersion"
$WranglerVersion = '4.127.1'        # サプライチェーン対策でバージョン固定 (要 Node.js >=22)
$Bucket = 'lhamiel-updates'
$BaseUrl = 'https://lhamiel.kagayoi.com'
$ZoneName = 'kagayoi.com'           # Cloudflare zone (apex)。$BaseUrl の Host から正規表現で推測すると
                                     # apex / co.jp 等の複合 TLD で誤判定するため定数で固定する
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'C:\Users\IMT\dev\Secret\secrets.json'
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'
# /n (Subject 名) で選択: 証明書の年次更新で thumbprint が変わっても動く
$SignParams = "/n `"$CertSubjectName`" /fd SHA256 /td SHA256 /tr http://time.certum.pl"

$RuntimeMatrix = @{
    'win-x64'   = @{ PlatformTarget = 'x64';   Channel = 'win' }
    'win-arm64' = @{ PlatformTarget = 'ARM64'; Channel = 'win-arm64' }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot
$WorkDir = Join-Path $RepoRoot 'local-release'
$ArtifactsDir = Join-Path $WorkDir 'artifacts'

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

# vpk (dotnet tool) は .NET 9 ランタイム要求だがローカルは 8/10 のみ → 10 にロールフォワード
$env:DOTNET_ROLL_FORWARD = 'Major'

# XPath で取得 (member enumeration は Version を持たない PropertyGroup 混在時に StrictMode で throw する)
$versionNode = ([xml](Get-Content 'Directory.Build.props' -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
if (-not $version) { throw 'Directory.Build.props から <Version> を取得できませんでした' }
Write-Host "バージョン: $version"

# SimplySign 接続確認 (証明書が見えなければ署名できないので最初に落とす)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -like "CN=$CertSubjectName*" -and $_.NotAfter -gt (Get-Date) }
if (-not $cert) {
    throw "署名証明書 (CN=$CertSubjectName) が見つかりません。SimplySign Desktop を起動してトークンでログインしてください。"
}
Write-Host "署名証明書: $($cert.Subject) (期限 $($cert.NotAfter.ToString('yyyy-MM-dd')))"

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
    $secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
    if (-not $secrets.cloudflare.api_token) { throw "secrets.json に cloudflare.api_token が見つかりません" }
    $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
    $env:CLOUDFLARE_ACCOUNT_ID = $AccountId

    $cfHeaders = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }
    $zoneResp = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones?name=$ZoneName" -Headers $cfHeaders -TimeoutSec 30
    if (-not $zoneResp.success -or @($zoneResp.result).Count -eq 0) { throw "Cloudflare zone '$ZoneName' の取得に失敗しました (トークンの zone:read 権限を確認してください)" }
    $zoneId = $zoneResp.result[0].id
    Write-Host "Cloudflare zone: $ZoneName ($zoneId)"
}

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

# ---- 1. ビルド + 署名付きパッケージング (RID ごと) ----
foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    if (-not $config) { throw "未知の runtime: $runtime" }
    $publishDir = Join-Path $WorkDir "publish-$runtime"
    # ProjectReference 先も RID ごとに出力を分離する。共通の bin/obj を使うと、先に
    # ビルドした x64 の参照アセンブリが ARM64 publish で再利用され CS8012 になる。
    $buildArtifactsDir = Join-Path $WorkDir "build-$runtime"

    Write-Host "== publish: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet publish ($runtime)" {
        dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r $runtime `
            -p:PlatformTarget=$($config.PlatformTarget) -p:OS=Windows_NT `
            --artifacts-path $buildArtifactsDir -o $publishDir
    }

    foreach ($required in 'Lhamiel.exe', '7z.dll') {
        if (-not (Test-Path (Join-Path $publishDir $required))) {
            throw "$required が publish 出力にありません ($runtime)"
        }
    }

    Invoke-Native "Windows 11 Shell 統合の生成 ($runtime)" {
        pwsh scripts/build-shell-integration.ps1 `
            -Runtime $runtime `
            -PublishDir $publishDir `
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
    $content = $content -replace '(?m)^\| .+\|$', ''
    $content = $content -replace '(?m)^\|[-: ]+\|$', ''
    $content = $content -replace '(?m)^>\s*', ''
    $content = $content -replace '\r?\n{3,}', "`n`n"
    [System.IO.File]::WriteAllText((Join-Path $publishDir 'README.txt'), $content.Trim(), [System.Text.Encoding]::UTF8)

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
            --shortcuts 'StartMenuRoot,Desktop' `
            --signParams $SignParams
    }
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
foreach ($f in Get-ChildItem $ArtifactsDir -File) {
    Write-Host "  ↑ $($f.Name)"
    Invoke-Native "R2 put ($($f.Name))" {
        pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$($f.Name)" --file $f.FullName --remote
    }
    $uploaded++
}
Write-Host "✅ R2 アップロード完了: $uploaded ファイル"

# ---- 2.5 Cloudflare エッジキャッシュのパージ ----
# 固定名ファイル (Setup.exe / Portable.zip / RELEASES / releases.*.json / assets.*.json) は
# 毎リリースで中身が変わるため、キャッシュ回避付き GET と SHA256 で配信実体を照合する。
# 不一致を確認した URL だけをパージし、再照合が通るまで公開完了とは扱わない。
function Test-PublishedArtifact([System.IO.FileInfo]$File) {
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    try {
        $url = "$BaseUrl/$($File.Name)?release_check=$([Guid]::NewGuid().ToString('N'))"
        $bytes = $client.GetByteArrayAsync($url).GetAwaiter().GetResult()
        $remoteHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
        return $bytes.LongLength -eq $File.Length -and $remoteHash -eq (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
    } finally {
        $client.Dispose()
    }
}

Write-Host '== Cloudflare キャッシュパージ ==' -ForegroundColor Cyan
# $zoneId / $cfHeaders はプリフライト (Cloudflare トークン取得時) で解決・検証済み
$staleFiles = @(Get-ChildItem $ArtifactsDir -File | Where-Object { $_.Name -notlike '*.nupkg' } | Where-Object { -not (Test-PublishedArtifact $_) })
$purgeUrls = @($staleFiles | ForEach-Object { "$BaseUrl/$($_.Name)" })
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
        foreach ($file in $staleFiles) {
            if (-not (Test-PublishedArtifact $file)) { throw "配信内容が一致しません: $($file.Name)" }
        }
    } catch {
        throw "アップロード済みですが、配信確認が完了していません: $($_.Exception.Message)"
    }
} else {
    Write-Host '  パージ対象なし'
}

# ---- 3. 配信確認 (CDN/edge 伝播チェック) ----
Write-Host '== 配信確認 ==' -ForegroundColor Cyan
foreach ($runtime in $Runtimes) {
    $channel = $RuntimeMatrix[$runtime].Channel
    $url = "$BaseUrl/releases.$channel.json"
    $resp = Invoke-WebRequest -Uri $url -TimeoutSec 30 -MaximumRetryCount 3 -RetryIntervalSec 5
    Write-Host "  $url → HTTP $($resp.StatusCode) ($($resp.RawContentLength) bytes)"
}

# ---- 4. 旧バージョン nupkg のクリーンアップ (Aggressive 戦略) ----
# ローカル artifacts の manifest (= 今アップロードしたものと同一) から keep set を作り、
# R2 上の「.nupkg かつ manifest 外」だけを削除する。固定ファイル名 (Setup.exe /
# Portable.zip / RELEASES* / assets.*.json / releases.*.json) は対象外なので安全。
Write-Host '== 旧 nupkg クリーンアップ ==' -ForegroundColor Cyan
$keep = @{}
$manifests = Get-ChildItem $ArtifactsDir -Filter 'releases.*.json'
if (-not $manifests) { throw 'artifacts に releases.*.json が見つかりません' }
foreach ($m in $manifests) {
    foreach ($asset in (Get-Content $m.FullName -Raw | ConvertFrom-Json).Assets) {
        if ($asset.FileName) { $keep[$asset.FileName] = $true }
    }
}
Write-Host "  保持対象 nupkg: $($keep.Count) 件"

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

# ---- 5. packages.lock.json のクリーンアップ (リリース後の working tree clean 化) ----
# RID 付き publish (dotnet publish -r <rid>) は packages.lock.json を汚染する:
# RID 固有セクション (net10.0/win-arm64 等) や Native AOT 用の依存
# (Microsoft.DotNet.ILCompiler / Microsoft.NET.ILLink.Tasks) が書き足され、Debug 専用
# パッケージが落ちることがある。この汚染 lock は CI の RestoreLockedMode で NU1004 になり、
# lockfile の supply-chain diff 検知も publish churn で無意味化する。
# --force-evaluate で RID なし clean 状態に戻し、毎回 working tree が clean な状態でリリースを終える
# (/vava のリリース後 lock drift 手動 commit/revert が不要になる)。
# 成果物は既にアップロード済みなので、ここでの失敗はリリースには影響しない → warning に留めて継続する。
Write-Host '== packages.lock.json クリーンアップ ==' -ForegroundColor Cyan
try {
    Invoke-Native 'dotnet restore --force-evaluate' {
        dotnet restore Lhamiel.slnx --force-evaluate
    }
    # clean lock の検証: locked-mode 復元が NU1004 ゼロで通れば RID なし clean 状態
    Invoke-Native 'dotnet restore --locked-mode (clean lock 検証)' {
        dotnet restore Lhamiel.slnx --locked-mode
    }
    Write-Host '  ✅ packages.lock.json は RID なし clean 状態 (locked-mode 検証 OK)'
} catch {
    Write-Warning "  packages.lock.json のクリーンアップに失敗しました (アップロード済みリリースには影響なし)。手動で 'dotnet restore Lhamiel.slnx --force-evaluate' を実行してください — $($_.Exception.Message)"
}

Write-Host "`n🎉 リリース完了: v$version → $BaseUrl" -ForegroundColor Green
