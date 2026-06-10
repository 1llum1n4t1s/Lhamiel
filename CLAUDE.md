# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
CLAUDE.md is the canonical project-wide guidance file; keep it current when updating conventions.

## Project Overview

Lhamiel is a Windows archive compression/decompression desktop app built with **Avalonia 12** (not WPF) and **.NET 10.0**. The UI language is Japanese.

> **⚠️ Windows Only**: `FolderOpener` (explorer.exe)、`NativeLibraryManager` (LoadLibraryW)、`IpcService` (Named Pipe)、`FileIconHelper` (`SHGetFileInfo`)、`ShellLinkNative` (COM) など Windows 固有 API に広範に依存。Linux / macOS 対応は想定していない。

## Build & Development Commands

```bash
# Build (x64, デフォルト)
dotnet build Lhamiel.slnx -c Debug
dotnet build Lhamiel.slnx -c Release

# Build (ARM64)
dotnet build src/Lhamiel/Lhamiel.csproj -c Debug -p:RuntimeIdentifier=win-arm64 -p:PlatformTarget=ARM64

# Run tests (xUnit 3)
dotnet test Lhamiel.slnx -c Release

# Run a single test
dotnet test Lhamiel.slnx --filter "FullyQualifiedName~TestMethodName"

# Publish native AOT executable (x64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64

# Publish native AOT executable (ARM64)
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-arm64 -p:PlatformTarget=ARM64

# Run the app
dotnet run --project src/Lhamiel/Lhamiel.csproj

# ローカルインストーラー作成（Native AOT 不使用、テスト確認用）
dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64 -p:PublishAot=false --self-contained -o local-publish
vpk pack --packId Lhamiel --packVersion <VERSION> --packTitle "Lhamiel" --packAuthors "Lhamiel" --mainExe Lhamiel.exe --icon src/Lhamiel/icon/app.ico --packDir local-publish --outputDir local-installer --channel win --shortcuts "StartMenu,Desktop"
# → local-installer/Lhamiel-win-Setup.exe が生成される
```

> **Note**: Native AOT ビルド（`dotnet publish`）は VS の C++ ツールチェーン（`vswhere.exe`）が必要。ローカルテストでは `-p:PublishAot=false --self-contained` を使う。

Solution file: `Lhamiel.slnx` (VS 2026 format). **x64 / ARM64** 両対応。`TreatWarningsAsErrors` が ON — 警告は全てビルドエラーになる。

## Architecture

**MVVM with CommunityToolkit.Mvvm** — no DI container; dependencies are wired manually.

### Layers

