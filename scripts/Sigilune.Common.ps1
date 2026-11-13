# Sigilune.Common.ps1 — Certum SimplySign のクラウド署名を Sigilune で行う共通関数。
#
# Sigilune (https://github.com/1llum1n4t1s/Sigilune) は signtool の /dlib として
# クラウド鍵で署名するため、SimplySign Desktop の接続やワンタイムコードの手入力が要らない。
# 事前に `sigilune setup` で Certum アカウントと TOTP 登録情報を資格情報マネージャーへ登録しておく。
#
# GitHub Actions では `uses: 1llum1n4t1s/Sigilune@v1` が PATH へ導入し、資格情報は
# Environment "release" の Secrets を環境変数 SIGILUNE_EMAIL / SIGILUNE_TOTP_URI で渡す。
#
# 実行ファイルの場所は 環境変数 SIGILUNE_EXE → PATH 上の sigilune.exe → %USERPROFILE%\.local\Sigilune\bin\sigilune.exe の順。
# 同じフォルダーに sigilune_dlib.dll が必要。

# Sigilune の実行ファイルを解決する（見つからなければ導入手順を示して停止する）。
function Resolve-SigiluneExe {
    $onPath = Get-Command 'sigilune.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $candidate = if ($env:SIGILUNE_EXE) { $env:SIGILUNE_EXE }
        elseif ($onPath) { $onPath.Source }
        else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local\Sigilune\bin\sigilune.exe' }
    if (-not [IO.Path]::IsPathFullyQualified($candidate) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Sigilune が見つかりません: $candidate`nhttps://github.com/1llum1n4t1s/Sigilune を導入し、sigilune setup で Certum アカウントを登録してください（場所は SIGILUNE_EXE で指定可）。"
    }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $candidate) 'sigilune_dlib.dll') -PathType Leaf)) {
        throw "sigilune_dlib.dll が sigilune.exe と同じフォルダーにありません: $candidate"
    }
    return $candidate
}

# Certum クラウドのセッションを用意する（キャッシュが有効ならログインしない）。署名前に呼び、失敗を早く検出する。
function Connect-Sigilune {
    param([Parameter(Mandatory)][string]$SigiluneExe)
    & $SigiluneExe login
    if ($LASTEXITCODE -ne 0) { throw "Sigilune のログインに失敗しました (exit $LASTEXITCODE)。sigilune status で登録を確認してください。自動再試行はしません。" }
}

# ファイルへ署名し、Sigilune の検証（有効・署名者一致・タイムスタンプ）に加えて署名者の CN を照合する。
function Invoke-SigiluneSign {
    param(
        [Parameter(Mandatory)][string]$SigiluneExe,
        [Parameter(Mandatory)][string[]]$Path,
        [Parameter(Mandatory)][string]$CertificateSubjectName
    )
    & $SigiluneExe sign @Path
    if ($LASTEXITCODE -ne 0) { throw "Sigilune の署名に失敗しました (exit $LASTEXITCODE): $($Path -join ', ')" }
    foreach ($file in $Path) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike "CN=$CertificateSubjectName*" -or -not $signature.TimeStamperCertificate) {
            throw "署名検証に失敗しました: $file → $($signature.Status)"
        }
    }
}
