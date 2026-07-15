# Windows 11 の新しい右クリックメニュー向けに、IExplorerCommand DLL と
# 外部配置（sparse）MSIX を生成して publish 出力へ追加する。

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [Parameter(Mandatory)]
    [string]$PublishDir,

    [string]$CertificateSubjectName = 'Open Source Developer Yuichiro Shinozaki',

    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishPath = [System.IO.Path]::GetFullPath($PublishDir, $repoRoot)
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "publish ディレクトリが見つかりません: $publishPath"
}

$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
$projectPath = Join-Path $repoRoot 'src\Lhamiel.ShellExtension\Lhamiel.ShellExtension.vcxproj'
$packageSource = Join-Path $repoRoot 'src\Lhamiel.ShellExtension\Package'
$nativeOutput = Join-Path $repoRoot "src\Lhamiel.ShellExtension\bin\$platform\Release"
$packageWorkDirectory = Join-Path $repoRoot "src\Lhamiel.ShellExtension\obj\$platform\Release\Package"
$packageOutput = Join-Path $repoRoot "src\Lhamiel.ShellExtension\obj\$platform\Release\Lhamiel.ContextMenu.msix"

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "vswhere.exe が見つかりません: $vswhere" }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio の MSBuild.exe が見つかりません' }

$windowsKitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdkBin = Get-ChildItem -LiteralPath $windowsKitsBin -Directory |
    Where-Object { $_.Name -match '^10\.0\.' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK の bin ディレクトリが見つかりません' }
$makeAppx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signTool = Join-Path $sdkBin.FullName 'x64\signtool.exe'
foreach ($tool in $makeAppx, $signTool) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Windows SDK ツールが見つかりません: $tool" }
}

Write-Host "== Shell 拡張ビルド: $Runtime ==" -ForegroundColor Cyan
& $msbuild $projectPath /t:Build /p:Configuration=Release /p:Platform=$platform /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "Shell 拡張のビルドに失敗しました (exit $LASTEXITCODE)" }

$nativeDll = Join-Path $nativeOutput 'Lhamiel.ShellExtension.dll'
if (-not (Test-Path -LiteralPath $nativeDll)) { throw "Shell 拡張 DLL が見つかりません: $nativeDll" }

if (-not $SkipSigning) {
    & $signTool sign /n $CertificateSubjectName /fd SHA256 /td SHA256 /tr http://time.certum.pl $nativeDll
    if ($LASTEXITCODE -ne 0) { throw "Shell 拡張 DLL の署名に失敗しました (exit $LASTEXITCODE)" }
}

$versionNode = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props に Version が見つかりません'
}
$versionParts = @($versionNode.InnerText.Trim().Split('.') | ForEach-Object { [int]$_ })
if ($versionParts.Count -lt 1 -or $versionParts.Count -gt 4 -or @($versionParts | Where-Object { $_ -lt 0 -or $_ -gt 65535 }).Count -gt 0) {
    throw "sparse MSIX に変換できないバージョンです: $($versionNode.InnerText)"
}
while ($versionParts.Count -lt 4) { $versionParts += 0 }
$packageVersion = $versionParts -join '.'

New-Item -ItemType Directory -Path $packageWorkDirectory -Force | Out-Null
$packageManifest = [xml](Get-Content (Join-Path $packageSource 'AppxManifest.xml') -Raw)
$packageManifest.Package.Identity.Version = $packageVersion
$manifestPath = Join-Path $packageWorkDirectory 'AppxManifest.xml'
$writerSettings = [System.Xml.XmlWriterSettings]::new()
$writerSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writerSettings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $writerSettings)
try { $packageManifest.Save($writer) } finally { $writer.Dispose() }

if (Test-Path -LiteralPath $packageOutput) { Remove-Item -LiteralPath $packageOutput -Force }
# sparse package は実体を外部配置先から解決するため、MakeAppx のファイル存在検証を無効化する。
& $makeAppx pack /d $packageWorkDirectory /p $packageOutput /o /nv
if ($LASTEXITCODE -ne 0) { throw "sparse MSIX の生成に失敗しました (exit $LASTEXITCODE)" }

if (-not $SkipSigning) {
    & $signTool sign /n $CertificateSubjectName /fd SHA256 /td SHA256 /tr http://time.certum.pl $packageOutput
    if ($LASTEXITCODE -ne 0) { throw "sparse MSIX の署名に失敗しました (exit $LASTEXITCODE)" }
}

$assetDirectory = Join-Path $publishPath 'Assets\Lhamiel'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $publishPath 'Lhamiel.ShellExtension.dll') -Force
Copy-Item -LiteralPath $packageOutput -Destination (Join-Path $publishPath 'Lhamiel.ContextMenu.msix') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\Lhamiel\icon\app_icon.png') `
    -Destination (Join-Path $assetDirectory 'app_icon.png') -Force

if (-not $SkipSigning) {
    foreach ($signedFile in $nativeDll, $packageOutput) {
        $signature = Get-AuthenticodeSignature -LiteralPath $signedFile
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "CN=$CertificateSubjectName*") {
            throw "署名検証に失敗しました: $signedFile → $($signature.Status)"
        }
    }
}

Write-Host "  ✅ Lhamiel.ShellExtension.dll / Lhamiel.ContextMenu.msix を追加しました"