- **View/** — Avalonia AXAML + code-behind (MainWindow, ProgressWindow, FileConflictDialog, ErrorRecoveryDialog, DiskSpaceDialog, ConfirmDialog)
- **ViewModels/** — MainWindowViewModel with `[ObservableProperty]` source generators
- **Util/** — All business logic (archive operations, settings, logging, file association, updates, crash handling, diagnostics)
- **Models/** — Data models

### Core Processing Flow

Drag-and-drop drives the app:
1. `MainWindow.DropZone_Drop` → `MainWindowViewModel.ProcessDroppedPathsAsync`
2. ViewModel delegates to `ArchiveProcessor` which orchestrates extraction/compression
3. `ArchiveExtractor` / `ArchiveCompressor` wrap `1llum1n4t1s.Sevenzip`
4. Post-extraction: CRC verification (`ArchiveIntegrityVerifier`) → MotW propagation (`MotwPropagator`)
5. `ProgressWindow` shows real-time progress via `IProgress<T>`

**展開時の出力先決定** (`ArchiveProcessor`):
- `CreateArchiveNameFolder=ON` + ルートフォルダがアーカイブ名と一致 → フォルダ作成スキップ（`ShouldSkipFolderCreation`）
- `CreateArchiveNameFolder=ON` + それ以外 → `baseDir/アーカイブ名/` フォルダを作成
- `CreateArchiveNameFolder=OFF` → `baseDir` に直接展開
- 複合拡張子（`.tar.gz` 等）は `GetArchiveBaseName()` で正しく処理

**展開後にフォルダを開く**: フォルダ決定ロジックは `FolderOpener.GetExtractionFolderToOpen` に集約。呼び出し側は展開時の `createArchiveNameFolder` 設定値を渡す（展開中の設定変更による不整合を防止）

**圧縮時のロック中ファイル対応**: ソースファイルは元パスのまま `ArchiveWriter.Add()` に渡す。ロック中のファイルはライブラリ（`1llum1n4t1s.Sevenzip`）の `UpdateCallback` が自動的に一時コピーして処理する。スキャン後に削除されたファイルは `File.Exists()` チェックでスキップし、残りのファイルで圧縮を続行する。**`writer.Add()` が `AccessException` を投げた場合（VS の `.vsidx` のように `FileShare.None` で握られていてライブラリの 2 段試行（`FileShare.Read` → `FileShare.ReadWrite|Delete`）で両方失敗するケース）は、当該ファイルのみログに warning を出してスキップし、残りで圧縮を続行する**（1 ファイルアクセス不能で全体を死なせない）。スキップ件数は完了直前に集約ログを残す。

**圧縮時のファイル列挙・除外設定**:
- `ArchiveCompressor.ScanSourceFiles` は `Settings` スナップショットと `GitignoreMatcher` を元に対象一覧を構築する。
- `IncludeHiddenAndSystemEntries=true`（デフォルト）では `EnumerationOptions.AttributesToSkip = 0` とし、Hidden/System 属性のファイル・フォルダも含める（例: `.git`）。
- `IncludeHiddenAndSystemEntries=false` では Hidden/System 属性をスキップする。
- 除外パターンは `%LocalAppData%\Lhamiel\.lhaignore` に保存され、**`.gitignore` 互換のグロブ・否定・ディレクトリ限定構文に対応**（例: `*.log`, `node_modules/`, `/build`, `**/cache`, `!keep.txt`）。
- `ArchiveCompressor.GetFilesRecursively` は除外ディレクトリで枝刈りする手書き DFS（`Stack<string>`）を使うので、`node_modules/` 配下を踏まずに済む。
- **空ディレクトリエントリは「空マーカーディレクトリ」経由で追加する**（`CreateEmptyDirectoryMarker`）。`writer.Add(realDir, "rel/")` のように**実ディレクトリ**を渡すと、ライブラリ（`1llum1n4t1s.Sevenzip`）の `AddRecursive` が `Io.GetFiles`/`Io.GetDirectories` で**フィルタなしに再走査**し、スキャンで除外したはず（Hidden/System 属性・`.lhaignore` 該当）のファイルを復活させてしまう（中身ゼロ判定のディレクトリでも実体には除外ファイルが残るため）。ライブラリ側はフィルタを一切持たない前提なので、**除外はすべて呼び出し側（Lhamiel）が担保する**。個別ファイルは `ScanSourceFiles` の結果リストから 1 件ずつ `writer.Add` するため再走査は起きない。
- 圧縮実行ごとに `LhaignoreFile.LoadMatcher()` で最新内容を読み直すため、設定 UI を介さない外部編集も反映される。
- 設定 UI（追加・削除・既定値リセット・「除外設定ファイルを開く」）は `.lhaignore` を直接編集する。`FileSystemWatcher` が外部編集を検知して `ObservableCollection<string>` を再ロードする。

### Key Util Classes

| Class | Responsibility |
|-------|---------------|
| `ArchiveProcessor` | Orchestrator — decides extract vs compress, manages workflow |
| `ArchiveExtractor` | Extraction with `ShouldSkipFolderCreation`, `TryExtractEntryAsync` (retry with exponential backoff) |
| `ArchiveCompressor` | Compression with Unicode NFC normalization, Hidden/System enumeration control, and `.gitignore` 互換除外マッチ (`GitignoreMatcher` + ディレクトリ枝刈り DFS) |
| `GitignoreMatcher` | `.gitignore` 互換のパターンコンパイラ／マッチャ（`*` / `?` / `**` / `[abc]` / 否定 `!` / アンカー `/` / ディレクトリ限定 `/` 末尾）。`IsExcluded(..., traversalMode)` の 2 経路: **traversal**（DFS 枝刈り併用・各エントリを自身レベルだけで照合し、除外の推移性は DFS が担保）と **flat**（単発ファイル判定用・推移マッチ）。traversal では非 globstar ルールに末尾 `$` の `ExactPathRegex` を使い、git 同様に**ディレクトリ否定再包含**（`*.xcodeproj/*` + `!*.xcodeproj/xcshareddata/` 等で配下を救う）を正しく扱う。globstar（`foo/**`）は `/` を跨ぐので通常 `Regex` を使う |
| `LhaignoreFile` | `%LocalAppData%\Lhamiel\.lhaignore` の I/O（読込・追記・削除・既定値リセット・移行）。`LoadMatcher()` で `GitignoreMatcher` を返す |
| `ArchiveErrorHandler` | HResult-based error classification (二段判定: HResult → メッセージ走査フォールバック) |
| `ArchiveIntegrityVerifier` | Post-extraction CRC verification via `reader.Test()` |
| `LockedFileRetryPolicy` | Generic exponential backoff retry for SHARING_VIOLATION / LOCK_VIOLATION |
| `MotwPropagator` | Zone.Identifier ADS propagation from source archive to extracted files |
| `CrashHandler` | MiniDump P/Invoke for unhandled exceptions, dump rotation |
| `DiagnosticsCollector` | Export support ZIP (logs, masked settings, environment info, dumps) |
| `PartialExtractionHandler` | **[Obsolete]** — delegates to `ArchiveExtractor.TryExtractEntryAsync`. Types still used by ErrorRecoveryDialog |
| `Settings` / `SettingsManager` | JSON config at `%LocalAppData%\Lhamiel\settings.json` with JsonDocument fallback recovery, compression scan settings, and exclusion patterns。`UpdateBaseUrl` は `[JsonIgnore]` + getter-only でハードコード固定（`CanonicalUpdateBaseUrl`）、悪意ある第三者ホストへの誘導防御 |
| `PathValidator` | Path safety checks + `EnsureLongPathPrefix` for paths > 260 chars |
| `NativeLibraryManager` | 7z.dll lifecycle management |
| `NativeArchiveGate` | ネイティブ 7z.dll (`1llum1n4t1s.Sevenzip`) 接触をプロセス全体で 1 スロットに直列化する `SemaphoreSlim(1,1)` ゲート。ライブラリの共有シングルトン `SevenZipLibrary` (refcount + COM 追跡) は `ArchiveReader`/`ArchiveWriter` の並行動作をサポートしない (ライブラリ doc 参照) ため、各 reader/writer の「生成→使用→Dispose」全体を `Enter`/`EnterAsync` で囲む。バッチ展開・圧縮の `IoBoundParallelism` (2〜4) 並列時もネイティブ接触が重ならない。非リエントラント (全ネイティブ接触点は逐次の兄弟で入れ子なし) |
| `UpdateChecker` | Velopack 自動更新の **`--update-check` サイレント CLI 経路** (Program.cs から `StartupRegistration` の HKCU\Run 登録経由で発火、UI 不要なバックグラウンド自動更新)。配信元は `Settings.UpdateBaseUrl` (= Cloudflare R2 カスタムドメイン) を **`Velopack.Sources.SimpleWebSource`** で取得 (v1.0.168 で `GithubSource` から切替) |
| `App.Check4Update` | Velopack 自動更新の **UI 経路**。`Settings.Check4UpdatesOnStartup=true` のときメイン画面起動直後 + 「アップデート確認」ボタンから手動起動。`VelopackUpdateDialog.UpdateDialogWindow` をオーナー付きで表示し、Velopack 0.0.1369-g1d5c984 と組み合わせて 30 秒タイムアウトで動作。`SimpleWebSource` 経由 R2 取得 |
| `App.UpdateCheckStateChanged` 静的イベント | `_isCheckingUpdate` フラグ遷移を `TryBeginUpdateCheck` / `EndUpdateCheck` ヘルパーで発火。`MainWindowViewModel.IsCheckingUpdate` を駆動し、起動時自動チェック中も「アップデート確認」ボタンが自動 disabled (並走実行防止) |
| `LhamielUpdateStrings` | `VelopackUpdateDialog.IUpdateDialogStrings` の Lhamiel 実装 (Models/)。`Text.SelfUpdate.*` / `Text.Close` を `App.Text()` 経由で動的解決 (シングルトン、`NotifyLocaleChanged()` でロケール切替即時反映) |
| `IpcService` | Single-instance enforcement via Named Pipe (`PipeOptions.CurrentUserOnly`) |
| `FolderOpener` | Opens explorer to extraction/compression output folder |
| `CompressionPasswordSession` | 圧縮パスワード平文を DPAPI (`DataProtectionScope.CurrentUser`) で暗号化/復号する短寿命ヘルパ。`Protect(string)`→`byte[]` を `Settings.EncryptedCompressionPassword` に永続化、`TryUnprotect(byte[])`→`string?` で復号 (失敗時 null、Settings 側は自動 wipe しない)。中間 `byte[]` は `CryptographicOperations.ZeroMemory` で best-effort 0 埋め。最大平文長 1024 chars。`v1.0.181+` |
| `IPasswordDialogService` / `DefaultPasswordDialogService` | `PasswordDialog.ShowFromBackgroundAsync` を `ArchiveProcessor.PasswordDialogImpl` 経由で差し替え可能化したファサード。展開時 (`PasswordDialogMode.Extract`) と新規圧縮時 (`PasswordDialogMode.CompressNew` — 確認入力欄あり) を mode 引数で切替。テストではスタブで置換 |

### Testability Pattern

DI コンテナ不導入の方針に従い、`ArchiveProcessor` の外部依存は `internal static` プロパティで差し替え可能にしている:

```csharp
// プロダクションコード
internal static IMessageService MessageServiceImpl { get; set; } = new DefaultMessageService();
internal static IUiDispatcher UiDispatcherImpl { get; set; } = new DefaultUiDispatcher();
internal static IConflictDialogService ConflictDialogImpl { get; set; } = new DefaultConflictDialogService();

// テストコード
ArchiveProcessor.MessageServiceImpl = new StubMessageService();
// ... テスト実行 ...
// IDisposable.Dispose() で元に戻す
```

インターフェース定義: `ServiceContracts.cs` (`IMessageService`, `IUiDispatcher`, `IConflictDialogService`)

### Error Handling Strategy

1. **HResult 二段判定** (`ArchiveErrorHandler`): まず `HResult` 定数でエラー分類 → 不一致時にメッセージ文字列走査にフォールバック
2. **LockedFileRetryPolicy**: SHARING_VIOLATION (`0x80070020`) / LOCK_VIOLATION (`0x80070021`) を指数バックオフでリトライ（同期 6 回 / 非同期 3 回）
3. **Settings.Load 3 段フォールバック**: `JsonSerializer.Deserialize` → `JsonDocument` 個別プロパティ回収 → 破損ファイル退避（Move → Delete → 空 JSON 上書き）

### File Conflict Resolution System

ファイル衝突解決は `FileConflictDialog` に統合。
- **展開時**: 2ペイン比較（現在の場所 vs 宛先の場所）
- **圧縮時**: 縦1列リストでグループ表示
- モデル: `FileConflictEntry` / `FileConflictGroup` / `FileConflictResult`（`Models/FileConflictInfo.cs`）

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

Adding a new locale: create `Resources/Locales/{xx_YY}.axaml` → add `ResourceInclude` in `App.axaml` → add to `App.SupportedLocales` + `App.LocaleDisplayNames` → add to `MainWindowViewModel.LocaleOptions`

**Pitfall**: `ResourceInclude` implements `IResourceProvider`, NOT `ResourceDictionary` — type casts must use `IResourceProvider`.

## Key Technical Details

- **Avalonia 12, not WPF** — compiled bindings (`x:CompileBindings="True"`), FluentTheme. `ExtendClientAreaChromeHints` は削除済み → `WindowDecorations` を使う
- **Native AOT** (`PublishAot=true`) — avoid reflection-heavy patterns
- **7z.dll** — `1llum1n4t1s.Sevenzip` NuGet が同梱。`NativeLibraryManager` が起動時に `LoadLibrary` で固定
- **ネイティブ操作の直列化** — `1llum1n4t1s.Sevenzip` の共有シングルトン `SevenZipLibrary` は `ArchiveReader`/`ArchiveWriter` の並行動作をサポートしない (refcount + COM 追跡が直列前提)。Lhamiel はバッチ展開・圧縮で `ArchiveProgressHelper.IoBoundParallelism` (2〜4) の並列度を使うため、**全ネイティブ接触点 (reader/writer の生成→使用→Dispose) を `NativeArchiveGate` (`SemaphoreSlim(1,1)`) で 1 スロットに直列化**する。純 I/O 後処理 (最終移動・MotW 伝播) はゲート外で並行のまま。新たなネイティブ接触点を追加するときは既存ゲートスコープの内側で取得しない (非リエントラントなので入れ子はデッドロック)
- **Logger** — `SuperLightLogger` File Target, `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log`
- **Velopack** 自動更新 — 配信元は **Cloudflare R2 単独** (`https://lhamiel.nephilim.jp`、`SimpleWebSource` 経由)。通常リリース (`/vava`) は R2 のみに配信する。配信ドメインは中立ドメイン `lhamiel.nephilim.jp` に移行済み (旧 `lhamiel.1llum1n4t1.com` はクラウド/企業 egress の SNI フィルタで false positive を起こすため)。**旧 `lhamiel.1llum1n4t1.com` は配信期間が短かったためクリーンに廃止** (R2 踏み台として残さない)。旧 `GithubSource` クライアント (v1.0.167 以下) 救済のため、**GitHub Releases には `nephilim.jp` 版を「踏み台」として publish する** (`GithubSource` は最新版を選ぶので、それ経由で更新 → 再起動後に `nephilim.jp` を見るようになる。踏み台は削除せず永続保持)。継続的な GitHub Releases 併用配信はしない。2 系統: (1) `Program.cs --update-check` サイレント CLI 経路 (Windows ログイン時 `StartupRegistration` から発火、UI 無し)、(2) `App.Check4Update` UI 経路 (`VelopackUpdateDialog.Avalonia` 1.0.3 経由のダイアログ表示、`Settings.Check4UpdatesOnStartup=true` で起動時自動 + メニューから手動)
- **AllowUnsafeBlocks** for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`, `CrashHandler`)
- **Acrylic blur** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint`
- Async/await + CancellationToken throughout all I/O operations
- Version: `Directory.Build.props` の `<Version>` タグで全プロジェクト共有
- **Unicode NFC normalization**: macOS HFS+ の NFD ファイル名を展開・圧縮時に NFC 正規化（`Settings.NormalizeUnicodeFileNames`）
- **Long path support**: `app.manifest` で `longPathAware` + `PathValidator.EnsureLongPathPrefix`
- **Mark of the Web**: 元アーカイブの Zone.Identifier ADS を展開ファイルに伝播（`Settings.PropagateMarkOfTheWeb`）
- **Password-protected compression** (`v1.0.181+`): `Settings.IsPasswordProtectionEnabled`=true で ZIP=AES-256 (WinZip AE-2)、7z=AES-256 を強制 (`ArchiveCompressor.CreateArchiveWriter` で `EncryptionMethod.Aes256` + `CustomParameters["he"]="on"`)。TAR は非対応 — 3 層ガード: (1) UI は TAR 選択時に checkbox を disable、(2) `TryResolveCompressionPasswordAsync` は formatHint=TAR で password 解決をスキップして「保護なし」に coerce (明示 `--format TAR` の CLI/シェル経路が ZIP/7z 用の保存済み保護選好で誤爆しないため)、(3) `ArchiveCompressor.CreateArchiveWriter` は非 null password + TAR で `InvalidOperationException` (本物のバグ検知用 fail-loud)。パスワード入力は `Settings.PasswordMode` で 2 モード: `"PromptEachTime"` (ドロップ毎に確認) と `"Remember"` (DPAPI 暗号化済バイト列を `Settings.EncryptedCompressionPassword` に保存)。`ArchiveProcessor.TryResolveCompressionPasswordAsync` がモード分岐を集約し、復号失敗 (別 PC/Win パスワードリセット) は通知 + 再プロンプトで対応 (Settings 側は自動 wipe しない、サイレント wipe による失念事故を防ぐ)。`EncryptFileNames` は `[JsonIgnore]` で永続化対象外 — VM の `ObservableProperty` のみ、`IsPasswordProtectionEnabled` OFF→ON 遷移で毎回 `true` に強制リセット (`OnIsPasswordProtectionEnabledChanged`)。ZIP は仕様上ヘッダ暗号化不可なので `EncryptFileNames` は 7z 限定 (UI で disable + 警告)。**ZIP パスワードは ASCII 限定** — 同梱 7-Zip 26.00 が ZIP 作成時に非 ASCII パスワードを `E_INVALIDARG` で拒否する upstream regression のため (7z は非 ASCII OK、実機確認済み)。2 層対応: (1) `TryResolveCompressionPasswordAsync` が formatHint=ZIP で入力検証 + `Error.ZipPasswordAsciiOnly` 通知 + 再プロンプト (上限 5 回、`PromptCompressionPasswordAsync`)。Remember の保存済みパスワードが非 ASCII + ZIP のときは `Notify.SavedPasswordZipAsciiOnly` を出して**今回限りの一時パスワード**を再プロンプト (7z 用に有効な保存値は上書きしない)、(2) `ArchiveCompressor.CreateArchiveWriter` の fail-fast guard (`ContainsNonAscii`、バッチ override 等の迂回経路の防御線)。本家修正で 26.00 regression が解消されたら `SevenZipFormat_WithNonAsciiPassword_Succeeds` sentinel テストを参考に制約解除を検討。空アーカイブ防止: `addedCount==0` で `InvalidOperationException`、`Error.AllSourcesInaccessible` を表示 (スキャン後の全ファイル消失・全件アクセス不能が対象)。空ディレクトリ単体のドロップは `IncludeRoot` モードなら root 自身を空ディレクトリエントリとして追加し有効なアーカイブを作る (`ScanSourceFiles` の root マーカー、ExcludeRoot/Flat は相対パスが `.` になるため対象外)。パスワード平文は `Logger.RegisterRedactionToken` で全ログ自動マスク (defense-in-depth、4 文字以上の token のみ)、`DiagnosticsCollector` の `_sensitiveKeys` に `EncryptedCompressionPassword` を追加して support ZIP からも除外 (dumps は `v1.0.181+` で常時除外)。**新規圧縮パスワードは 4 文字以上を強制** — `PasswordDialog.MinCompressPasswordLength` (CompressNew モードのみ、Extract は既存書庫互換のため制限なし)。redaction の 4 文字下限と連動し「マスクされない圧縮パスワード」の存在をなくす (連動契約は `MinCompressPasswordLength_PasswordIsAlwaysRedactable` テストが担保)。設定パネルの「パスワード変更」(`ChangeSavedPasswordAsync`) も `ArchiveProcessor.PromptCompressionPasswordAsync` (internal) を共用し、ZIP 選択中は同じ ASCII 検証 + 再プロンプトを通す。
- **Header-encrypted (he=on) 7z の展開**: 同梱 7-Zip 26.00 はヘッダ暗号化アーカイブをパスワード無しで開くと **ctor 時点で `SevenZipException` (IsNotArc) を投げ「破損」と区別できない**（実機確認済み）。このため `GetArchiveStructureInfo` は開けなかったとき `ArchiveStructureInfo.OpenFailed=true` を返し、`ArchiveProcessor.ExtractArchiveAsync` が **拡張子 .7z/.rar に限り**パスワード確認 → password 付き再解析（最大 3 回）を行う。成功したパスワードは `knownPassword` として `ExtractArchiveAsync` → `ExtractArchive` の AsyncPasswordQuery 初回応答・`DetectExtractionConflicts`・`ArchiveIntegrityVerifier.VerifyArchiveAsync` に引き回し、展開中の再ダイアログを防ぐ + ヘッダ暗号化でもフォルダ二重ネスト防止 (`ShouldSkipFolderCreation`) と CRC 検証が機能する。全試行失敗時は従来経路（パスワード無し）に合流し、本当に破損したアーカイブの UX を変えない。**明示キャンセルは展開経路と同じ `OperationCanceledException` でこのアーカイブの展開ごと中止する**（従来経路に合流させると展開中の AsyncPasswordQuery が再ダイアログを出すため。バッチ側は OCE を失敗ではなくスキップ扱い）。**プロンプトループ全体は `ArchiveProcessor.StructurePasswordPromptGate` (`SemaphoreSlim(1,1)`) で直列化** — バッチ展開の `IoBoundParallelism` 並列でモーダルダイアログが積み重なるのを防ぐ（`NativeArchiveGate` は `GetArchiveStructureInfo` 内部で取得される非リエントラント構造のため流用不可。取得順は常に「プロンプトゲート → NativeArchiveGate (一時取得)」の一方向のみ）。redaction はパスワードを渡す再解析の**前**に試行ごと登録（解析例外メッセージ経由の平文混入防止）+ 確定後は `Logger.RegisterRedactionToken(knownPassword)` がメソッド終端まで引き継ぐ。
- **VM 設定の即時フラッシュ**: `MainWindowViewModel` の AutoSave は 300ms デバウンスのため、永続層 (`SettingsManager`) スナップショットを取る直前の UI 変更が未反映のことがある。対策として `MainWindowViewModel.Current` (static、ctor で設定) + `FlushPendingAutoSave()` を、(1) `ArchiveProcessor` の Remember パスワード保存直前 (`MutateAndSave` の live 再チェックが古い値を見ないように)、(2) `App.ProcessCommandLineFiles` 冒頭 (シェル/IPC 経由圧縮のスナップショット鮮度) の 2 箇所で呼ぶ。どちらも UI スレッドから呼ぶこと。テストは VM を構築しないので `Current` は null → no-op。
- **Compression scan attributes**: `Settings.IncludeHiddenAndSystemEntries` が圧縮スキャン時の `EnumerationOptions.AttributesToSkip` を制御する。
- **Compression exclusions**: `%LocalAppData%\Lhamiel\.lhaignore` に `.gitignore` 互換構文で記述。`GitignoreMatcher` が compile して `ArchiveCompressor` 側でディレクトリ枝刈り付きマッチを行う。UI 追加・削除・既定値リセット + 「除外設定ファイルを開く」（既定エディタで開く）の 4 操作で管理し、`FileSystemWatcher` が外部編集を検知して UI を再同期する。
- **Nested .gitignore**: `Settings.RespectNestedGitignore` (default **false / オプトイン**) が ON なら、圧縮対象のサブディレクトリ内の `.gitignore` をスキャン前に発見し、各 `.gitignore` をそのスコープで `GitignoreMatcher.CompileLayered` に追加。`.lhaignore` の枝刈り後のディレクトリのみ探索する（node_modules 内の .gitignore は読まない）。除外判定は DFS 枝刈り + `IsExcluded(traversalMode: true)` の「自身レベル照合」で行うため、git と同様に**ディレクトリ否定再包含**（`*.xcodeproj/*` + `!*.xcodeproj/xcshareddata/` で配下の共有ファイルを救い、`d/*`+`!d/sub/` と `d/`+`!d/sub/` の差も区別）を正しく扱う。なお `.gitignore` パターン一致のみで判定するため、git の「追跡済みファイルは ignore に勝つ」例外（`git add -f` した除外パターン該当ファイル）は再現しない。

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test + code coverage on every PR
- **Release**: `.github/workflows/velopack-release.yml` — `release/*` ブランチへの push でトリガー。`vpk pack` で win + win-arm64 を並列ビルド後、`r2-upload` job が `wrangler@4.x` (Node.js 22) で Cloudflare R2 バケット `lhamiel-updates` にアップロード + `curl --fail` で配信確認 (`releases.{channel}.json` HTTP 200 検証) + **manifest 外の旧 `*.nupkg` を Cloudflare API V4 で自動削除する cleanup step** (Aggressive 保持戦略: `releases.{channel}.json` に書かれない nupkg は削除、Setup.exe / Portable.zip / RELEASES* / assets.*.json / releases.*.json は固定ファイル名で上書きされる Velopack 内部ファイル & ランディング DL 用なので保護)。**R2 単独配信** (GitHub Releases への継続 publish はしない。旧クライアント救済の踏み台は `/transfer-cf` 移行作業で publish 済み)。必要 Secrets: `CLOUDFLARE_API_TOKEN` / `CLOUDFLARE_ACCOUNT_ID`
- **CodeQL**: `.github/workflows/codeql.yml` — C# security analysis on PR + weekly
- **Dependabot**: `.github/dependabot.yml` — NuGet weekly + github-actions monthly

## Testing

Tests in `Lhamiel.Tests.Unit/` using **xUnit 3** + Moq. The test project references the main project directly via `InternalsVisibleTo`.

- 通常テスト: `*Tests.cs`
- 嫌がらせテスト (adversarial): `*AdversarialTests.cs` — 境界値、異常入力、状態遷移の矛盾
- `[Collection("ArchiveProcessor")]` — `ArchiveProcessor` の static プロパティを差し替えるテストは排他実行が必要
- ADS テスト (`MotwPropagatorTests`): Windows 限定、`Assert.SkipWhen(!OperatingSystem.IsWindows())` でスキップ

## Documentation

- `docs/ARCHITECTURE.md` — detailed system design and data flows
- `docs/SETTINGS_SCHEMA.md` — complete settings.json reference
- `docs/PARALLEL_IMPLEMENTATION_REPORT.md` — parallel processing research
