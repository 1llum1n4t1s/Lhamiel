param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Channel = "win"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "Lhamiel.csproj"
$publishDir = Join-Path $root "bin/Release/net10.0-windows10.0.26100.0/win-x64/publish"
$releaseDir = Join-Path $root "Releases"
$iconPath = Join-Path $root "Asset/icon/app.ico"

Write-Host "Publishing to $publishDir" -ForegroundColor Cyan

dotnet publish $project -c Release -r win-x64 --self-contained false

Write-Host "Packing release with vpk" -ForegroundColor Cyan

vpk pack `
    --packId "Lhamiel" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "Lhamiel.exe" `
    --icon $iconPath `
    --outputDir $releaseDir `
    --channel $Channel

Write-Host "Release artifacts created in $releaseDir" -ForegroundColor Green
