param(
    [Parameter(Mandatory = $false)]
    [string]$Version,
    [string]$Channel = "release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$csprojPath = Join-Path $root "Lhamiel.csproj"
$project = $csprojPath
$publishDir = Join-Path $root "bin/Release/net10.0-windows8.0/win-x64/publish"
$releaseDir = Join-Path $root "Releases"
$iconPath = Join-Path $root "icon/app.ico"

# バージョンが指定されていない場合はcsprojから取得
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Version not specified, reading from csproj..." -ForegroundColor Yellow
    [xml]$xml = Get-Content -Path $csprojPath

    # すべての PropertyGroup から Version を検索
    foreach ($propertyGroup in $xml.Project.PropertyGroup) {
        if ($propertyGroup.Version) {
            $Version = $propertyGroup.Version
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Error "Version not found in csproj at: $csprojPath"
        exit 1
    }

    Write-Host "Version read from csproj: $Version" -ForegroundColor Green
}

Write-Host "Publishing to $publishDir" -ForegroundColor Cyan
Write-Host "  Project: $project"
Write-Host "  Version: $Version"
Write-Host "  Channel: $Channel"

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Packing release with vpk" -ForegroundColor Cyan

if (Test-Path $releaseDir) {
    Remove-Item -Path $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

vpk pack `
    --packId "Lhamiel" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "Lhamiel.exe" `
    --icon $iconPath `
    --outputDir $releaseDir `
    --channel $Channel `
    --shortcuts "StartMenu,Desktop"

if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Release artifacts created in $releaseDir" -ForegroundColor Green
Get-ChildItem -Path $releaseDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName)" }
