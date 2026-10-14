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

    [switch]$SkipSigning,

    [string]$WorkDirectory
)



# 指定した作業出力は絶対パスの外部ディレクトリに限定し、リンク経由の書込みも拒否する。
function Resolve-ExternalWorkDirectory {
    param([string]$Path, [string]$RepositoryRoot)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathFullyQualified($Path)) { throw 'WorkDirectory は絶対パスで指定してください。' }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $forbiddenRoots = @($RepositoryRoot, (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'))
    if ($env:CODEX_HOME) { $forbiddenRoots += $env:CODEX_HOME }
    foreach ($root in $forbiddenRoots) {
        $boundary = [IO.Path]::GetFullPath($root).TrimEnd('\','/')
        if ($resolved.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "WorkDirectory はリポジトリ / Codex ディレクトリの外に指定してください: $resolved" }
    }
    $ancestor = $resolved
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "WorkDirectory の祖先は通常のディレクトリに限定します: $ancestor" }
        }
        $ancestor = Split-Path $ancestor -Parent
    }
    if (Test-Path -LiteralPath $resolved) {
        if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force -ErrorAction Stop | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "WorkDirectory 内のリンクを除いてください: $resolved" }
    }
    return $resolved
}

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

$nativeIntermediate = Join-Path $repoRoot "src\Lhamiel.ShellExtension\obj\$platform\Release"
if ($WorkDirectory) {
    $shellWork = Resolve-ExternalWorkDirectory -Path $WorkDirectory -RepositoryRoot $repoRoot
    if ((Test-Path -LiteralPath $shellWork) -and @(Get-ChildItem -LiteralPath $shellWork -Force).Count) {
        throw "Shell WorkDirectory が空ではありません: $shellWork。PRUNLET.md に従い Prunlet CLI を直接 CMD から実行して清掃するか、新しいディレクトリを指定してください。"
    }
    $publishPath = Resolve-ExternalWorkDirectory -Path $publishPath -RepositoryRoot $repoRoot
    $nativeOutput = Join-Path $shellWork 'bin'
    $nativeIntermediate = Join-Path $shellWork 'obj'
    $packageWorkDirectory = Join-Path $shellWork 'Package'
    $packageOutput = Join-Path $shellWork 'Lhamiel.ContextMenu.msix'
}
if (Test-Path -LiteralPath $packageOutput) {
    throw "既存の MSIX は自動削除しません: $packageOutput。PRUNLET.md に従い Prunlet CLI を直接 CMD から実行して清掃してください。"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "vswhere.exe が見つかりません: $vswhere" }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    $vsPath = & $vswhere -latest -prerelease -products * -property installationPath | Select-Object -First 1
    if ($vsPath) {
        $candidate = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
        if (Test-Path -LiteralPath $candidate) { $msbuild = $candidate }
    }
}
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
$vsRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $msbuild)))
$toolsetPath = Join-Path $vsRoot "MSBuild\Microsoft\VC\v180\Platforms\$platform\PlatformToolsets\v145"
if (Test-Path -LiteralPath $toolsetPath) {
    $buildArguments = @($projectPath, '/t:Build', '/p:Configuration=Release', "/p:Platform=$platform", '/nologo', '/verbosity:minimal')
    if ($WorkDirectory) { $buildArguments += "/p:OutDir=$nativeOutput\", "/p:IntDir=$nativeIntermediate\" }
    & $msbuild @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Shell 拡張のビルドに失敗しました (exit $LASTEXITCODE)" }
} else {
    # VS のプラットフォーム定義だけが欠けている場合、同じ既存 MSVC で直接ビルドする。
    # vcxproj の Release 設定（C++20 / W4 / WX / MT / LTCG）と export 定義を維持する。
    $devShellModule = Join-Path $vsRoot 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
    Import-Module $devShellModule -ErrorAction Stop
    Enter-VsDevShell -VsInstallPath $vsRoot -SkipAutomaticLocation -DevCmdArguments "-arch=$($platform.ToLowerInvariant()) -host_arch=x64"
    $compiler = (Get-Command cl.exe -ErrorAction Stop).Source
    New-Item -ItemType Directory -Path $nativeOutput, $nativeIntermediate -Force | Out-Null
    $sourcePath = Join-Path $repoRoot 'src\Lhamiel.ShellExtension\ShellExtension.cpp'
    $definitionPath = Join-Path $repoRoot 'src\Lhamiel.ShellExtension\dll.def'
    & $compiler /nologo /LD /O2 /GL /Gy /W4 /WX /std:c++20 /permissive- /EHsc /MT /utf-8 `
        /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /DNDEBUG `
        "/Fo$(Join-Path $nativeIntermediate 'ShellExtension.obj')" "/Fd$(Join-Path $nativeIntermediate 'ShellExtension.pdb')" $sourcePath `
        /link "/IMPLIB:$(Join-Path $nativeOutput 'Lhamiel.ShellExtension.lib')" "/OUT:$(Join-Path $nativeOutput 'Lhamiel.ShellExtension.dll')" "/DEF:$definitionPath" `
        /SUBSYSTEM:WINDOWS "/MACHINE:$platform" /LTCG /OPT:REF /OPT:ICF /DEBUG "/PDB:$(Join-Path $nativeOutput 'Lhamiel.ShellExtension.pdb')" "/MANIFESTFILE:$(Join-Path $nativeIntermediate 'Lhamiel.ShellExtension.dll.intermediate.manifest')" shell32.lib ole32.lib user32.lib uuid.lib
    if ($LASTEXITCODE -ne 0) { throw "Shell 拡張の直接ビルドに失敗しました (exit $LASTEXITCODE)" }
}

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

# sparse package は実体を外部配置先から解決するため、MakeAppx のファイル存在検証を無効化する。
& $makeAppx pack /d $packageWorkDirectory /p $packageOutput /nv
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
