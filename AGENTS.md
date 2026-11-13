# AGENTS.md

This file provides guidance to Codex when working in this repository.
AGENTS.md is the canonical project-wide guidance file; keep it current when updating conventions.
System structure, boundaries, data flows, and design decisions are documented in [DESIGN.md](DESIGN.md); keep this file focused on operational guidance and implementation constraints.

## Project Overview

Lhamiel is a Windows archive compression/decompression desktop app built with **Avalonia 12** (not WPF) and **.NET 11.0**. The UI language is Japanese.

> **⚠️ Windows Only**: `FolderOpener` (explorer.exe)、`NativeLibraryManager` (LoadLibraryW)、`IpcService` (Named Pipe)、`FileIconHelper` (`SHGetFileInfo`)、`ShellLinkNative` (COM) など Windows 固有 API に広範に依存。Linux / macOS 対応は想定していない。

## Build & Development Commands

```bash
# Build (x64, デフォルト)
dotnet build Lhamiel.slnx -c Debug
dotnet build Lhamiel.slnx -c Release

# Build (ARM64)
dotnet build src/Lhamiel/Lhamiel.csproj -c Debug -p:RuntimeIdentifier=win-arm64 -p:PlatformTarget=ARM64

# Run tests (xUnit 4)
dotnet test Lhamiel.slnx -c Release

# 追跡対象 packages.lock.json を既定の x64 / NuGet パッケージ構成で更新・検証
dotnet restore Lhamiel.slnx -p:KagayoiSupportClientUsePackage=true --force-evaluate
dotnet restore Lhamiel.slnx -p:KagayoiSupportClientUsePackage=true --locked-mode

# Run a single test
dotnet test Lhamiel.slnx --filter "FullyQualifiedName~TestMethodName"

# ネイティブシェルの選択リスト・登録照合テスト（VS Developer PowerShell）
msbuild src/Lhamiel.ShellExtension/ShellExtension.Tests.vcxproj /p:Configuration=Release /p:Platform=x64
./src/Lhamiel.ShellExtension/bin/x64/Release/Lhamiel.ShellExtension.Tests.exe

# Publish native AOT executable (x64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64 -p:OS=Windows_NT

# Publish native AOT executable (ARM64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-arm64 -p:PlatformTarget=ARM64 -p:OS=Windows_NT

# Run the app
dotnet run --project src/Lhamiel/Lhamiel.csproj

# 署名付きローカルインストーラー作成（配信なし。Sigilune の導入・登録が必要）
pwsh scripts/release-local.ps1 -SkipUpload -Runtimes win-x64 -WorkDirectory <空の絶対Tempパス>
# → 指定作業ルートの packages/Lhamiel-win-Setup.exe が生成される
```

> **Note**: Native AOT ビルド（`dotnet publish`）は VS の C++ ツールチェーン（`vswhere.exe`）が必要。ローカルテストでは `-p:PublishAot=false --self-contained` を使う。

Solution file: `Lhamiel.slnx` (VS 2026 format). **x64 / ARM64** 両対応。`TreatWarningsAsErrors` が ON — 警告は全てビルドエラーになる。

**SDK の固定**: リポジトリ直下の `global.json` が .NET SDK を `rollForward: disable` で厳密に固定し、CI も `setup-dotnet` の `global-json-file` で同じ SDK を使う。SDK の patch は Native AOT の暗黙 `Microsoft.DotNet.ILCompiler` 版を変え、追跡対象 lockfile の locked restore を NU1004 で失敗させるため、SDK を上げるときは同時に上記の package-mode restore 2 コマンドで lockfile を再生成する。製品の TargetFramework（`net11.0-windows8.0`）は SDK と同じメジャーなので、テストホスト用の追加ランタイムは不要。CI のテスト job は locked restore を独立ステップで実行してから `--no-restore` でテストする（RID 付き publish には locked-mode を掛けない）。

