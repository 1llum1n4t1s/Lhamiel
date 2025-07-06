# SVG to ICO converter using ImageMagick
# 必要なサイズのPNGファイルを生成し、ICOファイルにまとめる

# サイズの配列
$sizes = @(16, 32, 48, 256)

# 一時ディレクトリを作成
$tempDir = "temp_icons"
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir

# SVGから各サイズのPNGを生成
foreach ($size in $sizes) {
    $outputFile = "$tempDir\icon_${size}x${size}.png"
    
    # ImageMagickを使用してSVGからPNGに変換
    magick convert "icon.svg" -resize "${size}x${size}" -background transparent $outputFile
    
    if (Test-Path $outputFile) {
        Write-Host "Generated: $outputFile"
    } else {
        Write-Host "Failed to generate: $outputFile"
    }
}

# PNGファイルをICOファイルにまとめる
$pngFiles = Get-ChildItem "$tempDir\*.png" | Sort-Object Name
if ($pngFiles.Count -gt 0) {
    $icoFiles = $pngFiles | ForEach-Object { $_.FullName }
    $icoFilesString = $icoFiles -join " "
    
    magick convert $icoFilesString "app.ico"
    
    if (Test-Path "app.ico") {
        Write-Host "Successfully created: app.ico"
    } else {
        Write-Host "Failed to create ICO file"
    }
}

# 一時ディレクトリを削除
Remove-Item $tempDir -Recurse -Force

Write-Host "Icon generation completed!" 