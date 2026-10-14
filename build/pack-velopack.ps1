param(
    [Parameter(Mandatory = $false)]
    [string]$Version,
    [string]$Channel = "release"
)



# 検証済みの作業対象だけを標準APIで削除する。
function Remove-LocalWorkItem {
    param([string[]]$LiteralPath, [string]$AllowedRoot, [switch]$IgnoreMissing, [switch]$AllowLeafJunction)
    if ([string]::IsNullOrWhiteSpace($AllowedRoot) -or -not [IO.Path]::IsPathRooted($AllowedRoot)) { throw 'Cleanup root must be absolute.' }
    $boundary = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($target in $LiteralPath) {
        if ([string]::IsNullOrWhiteSpace($target) -or -not [IO.Path]::IsPathRooted($target)) { throw 'Cleanup target must be absolute.' }
        $resolved = [IO.Path]::GetFullPath($target)
        if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target is outside the allowed root.' }
        $ancestor = $resolved
        while ($ancestor) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and -not ($AllowLeafJunction -and $ancestor -eq $resolved -and $item.LinkType -eq 'Junction')) { throw 'Cleanup path contains a reparse point.' }
            }
            $ancestor = Split-Path $ancestor -Parent
        }
        if (-not (Test-Path -LiteralPath $resolved)) { if ($IgnoreMissing) { continue }; throw "Cleanup target is missing: $resolved" }
        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        $leafLink = [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
        if ($item.PSIsContainer -and -not $leafLink) {
            if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force -ErrorAction Stop | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Cleanup directory contains a reparse point.' }
        }
        Remove-Item -LiteralPath $resolved -Recurse:($item.PSIsContainer -and -not $leafLink) -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $resolved) { throw "Cleanup target remains: $resolved" }
    }
}

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

    Remove-LocalWorkItem -LiteralPath $publishDir -AllowedRoot $root.Path
}

dotnet publish $project -c Release -r win-x64 --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Packing release with vpk" -ForegroundColor Cyan

if (Test-Path $releaseDir) {

    Remove-LocalWorkItem -LiteralPath $releaseDir -AllowedRoot $root.Path
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
    --shortcuts "StartMenuRoot,Desktop"

if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Release artifacts created in $releaseDir" -ForegroundColor Green
Get-ChildItem -Path $releaseDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName)" }