ARM64 の build / publish 後は、上記の `KagayoiSupportClientUsePackage=true` を指定した restore 2 コマンドで追跡対象 lockfile を更新・検証し、`git diff -- src/Lhamiel/packages.lock.json` で RID が既定の `win-x64` に戻ったことと意図しない依存変更がないことを確認する。`release-local.ps1` 終端の restore は、兄弟 SDK が存在する環境ではローカル用 lockfile だけを更新するため、追跡対象の検証を代替しない。復元モードと分離の理由は [DESIGN.md](DESIGN.md#shared-support-sdk-with-two-resolution-modes) を参照する。

## Architecture

MVVM with CommunityToolkit.Mvvm, manually wired without a DI container. Read [DESIGN.md](DESIGN.md#major-components-and-responsibilities) and its [runtime flows](DESIGN.md#runtime-data-flows) before changing a flow; its [critical invariants](DESIGN.md#critical-invariants) are mandatory. `ArchiveProcessor` coordinates `ArchiveExtractor` / `ArchiveCompressor`; SevenZip handles other formats and `LzhArchiveBackendProvider` uses UnLhaRe for LHA/LZH. Preserve CRC verification during extraction: LHA/LZH file output is committed from staging only after CRC verification; do not add a post-extraction second read. Error classification uses SevenZip exception types, UnLhaRe `Kind`, then I/O HRESULT and message fallback; never retry LZH with a different decoder.

- Compression progress must show indeterminate checking, scanning, preparation and finalization phases; report final 100% only after writer disposal, and avoid marquee flicker in parallel batches. SevenZip progress is cumulative, not per-file. Its writer retains read locks until disposal. Files missing after scanning are logged and skipped. `writer.Add` access failures are logged and counted; when password protection is enabled, report those counted skips to the user. All inaccessible inputs must fail rather than publish an empty archive.
- For extraction output selection, preserve `CreateArchiveNameFolder` and compound-extension behavior. Use `FolderOpener.GetExtractionFolderToOpen` with the operation's settings snapshot and `ShellOpener.OpenFolderWithDefaultHandlerAsync` so the default file manager is respected; fallback to Explorer only on failure. Self-terminating CLI/shell flows await folder launch and dialogs. `ProcessCommandLineFiles` suppresses automatic shutdown until all accepted requests and IPC dispatch finish; progress-window closure must not terminate waiting work. Compression opens the actual output folder, including same-directory mode.
- Apply file association changes only on checkbox/select-all changes or an actual icon variant change, not startup or exit; ignore empty/equal variant changes. Preserve registered extensions and notify Explorer only when applying.
- Compression filtering, empty-directory markers and safety rules are in [DESIGN.md](DESIGN.md#caller-owned-compression-filtering). `ScanSourceFiles` runs once before capacity checking; use the same filtered list for compression. Common `.lhaignore` and optional folder-local rules are reloaded for each scan. Hidden/System inclusion follows `IncludeHiddenAndSystemEntries`.
- Keep the [layered concurrency gates](DESIGN.md#layered-concurrency-gates), including native reader/writer lifetime and cross-process output protection. Do not recursively acquire the native gate. Settings and operation snapshots must preserve pending UI changes and remembered-password updates.
### Testability Pattern

- ArchiveProcessor uses interface-backed static seams (see `ServiceContracts.cs`) for tests; tests replacing them must restore them and run in `[Collection("ArchiveProcessor")]`.
- Dialogs follow [shared dialog chrome](DESIGN.md#shared-dialog-chrome): `DialogBody` holds content, `DialogActions` holds actions (progress uses `CancelButton`), and `DialogChrome.Attach` follows loading. Preserve background fallback and bindings; password controls clear on every close route and the accepted result is cleared after retrieval. Verify SDK update-dialog controls and state when updating the SDK.
- `DiskSpaceChecker` fails closed when space cannot be determined. `DiagnosticsCollector` excludes dumps and secrets. Do not log `Environment.UserName`; path masking uses the cached USERNAME/profile patterns. `IpcService` uses a current-user-only named pipe with bounded connect and send/flush; tests use a distinct pipe name.
## Localization System

**17 languages** via Avalonia ResourceDictionary (XAML-based, not .resx).

```csharp
// C# — note: auto-prepends "Text." to the key
var msg = App.Text("Error.DuringExtraction", ex.Message);  // looks up "Text.Error.DuringExtraction"
```

```xml
<!-- XAML — use DynamicResource (not StaticResource) for runtime locale switching -->
<TextBlock Text="{DynamicResource Text.DropZone.Message}" />
```

Adding a new locale: create `Resources/Locales/{xx_YY}.axaml` → add `ResourceInclude` in `App.axaml` → add to `App.SupportedLocales` + `App.LocaleDisplayNames` (`MainWindowViewModel.LocaleOptions` derives from these)

**Pitfall**: `ResourceInclude` implements `IResourceProvider`, NOT `ResourceDictionary` — type casts must use `IResourceProvider`.

## Key Technical Details

- **タスクバーのアプリ識別子** — 未パッケージの通常起動は、ショートカット経由に限らず実行ファイルの配置先から識別子を解決する。変更時は [TaskbarIdentity の不変条件](DESIGN.md#critical-invariants)に従い、`TaskbarIdentityTests` / `AppIconVariantTests` / `ProgramLifecycleTests` で両経路の一致・別配置の隔離・リンクの引数維持・再書込抑止を確認する。

- **Avalonia 12, not WPF** — compiled bindings (`x:CompileBindings="True"`), FluentTheme. `ExtendClientAreaChromeHints` は削除済み → `WindowDecorations` を使う
- **Native AOT** (`PublishAot=true`) — avoid reflection-heavy patterns
- **設定画面のカラーアイコン** — [DESIGN.md](DESIGN.md#settings-navigation-assets) のリソース構成に従い、サイドバーと本文見出しで同じ画像を使う。画像を更新するときは Avalonia リソース登録とライセンスの埋込み・表示も維持する。
- **対応形式の追加・変更** — 展開拡張子は `ArchiveFormatConstants.SupportedArchiveFormats` を更新し、展開判定・関連付け UI の共通カタログを維持する。圧縮形式は `Settings.SupportedCompressionFormats`、`ArchiveCompressor.WritableFormats` / `ParseFormat`、CLI の形式許可リストとパスワード可否を併せて照合する。
- **右クリックメニュー** — 登録・解除を変更するときは [シェル契約](DESIGN.md#shell-command-visibility-separate-from-package-lifetime)を読み、失敗時の表示維持・COM 互換経路・パッケージ寿命を守る。Windows 10 build 19041 以降は sparse MSIX の COM 登録を使う。展開／圧縮の CLSID・canonical GUID・静的 verb 名を C++、manifest、`ShellContextMenu` で一致させる。`scripts/build-shell-integration.ps1` が x64/ARM64 DLL と sparse MSIX を生成し、`scripts/release-local.ps1` が署名後に配布する。Publisher は Certum 証明書 Subject と完全一致させる。
- **Kagayoi.Support の復元グラフ** — [2つの復元モード](DESIGN.md#shared-support-sdk-with-two-resolution-modes)を維持する。Release に Debug DLL が混入しないよう `Directory.Build.props` の `ShouldUnsetParentConfigurationAndPlatform=false` と、直接参照の `SetConfiguration="Configuration=$(Configuration)"` / `SetPlatform="Platform=AnyCPU"` を維持する。依存更新時は上記の package-mode restore 2 コマンドで追跡対象 lockfile を更新・検証する。
- **CRDebugger の Debug 限定参照** — `PackageReference` は構成非依存のまま `ExcludeAssets="compile;runtime"` + `GeneratePathProperty="true"` を維持し、Debug 用の `<Reference>` だけを構成条件付きにする。`PackageReference` 自体を Debug 条件付きにすると、Release restore 後の Debug Rebuild が共有 `project.assets.json` から参照を解決できず CS0246 になる
- **7z.dll** — `1llum1n4t1s.Sevenzip` NuGet が同梱。`NativeLibraryManager` が起動時に `LoadLibrary` で固定。`GeneratePathProperty` と `licenses/7-Zip/License.txt` の build/publish コピーを維持し、同梱 DLL のライセンス告知を配布出力から欠落させない
- **ネイティブ操作の直列化** — [並行ゲート契約](DESIGN.md#layered-concurrency-gates)に従い、SevenZip の reader/writer 生成から Dispose まで `NativeArchiveGate` で保護し、再入させない。全入口の worker 分離、展開先・圧縮出力のプロセス間保護、出力確定・復元までの保持と設定保存の整合を維持する。UnLhaRe は[別契約](DESIGN.md#versioned-unlhare-integration)に従う。
- **展開パスの安全境界** — `ArchiveReader.Save` を追加・移動するときは、書き込み開始前に必ず全 `item.FullName` を `ArchiveExtractor.ValidateArchiveEntryPaths` へ通す。出力パスには `RawName` を使わず、予約デバイス名・末尾空白/ピリオド・境界外パスを拒否する。展開先や一時/退避フォルダーの属性走査では `ReparsePoint` を辿らない（詳細は `DESIGN.md` の不変条件を参照）
- **Logger** — `SuperLightLogger` File Target。ログ保存先は [README.md](README.md#トラブルシューティング)。ログ経路で `Environment.UserName` を呼ばない（RDP 等で無期限ブロックする）。`MaskUserPath` は USERNAME とプロファイルフォルダ名の事前コンパイル済みパターンを使う。
- **Velopack** — `SimpleWebSource` で `https://lhamiel.kagayoi.com`（R2）から更新し、通常リリースは R2 のみに配信する。廃止済み `lhamiel.1llum1n4t1.com` は残さない。旧 `GithubSource` クライント用の Kagayoi 版 GitHub Releases 踏み台は削除せず永続保持し、継続併用配信はしない。`Program.cs --update-check` はログイン時のサイレント経路、`App.Check4Update` は起動時設定と手動操作に使う UI 経路。
- **Code signing (Authenticode)** — 全リリースバイナリ（exe・Setup・Portable を含む）を Certum Open Source Code Signing in the cloud で署名する。署名は [Sigilune](https://github.com/1llum1n4t1s/Sigilune)（クラウド鍵を signtool `/dlib` で使う CLI。SimplySign Desktop・手入力コード不要）で行い、共通関数は `scripts/Sigilune.Common.ps1`（実行ファイルは `SIGILUNE_EXE` → PATH → 既定 `%USERPROFILE%\.local\Sigilune\bin\sigilune.exe`）。通常のリリースは `.github/workflows/release.yml`（手動起動・Environment `release` の `SIGILUNE_EMAIL` / `SIGILUNE_TOTP_URI` / `CLOUDFLARE_API_TOKEN`）が `scripts/release-local.ps1` を実行し、ローカル実行は検証・再開に使う。Velopack は `--signTemplate "<sigilune> sign {{file}}"`、Shell 拡張 DLL と sparse MSIX は `Invoke-SigiluneSign` で署名する。署名者は証明書更新で thumbprint が変わるため CN で照合する。単発署名: `sigilune sign <file>`（署名後に有効性・署名者・タイムスタンプを検証）。
- **AllowUnsafeBlocks** for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`, `CrashHandler`)
- **Acrylic blur / 可読性レイヤー** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint`。白いデスクトップ背景が透けても文字コントラストを保つため、root `Panel` の子は「アクリル境界 → `Brush.Window` / `Opacity=0.5` / `IsHitTestVisible=false` のスクリム → コンテンツ」の順を維持する（`AcrylicFallbackHelper` が直接のアクリル子を探索する契約も守る）。補助文字はテーマ別 `FG2` を使い、個別ウィンドウで薄い `TintOpacity` を再導入しない
- Async/await + CancellationToken throughout all I/O operations
- Version: `Directory.Build.props` の `<Version>` タグで全プロジェクト共有
- **Unicode NFC normalization**: macOS HFS+ の NFD ファイル名を展開・圧縮時に NFC 正規化（`Settings.NormalizeUnicodeFileNames`）。展開後処理（MotW・スキップ削除）は正規化パスを優先し、実体が無い場合だけ `TryResolveExistingEntryPathFromNormalized` で生 Unicode 表現を同じ安全境界へ再検証して解決する（検証なしの raw path 結合は禁止）
- **Long path support**: `app.manifest` で `longPathAware` + `PathValidator.EnsureLongPathPrefix`
- **Mark of the Web**: 元アーカイブの Zone.Identifier ADS を展開ファイルに伝播（`Settings.PropagateMarkOfTheWeb`）
- **Support intake**: バージョンタブの問い合わせ導線は `SupportDialog` を開き、メール確認コードの検証後に `Kagayoi.Support` へ送信する。公開GitHub Issuesは開発者向け技術課題に限定し、利用者の問い合わせとメールアドレスは載せない
- **Password-protected compression** — Follow [format-specific password support](DESIGN.md#format-specific-password-support) in UI, settings, CLI/shell and direct calls. ZIP and 7z use AES-256; TAR/LZH reject passwords. ZIP passwords are ASCII-only with the bundled 7-Zip; validate prompts and direct writer calls. A remembered non-ASCII password remains saved for 7z and gets a temporary ZIP prompt. New compression passwords require at least 4 characters to match redaction; remembered shorter or undecryptable values trigger notification and re-prompt, never silent deletion. Remember uses DPAPI; `EncryptFileNames` is runtime-only, defaults on when protection is enabled, applies only to 7z, and must reach snapshots from both UI and shell/IPC. Register password redaction before logging, exclude password data and dumps from support bundles, and preserve `MinCompressPasswordLength_PasswordIsAlwaysRedactable`.
- **Encrypted extraction** — Header-encrypted 7z structure inspection can require a password before opening; retry and cancellation must retain their distinct outcomes. For failed .7z/.rar structure inspection, allow at most three password attempts and suppress further extraction prompts after all attempts fail. Serialize structure prompts and extraction password dialogs using the order `StructurePasswordPromptGate` → `NativeArchiveGate` → `ExtractionPasswordDialogGate` when nested. The structure prompt releases `ExtractionPasswordDialogGate` before reinspection acquires `NativeArchiveGate`; extraction may acquire the dialog gate while holding the native gate. The dialog gate also encloses `CrossProcessResourceGate.EnterMetadataAsync`. Never recursively acquire the native gate. Register every attempted password for redaction before inspection and through extraction, including passwords requested after structure inspection. Existing archives may use passwords shorter than the redaction minimum: suppress exception details in logs and user-facing error details while such passwords are in scope. Consult `ArchiveProcessor` and `ArchiveExtractor` before changing these paths.
- **VM 設定の即時フラッシュ**: `MainWindowViewModel` の AutoSave は 300ms デバウンスのため、永続層 (`SettingsManager`) スナップショットを取る直前の UI 変更が未反映のことがある。対策として `MainWindowViewModel.Current` (static、ctor で設定) + `FlushPendingAutoSave()` を、(1) `ArchiveProcessor` の Remember パスワード保存直前 (`MutateAndSave` の live 再チェックが古い値を見ないように)、(2) `App.TryRunArchiveWorkersAsync` と画面ドロップの受付 (全入口のスナップショット鮮度) に加え、圧縮・展開 worker 起動前と worker からの保存パスワード更新通知受信時にも呼ぶ。いずれも UI スレッドから呼ぶこと。テストは VM を構築しないので `Current` は null → no-op。
- **Compression scan attributes**: `Settings.IncludeHiddenAndSystemEntries` が圧縮スキャン時の `EnumerationOptions.AttributesToSkip` を制御する。
- **Compression exclusions**: `%LocalAppData%\Lhamiel\.lhaignore` に `.gitignore` 互換構文で記述。`GitignoreMatcher` が compile して `ArchiveCompressor` 側でディレクトリ枝刈り付きマッチを行う。UI 追加・削除・既定値リセット + 「除外設定ファイルを開く」（既定エディタで開く）の 4 操作で管理し、`FileSystemWatcher` が外部編集を検知して UI を再同期する。
- **Folder-local ignore files**: `Settings.RespectNestedGitignore` (default **false / オプトイン**、既存 JSON 互換のため名称維持) が ON なら、各ディレクトリで `Settings.SourceIgnoreFileNames`（default `[".gitignore"]`、上ほど高優先）を確認し、最初に存在する 1 ファイルだけをそのスコープで `GitignoreMatcher.CompileLayered` に追加する。候補順位は分岐ごとに継承し、子孫では同じ候補または高優先候補への切替だけを許可して、低優先候補へは戻らない（root `.gitignore` → child `.lhamielignore` は昇格、root `.lhamielignore` → child `.gitignore` は抑止）。空の高優先ファイルも採用扱いで、その分岐では低優先候補へ落ちない。候補一覧は全圧縮共通設定、ルール内容は各フォルダー固有。`.lhaignore` の枝刈り後のディレクトリのみ探索する。除外判定は DFS 枝刈り + `IsExcluded(traversalMode: true)` の「自身レベル照合」で行うため、git と同様に**ディレクトリ否定再包含**（`*.xcodeproj/*` + `!*.xcodeproj/xcshareddata/` で配下の共有ファイルを救い、`d/*`+`!d/sub/` と `d/`+`!d/sub/` の差も区別）を正しく扱う。なおパターン一致のみで判定するため、git の「追跡済みファイルは ignore に勝つ」例外（`git add -f` した除外パターン該当ファイル）は再現しない。

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test + code coverage on every PR
- 製品ページの配信は兄弟リポジトリの `../Web/deploy/deploy-lp.ps1` を使う。公開ホスト・更新ファイルの既存経路を維持する。
- **RID 別リリース出力**: `release-local.ps1` の x64 / ARM64 `dotnet publish` は RID ごとの `--artifacts-path` を必須とし、ProjectReference 先を含む共通 `bin/obj` の再利用による異アーキテクチャ参照（CS8012）を防ぐ。
- **リリースの再開**: リポジトリルートで `pwsh scripts/release-local.ps1 -ReusePublish -WorkDirectory <既存の絶対Tempパス>` を使うと、指定作業ルートの `publish-<RID>/` を検証して再パッケージし、exe がない RID はビルドする。配信だけ再開する場合は同じ作業ルートを `pwsh scripts/release-local.ps1 -UploadOnly -WorkDirectory <既存の絶対Tempパス>` に渡す。通常実行は空の作業ルートを要求し、省略時は作成先を表示する。再開時に `-WorkDirectory` は必須。`-Runtimes win-x64` / `win-arm64` で対象を限定し、`-SkipUpload` は配信前で終了する。保存場所・検証条件・再開の境界は [DESIGN.md](DESIGN.md#local-signed-releases-to-r2) を参照する。
- **ネイティブ配布物**: `.github/workflows/build.yml` と `scripts/release-local.ps1` の両 RID の publish 検査では、`Lhamiel.exe`、`7z.dll`、`unlhare.dll`、`licenses/Kagayoi.UnLhaRe/LICENSE`、`licenses/Kagayoi.UnLhaRe/THIRD_PARTY_NOTICES.md` の存在確認を維持する。UnLhaRe 更新時は `src/Lhamiel/Lhamiel.csproj` の `UnLhaReVersion` とパッケージ構成の lockfile を揃え、DLL の手動コピー経路を作らない。通常の依存更新でも locked restore と Release テストを維持する。
- **公開完了判定**: [DESIGN.md の配信フロー](DESIGN.md#update-and-release) に従い、公開前検証と R2 書込み・固定 URL 全件のパージ API の受付成功を確認する。公開後の配信内容取得・ハッシュ照合・CDN 伝播待ちは行わない。
- **CodeQL**: `.github/workflows/codeql.yml` — C# security analysis on PR + weekly
- **Dependabot**: `.github/dependabot.yml` — NuGet weekly + github-actions weekly

### リリースの実行

通常は GitHub Actions で公開する。/vava で版を上げて push した後、`gh workflow run release.yml --ref <ブランチ>` で起動し、`gh run watch` で完了を確認する（`-f skip_upload=true` は配信せず署名済み成果物だけを Actions に残す）。ワークフローは `scripts/release-local.ps1` を実行するため、ビルド・検証・配信の契約はローカル実行と同じ。CI の Certum セッションはランナー上の一時領域だけに置き、終了時に `sigilune logout` する。以下はローカルで実行・再開する場合の手順。

R2 配布は PowerShell 7.2 以降と同梱の `scripts/R2Distribution.Common.ps1` を使う。配布に Node.js・pnpm・Wrangler は不要（製品の JS ビルド要件は別途維持）。署名・転送・検証の正本は同梱の `scripts/R2Transfer.Core.cs` とし、PS helper は公開関数の互換入口とする。更新時は中核と使用する入口を同じディレクトリへ同梱する。固定名は `no-cache, must-revalidate`、バージョン付き成果物は不変として 1 年キャッシュし、payload を先、assets・RELEASES・releases を最後に送信する。

公開依頼時は、リポジトリルートで Sigilune のクラウドログインを事前確認し、成功した場合だけリリースを実行する（`release-local.ps1` もビルド前に同じ確認を行う）。失敗時は `sigilune status` で登録を確認するよう案内し、ログインを自動再試行せず、署名を省略して続行しない。配信しない検証と再開の引数は、上記のコマンドを参照する。

```powershell
$sigilune = if ($env:SIGILUNE_EXE) { $env:SIGILUNE_EXE } else { Join-Path $env:USERPROFILE '.local\Sigilune\bin\sigilune.exe' }
& $sigilune login
if ($LASTEXITCODE -eq 0) { pwsh -NoProfile -File scripts/release-local.ps1 } else { throw 'Sigilune でクラウドにログインできなかったよ。sigilune status で登録を確認してから /vava を再実行してね' }
```

## Testing

Tests in `src/Lhamiel.Tests.Unit/` using **xUnit 4**. The test project references the main project directly via `InternalsVisibleTo`.

- 通常テスト: `*Tests.cs`
- 嫌がらせテスト (adversarial): `*AdversarialTests.cs` — 境界値、異常入力、状態遷移の矛盾
- `[Collection("ArchiveProcessor")]` — `ArchiveProcessor` の static プロパティを差し替えるテストは排他実行が必要
- ADS テスト (`MotwPropagatorTests`): Windows 限定、`Assert.SkipWhen(!OperatingSystem.IsWindows())` でスキップ
- ログの保持期限・命名規則を変更するときは `LoggerRetentionTests` を実行し、通常ログと worker ログのローテートを含む期限判定、境界日の保持、緊急ログ・未知の命名形式の除外を確認する。設計は [DESIGN.md](DESIGN.md#diagnostic-log-retention) を参照する。
- IPC 圧縮の分岐・設定保存・プロセス間ゲートを変更するときは、既存の Lhamiel を閉じ、「元と同じ場所に保存」ON・パスワード保護 OFF にした環境で `pwsh -NoProfile -File scripts/Test-ParallelShellCompression.ps1 -AppPath <検証するLhamiel.exeの絶対パス> -ArtifactDirectory <未使用の成果物ディレクトリの絶対パス>` を実行する。並行 PID・後発処理の先行完了・ZIP 内容の SHA256 を `result.json` とログで確認する。スクリプトは設定を変更せず、入力とログを成果物として保持する。同一出力への直列化とパスワード保存の競合は別途確認する。
- 展開公開・MotW を変更するときは、通常展開と衝突解決の両経路で一時ルートと配下のリンクが公開前に拒否されること、MotW がリンクのルート・ファイルへ書き込まないことを確認する。属性検査後のリンク差し替えまで防げるとは扱わない（[設計の不変条件](DESIGN.md#critical-invariants)）。
- 上書き準備を変更するときは `ExtractionOverwriteAtomicityTests`、右クリックメニューの登録・解除を変更するときは `ShellContextMenuTests` と `ProgramLifecycleTests` を実行し、[DESIGN.md](DESIGN.md#adopted-design-decisions-and-trade-offs) の復元・パッケージ寿命の契約を確認する。シェル変更時は上記ネイティブテストも実行し、実際の Explorer（Windows 11 の新メニュー／「その他のオプション」）と対象ファイルマネージャーで複数選択の一括引渡し・重複表示・OFF/ON 切替を確認する。COM 能力確認を変更するときは、片方／両方の有効コマンドでの `E_NOINTERFACE`、他の負の HRESULT、再適用後の能力回復を確認し、静的 verb・表示状態・UI と保存設定の整合を検証する。`Marshal.ThrowExceptionForHR` の例外型を `COMException` に固定せず HRESULT で照合する。GUID の一致だけで重複表示の解消を保証しない。
- LHA/LZH 境界の変更時は `LzhArchiveIntegrationTests` と `ArchiveErrorHandlerTests` を実行し、Unicode・空フォルダー・選択展開・時刻復元、visitor の途中失敗、既存出力保護、読めない入力のスキップ、上限・キャンセルを確認する。`LzhArchiveBackendProvider.Current` を差し替えるテストは `[Collection("Sequential")]` で排他実行し、`finally` で元へ戻す。形式やパスワード可否の変更には `SettingsTests`、`SettingsPasswordTests`、`ArchiveProcessorPasswordTests`、`AppCommandLineLifecycleTests`、`LocaleTests` も含める。

## Documentation

- `DESIGN.md` — current system design and data-flow source of truth
- `docs/SETTINGS_SCHEMA.md` — complete settings.json reference
- `docs/ARCHITECTURE.md` — `FileConflictDialog` integration for extraction/compression conflicts; `DESIGN.md` wins on conflicts.
- `docs/DOUBLE_FOLDER_PREVENTION.md` — `ShouldSkipFolderCreation` double-folder prevention rules.
- `docs/PARALLEL_IMPLEMENTATION_REPORT.md` — historical research; read relevant sections only when past rationale matters to the current task. Use `DESIGN.md` for current contracts.

## ドメイン移行（2026-07 開始・期限 2027/05/31）

屋号を **Kagayoi** に統一したため、配信ドメインを `nephilim.jp` から `kagayoi.com` へ移行中。方針の全体像はユーザーグローバルの `AGENTS.md` §屋号とドメイン を参照する。

- **旧ドメイン `nephilim.jp` はレジストラで廃止申請済みで 2027/05/31 に失効する**（延長しない）。それまでに出荷済みバイナリを新ドメインへ移行しきる。
- 旧ホストの Worker route / custom domain は**期限まで消さない**。消すと出荷済みアプリの自動更新が止まる。
- `nephilim.jp` の Redirect Rules は `/` だけを 301 する。`releases.*.json` / `*.nupkg` / `*-Setup.exe` は転送せず R2 が配信を続ける。
- 配信は `lhamiel.kagayoi.com`（R2 `lhamiel-updates`）。旧 `lhamiel.nephilim.jp` は route に併記して残してある。
- sparse MSIX のパッケージ名 `Nephilim.Lhamiel.ContextMenu` は**改名しない**。Publisher が Certum 証明書の Subject と一致している必要があり、変えると Windows 11 の右クリックメニュー登録が壊れる。
