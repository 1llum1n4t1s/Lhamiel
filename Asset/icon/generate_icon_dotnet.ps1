# .NETを使用してICOファイルを生成するスクリプト

Add-Type -AssemblyName System.Drawing

# アイコンのサイズ
$sizes = @(16, 32, 48, 256)

# 各サイズの画像を生成する関数
function Create-IconImage {
    param([int]$size)
    
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # 高品質設定
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    
    # 背景を透明に設定
    $graphics.Clear([System.Drawing.Color]::Transparent)
    
    # スケール係数を計算
    $scale = $size / 256.0
    
    # 氷の四角形を描画
    $iceBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 179, 229, 252))
    $icePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 3, 169, 244), [Math]::Max(1, 3 * $scale))
    $iceRect = [System.Drawing.Rectangle]::new([Math]::Round(48 * $scale), [Math]::Round(48 * $scale), [Math]::Round(160 * $scale), [Math]::Round(160 * $scale))
    $graphics.FillRectangle($iceBrush, $iceRect)
    $graphics.DrawRectangle($icePen, $iceRect)
    
    # 内側の氷の四角形
    $innerIceBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 225, 245, 254))
    $innerIcePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 41, 182, 246), [Math]::Max(1, 2 * $scale))
    $innerIceRect = [System.Drawing.Rectangle]::new([Math]::Round(56 * $scale), [Math]::Round(56 * $scale), [Math]::Round(144 * $scale), [Math]::Round(144 * $scale))
    $graphics.FillRectangle($innerIceBrush, $innerIceRect)
    $graphics.DrawRectangle($innerIcePen, $innerIceRect)
    
    # フォルダーを描画
    $folderBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 152, 0))
    $folderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 245, 124, 0), [Math]::Max(1, 2 * $scale))
    
    # フォルダーの本体
    $folderRect = [System.Drawing.Rectangle]::new([Math]::Round(88 * $scale), [Math]::Round(88 * $scale), [Math]::Round(80 * $scale), [Math]::Round(80 * $scale))
    $graphics.FillRectangle($folderBrush, $folderRect)
    $graphics.DrawRectangle($folderPen, $folderRect)
    
    # フォルダーの上部（折り返し部分）
    $folderTopBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 183, 77))
    $folderTopRect = [System.Drawing.Rectangle]::new([Math]::Round(88 * $scale), [Math]::Round(88 * $scale), [Math]::Round(20 * $scale), [Math]::Round(20 * $scale))
    $graphics.FillRectangle($folderTopBrush, $folderTopRect)
    $graphics.DrawRectangle($folderPen, $folderTopRect)
    
    # フォルダーの内側
    $folderInnerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 243, 224))
    $folderInnerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 152, 0), [Math]::Max(1, 1 * $scale))
    $folderInnerRect = [System.Drawing.Rectangle]::new([Math]::Round(92 * $scale), [Math]::Round(112 * $scale), [Math]::Round(72 * $scale), [Math]::Round(52 * $scale))
    $graphics.FillRectangle($folderInnerBrush, $folderInnerRect)
    $graphics.DrawRectangle($folderInnerPen, $folderInnerRect)
    
    # フォルダーの線（ディレクトリ構造を表現）
    $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 152, 0), [Math]::Max(1, 2 * $scale))
    $graphics.DrawLine($linePen, [Math]::Round(100 * $scale), [Math]::Round(120 * $scale), [Math]::Round(156 * $scale), [Math]::Round(120 * $scale))
    $graphics.DrawLine($linePen, [Math]::Round(100 * $scale), [Math]::Round(128 * $scale), [Math]::Round(148 * $scale), [Math]::Round(128 * $scale))
    $graphics.DrawLine($linePen, [Math]::Round(100 * $scale), [Math]::Round(136 * $scale), [Math]::Round(140 * $scale), [Math]::Round(136 * $scale))
    $graphics.DrawLine($linePen, [Math]::Round(100 * $scale), [Math]::Round(144 * $scale), [Math]::Round(132 * $scale), [Math]::Round(144 * $scale))
    $graphics.DrawLine($linePen, [Math]::Round(100 * $scale), [Math]::Round(152 * $scale), [Math]::Round(124 * $scale), [Math]::Round(152 * $scale))
    
    # 氷の結晶効果（小さな菱形）
    $crystalBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(153, 129, 212, 250))
    
    # 結晶の位置を計算
    $crystalPositions = @(
        @([Math]::Round(40 * $scale), [Math]::Round(44 * $scale)),
        @([Math]::Round(216 * $scale), [Math]::Round(44 * $scale)),
        @([Math]::Round(40 * $scale), [Math]::Round(220 * $scale)),
        @([Math]::Round(216 * $scale), [Math]::Round(220 * $scale))
    )
    
    foreach ($pos in $crystalPositions) {
        $x, $y = $pos
        $crystalSize = [Math]::Max(1, 4 * $scale)
        $crystalPoints = @(
            [System.Drawing.Point]::new($x, $y),
            [System.Drawing.Point]::new($x + $crystalSize, $y + $crystalSize),
            [System.Drawing.Point]::new($x, $y + 2 * $crystalSize),
            [System.Drawing.Point]::new($x - $crystalSize, $y + $crystalSize)
        )
        $graphics.FillPolygon($crystalBrush, $crystalPoints)
    }
    
    # リソースを解放
    $graphics.Dispose()
    
    return $bitmap
}

# 各サイズの画像を生成
$images = @{}
foreach ($size in $sizes) {
    $images[$size] = Create-IconImage -size $size
}

# ICOファイルを生成
$icoFile = [System.IO.File]::Create("app.ico")
$writer = New-Object System.IO.BinaryWriter($icoFile)

# ICOヘッダー
$writer.Write([byte]0)  # Reserved
$writer.Write([byte]0)  # Reserved
$writer.Write([byte]1)  # Type (1 = ICO)
$writer.Write([byte]0)  # Type
$writer.Write([System.Int16]$sizes.Count) # Count

# 各画像のエントリ情報を書き込み
$offset = 6 + (16 * $sizes.Count) # ヘッダー + エントリ情報のサイズ
foreach ($size in $sizes) {
    $bitmap = $images[$size]
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData = $stream.ToArray()
    
    # エントリ情報
    if ($size -eq 256) {
        $writer.Write([byte]0)  # Width (0 = 256)
    } else {
        $writer.Write([byte]$size)  # Width
    }
    if ($size -eq 256) {
        $writer.Write([byte]0)  # Height (0 = 256)
    } else {
        $writer.Write([byte]$size)  # Height
    }
    $writer.Write([byte]0)      # Color count
    $writer.Write([byte]0)      # Reserved
    $writer.Write([System.Int16]1)     # Planes
    $writer.Write([System.Int16]32)    # Bit count
    $writer.Write([System.Int32]$pngData.Length) # Size
    $writer.Write([System.Int32]$offset) # Offset
    
    $offset += $pngData.Length
    $stream.Dispose()
}

# 各画像のデータを書き込み
foreach ($size in $sizes) {
    $bitmap = $images[$size]
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData = $stream.ToArray()
    $writer.Write($pngData)
    $stream.Dispose()
}

# リソースを解放
$writer.Close()
$icoFile.Close()

foreach ($bitmap in $images.Values) {
    $bitmap.Dispose()
}

Write-Host "Successfully created: app.ico with sizes: $($sizes -join ', ')" 