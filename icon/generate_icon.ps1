# .NETを使用してPNGファイルからICOファイルを生成するスクリプト
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 256)

function Create-IconFromPng {
    <#
    .SYNOPSIS
    PNGファイルからマルチサイズのICOファイルを生成します。
    #>
    param(
        # [string] PNGファイルのパス
        [string]$PngPath,
        # [string] 出力するICOファイルのパス
        [string]$OutputIcoPath
    )

    # PNGファイルの存在確認
    if (-not (Test-Path $PngPath)) {
        Write-Host "ERROR: PNGファイルが見つかりません: $PngPath" -ForegroundColor Red
        return $false
    }

    Write-Host "Processing: $PngPath -> $OutputIcoPath"

    # リソース管理用の変数初期化
    $sourceImage = $null
    $baseBitmap = $null
    $resizedBitmaps = @()
    $icoFileStream = $null
    $binaryWriter = $null

    try {
        # ソース画像の読み込み
        $sourceImage = [System.Drawing.Image]::FromFile($PngPath)
        $baseBitmap = New-Object System.Drawing.Bitmap($sourceImage)
        
        # PNGデータのキャッシュ
        $pngDataCache = @{}

        # 各サイズのリサイズ画像生成とPNGデータ変換
        foreach ($size in $sizes) {
            # 新しいビットマップの作成
            $resizedBitmap = New-Object System.Drawing.Bitmap($size, $size)
            $resizedBitmaps += $resizedBitmap
            
            # グラフィックスオブジェクトの生成と描画
            $graphics = [System.Drawing.Graphics]::FromImage($resizedBitmap)
            try {
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImage($baseBitmap, 0, 0, $size, $size)
            }
            finally {
                # グラフィックスリソースの解放
                if ($null -ne $graphics) { $graphics.Dispose() }
            }

            # メモリストリームを使用してPNG形式に変換しキャッシュ
            $memoryStream = New-Object System.IO.MemoryStream
            try {
                $resizedBitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngDataCache[$size] = $memoryStream.ToArray()
            }
            finally {
                # ストリームの解放
                if ($null -ne $memoryStream) { $memoryStream.Dispose() }
            }
        }

        # ICOファイルの作成（一時ファイルに書き、完了後に置換して使用中ファイルの部分更新を防ぐ）
        $tempIcoPath = "$OutputIcoPath.tmp"
        $icoFileStream = [System.IO.File]::Create($tempIcoPath)
        $binaryWriter = New-Object System.IO.BinaryWriter($icoFileStream)

        # ICOヘッダーの書き込み
        # 予約済み(2bytes: 0), タイプ(2bytes: 1=Icon), 画像数(2bytes)
        $binaryWriter.Write([byte]0); $binaryWriter.Write([byte]0)
        $binaryWriter.Write([byte]1); $binaryWriter.Write([byte]0)
        $binaryWriter.Write([System.Int16]$sizes.Count)

        # ディレクトリエントリの計算と書き込み
        # ヘッダー(6bytes) + エントリ(16bytes * 画像数)
        $dataOffset = 6 + (16 * $sizes.Count)
        foreach ($size in $sizes) {
            $pngData = $pngDataCache[$size]
            # 256pxの場合は0として記録
            $dim = if ($size -eq 256) { [byte]0 } else { [byte]$size }

            # 幅(1byte), 高さ(1byte), カラーパレット(1byte: 0), 予約済み(1byte: 0)
            $binaryWriter.Write($dim); $binaryWriter.Write($dim)
            $binaryWriter.Write([byte]0); $binaryWriter.Write([byte]0)
            # カラープレーン(2bytes: 1), ビット深度(2bytes: 32)
            $binaryWriter.Write([System.Int16]1); $binaryWriter.Write([System.Int16]32)
            # データサイズ(4bytes)
            $binaryWriter.Write([System.Int32]$pngData.Length)
            # データオフセット(4bytes)
            $binaryWriter.Write([System.Int32]$dataOffset)
            
            $dataOffset += $pngData.Length
        }

        # PNG画像データの書き込み
        foreach ($size in $sizes) {
            $binaryWriter.Write($pngDataCache[$size])
        }

        $binaryWriter.Flush()
        $icoFileStream.Flush()
        if ($null -ne $binaryWriter) { $binaryWriter.Close() }
        if ($null -ne $icoFileStream) { $icoFileStream.Close() }
        $binaryWriter = $null
        $icoFileStream = $null

        if (Test-Path $OutputIcoPath) {
            [System.IO.File]::Replace($tempIcoPath, $OutputIcoPath, "$OutputIcoPath.bak")
            if (Test-Path "$OutputIcoPath.bak") { Remove-Item "$OutputIcoPath.bak" -Force }
        } else {
            Move-Item -Path $tempIcoPath -Destination $OutputIcoPath -Force
        }

        Write-Host "✓ Successfully created: $OutputIcoPath with sizes: $($sizes -join ', ')" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
    finally {
        # すべてのリソースを確実に解放（try内で既にClose済みの場合はスキップ）
        if ($null -ne $binaryWriter) { try { $binaryWriter.Close() } catch { } }
        if ($null -ne $icoFileStream) { try { $icoFileStream.Close() } catch { } }
        foreach ($bmp in $resizedBitmaps) {
            if ($null -ne $bmp) { try { $bmp.Dispose() } catch { } }
        }
        if ($null -ne $baseBitmap) { try { $baseBitmap.Dispose() } catch { } }
        if ($null -ne $sourceImage) { try { $sourceImage.Dispose() } catch { } }
    }
}

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if (-not $scriptDir) {
    Write-Host "ERROR: スクリプトディレクトリを確認できません" -ForegroundColor Red
    exit 1
}

$appIconPath = Join-Path -Path $scriptDir -ChildPath "app_icon.png"
$appIcoPath = Join-Path -Path $scriptDir -ChildPath "app.ico"

$allSuccess = $true

if (Test-Path $appIconPath) {
    # メソッド呼び出し: アイコン生成の実行
    $success = Create-IconFromPng -PngPath $appIconPath -OutputIcoPath $appIcoPath
    if (-not $success) { $allSuccess = $false }
} else {
    Write-Host "WARNING: app_icon.png が見つかりません: $appIconPath" -ForegroundColor Yellow
}

if ($allSuccess) {
    Write-Host "`nIcon generation completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`nIcon generation completed with some warnings." -ForegroundColor Yellow
}
