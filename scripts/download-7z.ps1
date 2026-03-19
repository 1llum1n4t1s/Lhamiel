# 7-Zip 公式サイトから 7z.dll をダウンロードするスクリプト
# 使用方法: pwsh scripts/download-7z.ps1

param(
    [string]$Version = "2600",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$architectures = @(
    @{ Rid = "win-x64";  Suffix = "x64" },
    @{ Rid = "win-arm64"; Suffix = "arm64" }
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repoRoot "Lhamiel.slnx"))) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "7z-download-$Version"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# 展開用の 7zr.exe を取得（7-Zip がインストールされていない環境用）
function Get-SevenZipExtractor {
    # 1. システムにインストール済みの 7-Zip
    $systemPath = "C:\Program Files\7-Zip\7z.exe"
    if (Test-Path $systemPath) { return $systemPath }

    $cmdPath = Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ($cmdPath) { return $cmdPath }

    # 2. 7zr.exe（スタンドアロンコンソール版）をダウンロード
    $sevenZrPath = Join-Path $tempDir "7zr.exe"
    if (-not (Test-Path $sevenZrPath)) {
        $url = "https://www.7-zip.org/a/7zr.exe"
        Write-Host "[DL] 展開ツール: $url ..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $url -OutFile $sevenZrPath -UseBasicParsing
    }
    return $sevenZrPath
}

$extractor = Get-SevenZipExtractor

foreach ($arch in $architectures) {
    $rid = $arch.Rid
    $suffix = $arch.Suffix
    $targetDir = Join-Path $repoRoot "lib" "native" $rid
    $targetDll = Join-Path $targetDir "7z.dll"

    if ((Test-Path $targetDll) -and -not $Force) {
        Write-Host "[SKIP] $targetDll は既に存在します（-Force で上書き）" -ForegroundColor Yellow
        continue
    }

    $url = "https://www.7-zip.org/a/7z${Version}-${suffix}.exe"
    $installerPath = Join-Path $tempDir "7z${Version}-${suffix}.exe"

    Write-Host "[DL] $url ..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $installerPath -UseBasicParsing

    # 7-Zip インストーラーは 7z 自己展開アーカイブ
    $extractDir = Join-Path $tempDir "extract-$suffix"
    if (Test-Path $extractDir) { Remove-Item -Path $extractDir -Recurse -Force }

    Write-Host "[EXTRACT] $installerPath -> $extractDir" -ForegroundColor Cyan
    & $extractor x $installerPath -o"$extractDir" -y | Out-Null

    $extractedDll = Join-Path $extractDir "7z.dll"
    if (-not (Test-Path $extractedDll)) {
        Write-Error "展開結果に 7z.dll が見つかりません: $extractDir"
        Get-ChildItem -Path $extractDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName)" }
        exit 1
    }

    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -Path $extractedDll -Destination $targetDll -Force
    Write-Host "[OK] $targetDll" -ForegroundColor Green
}

# クリーンアップ
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}

Write-Host ""
Write-Host "完了！lib/native/ の内容:" -ForegroundColor Green
Get-ChildItem -Path (Join-Path $repoRoot "lib" "native") -Recurse -File | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.FullName) (${size} MB)"
}
