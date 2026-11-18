# Lhamiel コード署名 (Authenticode) 運用ドキュメント

## 概要

Lhamiel の Setup.exe / Update.exe / 同梱バイナリに Authenticode 署名を付与し、Windows SmartScreen 警告を解消するための運用ガイド。

> **現在の署名経路（2026-11 以降）**: 署名は [Sigilune](https://github.com/1llum1n4t1s/Sigilune) で行う。Certum のクラウド鍵を signtool の `/dlib` から直接使うため、SimplySign Desktop の接続・スマホでのコード入力・`--signParams` は不要になった。手順の正本は `scripts/release-local.ps1` と `scripts/Sigilune.Common.ps1`、および AGENTS.md の Code signing 項。以下の証明書の取得・本人確認・SimplySign アカウントの準備は引き続き有効だが、SimplySign Desktop と signtool `/sha1` を使う署名手順は旧経路の記録として残している。

### 採用構成

| 項目 | 採用 | 理由 |
|------|------|------|
| 証明書ベンダー | **Certum (Open Source Code Signing)** | 個人開発者・OSS プロジェクト向け最安帯 (€49/年) |
| 配信形態 | **SimplySign Cloud** | USB トークン不要、IDnow オンライン本人確認で即日〜翌営業日開始可能 |
| 署名方式 | **GitHub Actions で Sigilune 署名・R2 配信** (`.github/workflows/release.yml` が `scripts/release-local.ps1` を実行) | 鍵はクラウドから動かず、本人の PC を使わずにリリースできる |
| 署名ツール | **signtool.exe** (Windows SDK 同梱) | SimplySign Desktop 経由で Windows Cert Store に登録された証明書を直接利用 |
| 対象バイナリ | Setup.exe / Update.exe / Lhamiel.exe / 同梱 DLL | `vpk pack` が自動で全 PE を署名 |
| タイムスタンプ | `http://time.certum.pl` (RFC 3161) | Certum 公式 TSA |

### 重要な仕様 (2026 年現在)

- **証明書最大有効期間**: **459 日** (2026-02-27 以降の CA/B Forum 規制)。複数年プランは中間で再発行が必要なため **1 年プラン推奨**。
- **月次署名上限**: SimplySign Cloud は **5,000 回/月** (Lhamiel のリリース頻度なら大幅に余裕)。
- **dual signature (SHA1 + SHA256) は不要**: SHA1 ルートは 2020-05 失効済み。SHA256 単独で OK。

---

## 全体構成図

```text
┌─────────────────────────────────────────────────────────────────┐
│  ゆろさん PC (Windows 11)                                       │
│                                                                 │
│  ┌───────────────────┐    ┌──────────────────────────────────┐  │
│  │ /vava スキル        │    │ SimplySign Desktop                │  │
│  │ (PowerShell)      │    │  ├─ Windows Cert Store に Certum  │  │
│  │                   │    │  │   証明書を登録                  │  │
│  │  version bump      │───→│  └─ signtool が透過利用            │  │
│  │  publish + pack    │    │     (PIN は事前ログオン解錠)      │  │
│  │  vpk --signParams │    └──────────┬───────────────────────┘  │
│  │  R2 API upload     │               │                          │
│  └───────────────────┘    ┌──────────▼──────────────────────┐  │
│                           │ ゆろさんのスマホ (SimplySign mobile) │  │
│                           │  └─ プッシュ通知で署名承認 (1 タップ) │  │
│                           └──────────────────────────────────┘  │
└────────────┬───────────────────────────────────────────────────┘
             │ R2Transfer.Core.cs (S3 互換 API)
             ▼
┌─────────────────────────────────────────────────────────────────┐
│ Cloudflare R2 bucket: lhamiel-updates                           │
│  └─ https://lhamiel.kagayoi.com 経由でクライアント配信           │
└─────────────────────────────────────────────────────────────────┘
```

---

## 初回セットアップ (Certum 申込〜署名開始までの一連手順)

### Phase 1: Certum 申込 (所要時間: 10〜30 分)

1. **申込窓口にアクセス**
   - 公式: <https://certum.store/open-source-code-signing-on-simplysign.html> ($58 USD)
   - or 公式 EU: <https://shop.certum.eu/code-signing.html> → "Open Source Cloud" 選択 (€49 EUR)
   - 為替次第で安い方を選択 (両方とも同じ Certum 直販)

2. **入力情報**

   | 項目 | 内容 |
   |------|------|
   | 氏名 | **パスポート英字表記と完全一致** (姓名順、ヘボン式) |
   | 生年月日 | YYYY-MM-DD |
   | 住所 (英訳) | 例: `Apt 101, 1-2-3 Shibuya, Shibuya-ku, Tokyo, 150-0002, Japan` |
   | 電話 | `+81-90-XXXX-XXXX` |
   | メール | `1llum1n4t1@duck.com` (公開用エイリアス) |
   | OSS プロジェクト URL | `https://github.com/1llum1n4t1s/Lhamiel` |
   | プロジェクト説明 (英) | Lhamiel - A Windows archive compression/decompression desktop app built with Avalonia UI. MIT licensed, available on GitHub. |
   | ライセンス | MIT (LICENSE ファイルへの直リンク併記) |

3. **決済**: クレジットカード or PayPal (ゆろさん本人が入力)。

### Phase 2: IDnow オンライン本人確認 (所要時間: 数分〜数時間)

決済後に Certum から IDnow への招待メールが届く。スマホで:

1. IDnow アプリ DL or Web 版にアクセス
2. パスポート両面撮影 + 顔の動画記録
3. **住所証明**: 漢字の utility bill (公共料金請求書) は OCR で読めないので **ゆうちょ銀行の英文 bank statement** を使う (日本人ユーザーの定番回避策)
   - ゆうちょダイレクトから英文残高証明書を発行可能
4. IDnow 内で OK が出ると Certum 側審査へ自動連携

### Phase 3: Certum 側審査 (所要時間: 数時間〜2 営業日)

- 審査完了メールが届くまで待機
- 完了したら SimplySign アカウント作成案内が来る

### Phase 4: SimplySign セットアップ (所要時間: 30 分〜1 時間)

1. **モバイルアプリ DL**
   - iOS: <https://apps.apple.com/app/simplysign/id1117626020>
   - Android: <https://play.google.com/store/apps/details?id=eu.europa.ec.eudi.app>
   - Certum からの招待メール内のリンクで初期セットアップ

2. **モバイルで TOTP 認証セットアップ**
   - 認証コード (TOTP) はモバイルで自動生成される
   - プッシュ通知での承認も可能

3. **Desktop アプリ DL**
   - <https://support.certum.eu/en/simplysign-desktop-download/>
   - インストール後、モバイルで生成された TOTP で初回ログイン
   - これで **Windows Cert Store** に Certum 証明書が自動登録される

4. **証明書 thumbprint を取得** (後で `/vava` に渡す)

```powershell
# Windows Cert Store の CurrentUser\My に登録された Certum 証明書を確認
Get-ChildItem Cert:\CurrentUser\My |
  Where-Object { $_.Issuer -like "*Certum*" } |
  Select-Object Thumbprint, Subject, NotAfter

# 取得した Thumbprint を環境変数に保存 (.cf_token と同じ形で平文ファイル管理)
Set-Content -Path "$env:USERPROFILE/.lhamiel_sign_thumbprint" -Value "<取得した SHA1 thumbprint>"
```

---

## リリース時の運用 (毎回の手順)

### 標準フロー (`/vava` 経由)

1. **SimplySign Desktop を起動** (タスクトレイに常駐)
   - 初回起動時のみ PIN 入力 (Windows ログオン中はキャッシュ)
2. **`/vava` 実行**
   - version bump → publish → vpk pack (署名込み) → R2 upload (`scripts/release-local.ps1`) → 版タグ `v<version>` の push まで自動（配信は CI の `release.yml` がタグの push で行う）
3. **スマホ通知承認** (1〜数回)
   - vpk pack 中、署名 1 回ごとにスマホにプッシュ通知が来る
   - `--signParallel 1` 設定なら都度承認、まとめ承認モード設定すれば 1 回で済む
4. **署名検証**: リリーススクリプトがアップロード前にローカル成果物の署名を検証する。手動で確認する場合の手順は後述。

### 手動署名コマンド (`/vava` を使わない場合)

```powershell
# 環境変数準備
$thumbprint = (Get-Content "$env:USERPROFILE/.lhamiel_sign_thumbprint" -Raw).Trim()
$tsUrl = "http://time.certum.pl"
$signParams = "/sha1 $thumbprint /fd sha256 /td sha256 /tr $tsUrl"

# Native AOT publish
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64 `
  -p:PublishAot=true --self-contained -o publish/win-x64

# vpk pack with sign
vpk pack `
  --packId Lhamiel `
  --packVersion <VERSION> `
  --packTitle "Lhamiel" `
  --packAuthors "Kagayoi" `
  --mainExe Lhamiel.exe `
  --icon src/Lhamiel/icon/app.ico `
  --packDir publish/win-x64 `
  --outputDir releases/win `
  --channel win `
  --shortcuts "StartMenuRoot,Desktop" `
  --signParams $signParams `
  --signParallel 1   # HSM 経路は逐次署名が安全

```

R2 への配信は Wrangler を使わず、`scripts/release-local.ps1`（`R2Distribution.Common.ps1` / `R2Transfer.Core.cs`）で行う。
手動で署名・パッケージした成果物を配信する場合も、同スクリプトの作業ルートの `packages/` に配置して `-UploadOnly -WorkDirectory <作業ルート>` で送信し、送信順・キャッシュ設定・パージを統一する。

### 署名検証

```powershell
# ローカルで生成された Setup.exe の署名検証
Get-AuthenticodeSignature releases/win/Lhamiel-win-Setup.exe | Format-List
# Status='Valid'、SignerCertificate.Subject に Open Source Developer + ゆろさん氏名が入っていれば OK

```

---

## CI/CD への影響

### 現状

SimplySign Desktop は GUI アプリで GitHub Actions の runner では動かないが、Sigilune は Certum のクラウドへ直接接続して
signtool `/dlib` で署名するため、runner 上でも署名できる。公式リリースは `.github/workflows/release.yml`（版タグ `v<version>` の push で起動し、やり直しだけ手動起動）で作る。

```text
release.yml (GitHub Actions, Environment "release"):
  guard (Secrets なし: タグ・軽量タグ・main の祖先・Directory.Build.props の版との一致) の後、
  1llum1n4t1s/Sigilune@v1 で導入 → scripts/release-local.ps1 を実行
release-local.ps1 (CI・ローカル共通):
  Native AOT publish (win-x64 / win-arm64) → vpk pack (--signTemplate で Sigilune 署名)
  → 署名検証 → R2 アップロード (R2Distribution.Common.ps1) → 固定名 URL のキャッシュパージ → 旧版クリーンアップ
```

- 資格情報は Environment `release` の Secrets（`SIGILUNE_EMAIL` / `SIGILUNE_TOTP_URI` / `CLOUDFLARE_API_TOKEN`）だけに置く。TOTP の登録情報を持つため、Secrets を読めるワークフローを増やさない。
- CI (`.github/workflows/dotnet-build.yml` / `build.yml`) は PR の未署名ビルド・テスト検証だけを行い、署名・配信はしない。
- 実行コマンドと再開手順は [AGENTS.md](../AGENTS.md#cicd)、作業ディレクトリ・検証・保持方針は [DESIGN.md](../DESIGN.md#local-signed-releases-to-r2) を参照する。

---

## トラブルシュート

| 症状 | 原因 | 対処 |
|------|------|------|
| `vpk pack` が `SignTool Error: No certificates were found` | Thumbprint が間違っている / SimplySign Desktop 未起動 | `Get-ChildItem Cert:\CurrentUser\My` で thumbprint 再確認 + SimplySign Desktop 起動 |
| 署名中にスマホ通知が来ない | SimplySign mobile アプリのプッシュ通知が無効 | スマホの OS 設定でアプリの通知を許可 |
| `vpk pack` が PIN プロンプトでハング | Windows ログオン中の PIN キャッシュ切れ | SimplySign Desktop を再ログオン、`--signParallel 1` で 1 ファイルずつ |
| 署名は通るが Status=`NotSigned` になる | Timestamp Server (`time.certum.pl`) が一時的に不通 | 数分待ってリトライ、または DigiCert TSA (`http://timestamp.digicert.com`) に切替 |
| 配信後 SmartScreen 警告が消えない | 評判蓄積待ち (OV 証明書は数百〜数千 DL で警告消える) | EV 証明書 (DigiCert 等) なら即時解除、Open Source 版はゆっくり蓄積待ち |
| `signtool` 自体が見つからない | Windows SDK 未インストール | `winget install Microsoft.WindowsSDK` または `dotnet workload install` |

---

## 障害復旧

### 証明書 (秘密鍵) 漏洩疑い

1. **即座に Certum に失効依頼**: <https://support.certum.eu/> 経由で revocation request
2. **既に配信済みのバイナリは CRL/OCSP で自動的に invalid 化**される (クライアント側は数時間〜数日で反映)
3. **新規証明書を申込み** (€49 × 新規分)、再度 `/vava` で署名し直して再リリース

### Certum アカウント乗っ取り

- SimplySign mobile アプリの 2FA を再設定
- 全リカバリコードを再発行
- 直近の署名履歴を Certum サポート経由で確認

### Cert 失効後の Lhamiel クライアント挙動

- Velopack `SimpleWebSource` は **署名検証しないため** 配信は継続可能 (HTTPS のみ検証)
- ただし新規ユーザーのインストール時に Windows 側で「証明書が失効しています」警告が出る
- → タイミング的に **即座に新証明書で再署名 → R2 上書き再アップロード** が望ましい

---

## 関連 URL

### Certum
- 公式 EU ショップ: <https://shop.certum.eu/>
- 公式 USD ショップ: <https://certum.store/>
- サポート: <https://support.certum.eu/>
- SimplySign Desktop DL: <https://support.certum.eu/en/simplysign-desktop-download/>

### 参考記事 (日本人開発者の実例)
- <https://www.devas.life/code-signing-certificate-for-indie-developers/>
- <https://piers.rocks/2025/10/30/certum-open-source-code-sign.html>
- <https://www.msz.it/a-cheap-code-signing-certificate-for-open-source-projects-by-certum-asseco-an-honest-review-walkthrough/>

### Velopack 署名仕様
- <https://docs.velopack.io/packaging/signing>
- <https://docs.velopack.io/reference/cli/content/vpk-windows>

### Authenticode 仕様
- Microsoft Authenticode Specification: <https://learn.microsoft.com/en-us/windows/win32/seccrypto/cryptography-tools>
- RFC 3161 Time-Stamp Protocol: <https://www.rfc-editor.org/rfc/rfc3161>
