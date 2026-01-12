# .NETを使用してPNGファイルからICOファイルを生成するスクリプト
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 256)

function Create-IconFromPng {
    param([string]$PngPath, [string]$OutputIcoPath, [string]$IconName)

    if (-not (Test-Path $PngPath)) {
        Write-Host "ERROR: PNGファイルが見つかりません: $PngPath" -ForegroundColor Red
        return $false
    }

    Write-Host "Processing: $PngPath -> $OutputIcoPath"

    try {
        $sourceBitmap = New-Object System.Drawing.Bitmap($PngPath)
        $images = @{}

        foreach ($size in $sizes) {
            $resizedBitmap = New-Object System.Drawing.Bitmap($size, $size)
            $graphics = [System.Drawing.Graphics]::FromImage($resizedBitmap)
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($sourceBitmap, 0, 0, $size, $size)
            $graphics.Dispose()
            $images[$size] = $resizedBitmap
        }

        $icoFile = [System.IO.File]::Create($OutputIcoPath)
        $writer = New-Object System.IO.BinaryWriter($icoFile)

        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]1)
        $writer.Write([byte]0)
        $writer.Write([System.Int16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        foreach ($size in $sizes) {
            $bitmap = $images[$size]
            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngData = $stream.ToArray()

            $w = if ($size -eq 256) { [byte]0 } else { [byte]$size }
            $h = if ($size -eq 256) { [byte]0 } else { [byte]$size }

            $writer.Write($w)
            $writer.Write($h)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([System.Int16]1)
            $writer.Write([System.Int16]32)
            $writer.Write([System.Int32]$pngData.Length)
            $writer.Write([System.Int32]$offset)

            $offset += $pngData.Length
            $stream.Dispose()
        }

        foreach ($size in $sizes) {
            $bitmap = $images[$size]
            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngData = $stream.ToArray()
            $writer.Write($pngData)
            $stream.Dispose()
        }

        $writer.Close()
        $icoFile.Close()

        foreach ($bitmap in $images.Values) {
            $bitmap.Dispose()
        }
        $sourceBitmap.Dispose()

        Write-Host "✓ Successfully created: $OutputIcoPath with sizes: $($sizes -join ', ')" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "ERROR: $($_)" -ForegroundColor Red
        return $false
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

$allSuccess = $true

if (Test-Path "app_icon.png") {
    $success = Create-IconFromPng -PngPath "app_icon.png" -OutputIcoPath "app.ico" -IconName "アプリケーション"
    if (-not $success) { $allSuccess = $false }
} else {
    Write-Host "WARNING: app_icon.png が見つかりません" -ForegroundColor Yellow
}

if (Test-Path "file_icon.png") {
    $success = Create-IconFromPng -PngPath "file_icon.png" -OutputIcoPath "file.ico" -IconName "ファイル関連付け"
    if (-not $success) { $allSuccess = $false }
} else {
    Write-Host "WARNING: file_icon.png が見つかりません" -ForegroundColor Yellow
}

Pop-Location

if ($allSuccess) {
    Write-Host "`nIcon generation completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`nIcon generation completed with some warnings." -ForegroundColor Yellow
}
