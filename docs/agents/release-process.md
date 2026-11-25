# リリース手順

## 更新元と署名

- **Velopack** — `SimpleWebSource` で `https://lhamiel.kagayoi.com`（R2）から更新し、通常リリースは R2 のみに配信する。廃止済み `lhamiel.1llum1n4t1.com` は残さない。旧 `GithubSource` クライント用の Kagayoi 版 GitHub Releases 踏み台は削除せず永続保持し、継続併用配信はしない。`Program.cs --update-check` はログイン時のサイレント経路、`App.Check4Update` は起動時設定と手動操作に使う UI 経路。
- **Code signing (Authenticode)** — 全リリースバイナリ（exe・Setup・Portable を含む）を Certum Open Source Code Signing in the cloud で署名する。署名は [Sigilune](https://github.com/1llum1n4t1s/Sigilune)（クラウド鍵を signtool `/dlib` で使う CLI。SimplySign Desktop・手入力コード不要）で行い、共通関数は `scripts/Sigilune.Common.ps1`（実行ファイルは `SIGILUNE_EXE` → PATH → 既定 `%USERPROFILE%\.local\Sigilune\bin\sigilune.exe`）。通常のリリースは `.github/workflows/release.yml`（版タグ起動・Environment `release` の `SIGILUNE_EMAIL` / `SIGILUNE_TOTP_URI` / `CLOUDFLARE_API_TOKEN`）が `scripts/release-local.ps1` を実行し、ローカル実行は検証・再開に使う。Velopack は `--signTemplate "<sigilune> sign {{file}}"`、Shell 拡張 DLL と sparse MSIX は `Invoke-SigiluneSign` で署名する。署名者は証明書更新で thumbprint が変わるため CN で照合する。単発署名: `sigilune sign <file>`（署名後に有効性・署名者・タイムスタンプを検証）。

## リリースの実行

通常は GitHub Actions で公開する。/vava が版を上げた main へ軽量タグ `v<版>` を付けて push すると `release.yml` が自動で起動する（`guard` ジョブが軽量タグ・main の祖先・`Directory.Build.props` の版との一致を確かめる。やり直しは `gh workflow run release.yml --ref v<版>`）。`vigilia watch HEAD` で完了を確認する（REST枠を使わずGraphQLで取得し、同じ対象は全セッションでキャッシュ共有されるため複数セッションが同時に見てよい。`-f skip_upload=true` は配信せず署名済み成果物だけを Actions に残す）。ワークフローは `scripts/release-local.ps1` を実行するため、ビルド・検証・配信の契約はローカル実行と同じ。CI の Certum セッションはランナー上の一時領域だけに置き、終了時に `sigilune logout` する。以下はローカルで実行・再開する場合の手順。

R2 配布は PowerShell 7.2 以降と同梱の `scripts/R2Distribution.Common.ps1` を使う。配布に Node.js・pnpm・Wrangler は不要（製品の JS ビルド要件は別途維持）。署名・転送・検証の正本は同梱の `scripts/R2Transfer.Core.cs` とし、PS helper は公開関数の互換入口とする。更新時は中核と使用する入口を同じディレクトリへ同梱する。固定名は `no-cache, must-revalidate`、バージョン付き成果物は不変として 1 年キャッシュし、payload を先、assets・RELEASES・releases を最後に送信する。

公開依頼時は、リポジトリルートで Sigilune のクラウドログインを事前確認し、成功した場合だけリリースを実行する（`release-local.ps1` もビルド前に同じ確認を行う）。失敗時は `sigilune status` で登録を確認するよう案内し、ログインを自動再試行せず、署名を省略して続行しない。配信しない検証と再開の引数は、上の「リリースの再開」を参照する。

```powershell
$sigilune = if ($env:SIGILUNE_EXE) { $env:SIGILUNE_EXE } else { Join-Path $env:USERPROFILE '.local\Sigilune\bin\sigilune.exe' }
& $sigilune login
if ($LASTEXITCODE -eq 0) { pwsh -NoProfile -File scripts/release-local.ps1 } else { throw 'Sigilune でクラウドにログインできなかったよ。sigilune status で登録を確認してから /vava を再実行してね' }
```
