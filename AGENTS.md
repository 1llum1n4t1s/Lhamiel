# AGENTS.md

This file provides guidance to Codex (ChatGPT) and other coding agents working in this repository.
AGENTS.md is the canonical project-wide guidance file; keep it current when updating conventions.

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
vpk pack --packId Lhamiel --packVersion <VERSION> --packTitle "Lhamiel" --packAuthors "Lhamiel" --mainExe Lhamiel.exe --icon src/Lhamiel/icon/app.ico --packDir local-publish --outputDir local-installer --channel win --shortcuts "StartMenuRoot,Desktop"
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
4. Post-extraction: MotW propagation (`MotwPropagator`)。CRC は展開中に 7z.dll が常時照合（不一致は `SetOperationResult(CRCError)` → ライブラリが Cancel 返却 → `reader.Save` が例外、の構造的保証）。展開後の二度読み `reader.Test()` パスは v1.0.183 で廃止し、`Settings.VerifyAfterExtraction` は legacy no-op
5. `ProgressWindow` shows real-time progress via `IProgress<T>`

**圧縮の進捗表示契約** (528,450 ファイル / 60GB の実測で「100% のまま 9 分超」になった問題への対応): バイト % が動かない区間はマーキー (IsIndeterminate) + 経過テキストで埋める — (1) 容量見積り `Progress.CheckingDiskSpace`、(2) スキャン `Progress.ScanningFiles` (発見件数、`ScanSourceFiles` の `progress` 引数)、(3) `writer.Add` ループ (実測 93 秒/528k 件) と Save 冒頭の Prepare 列挙は `Progress.PreparingCompression` (i/N、`ProgressTextIntervalMs`=200ms スロットル。ライブラリの Prepare 報告は素通しで数十万件届くためここで間引く)、(4) バイト進捗 100% 到達後〜`writer.Dispose` 完了 (セントラルディレクトリ書出し + 数十万ストリーム一括 close) は `Progress.Finalizing` — `finalizing` フラグで以後の確定 % を抑止、(5) 完了直前に確定 100% を 1 回報告 (テスト契約: `CompressFilesAsync_ReportsFinalizingBeforeFinal100`)。pct=0 は `ProgressThrottler` の boundary 扱いで素通りするため 1 回に抑える。並列バッチ (`CreateMappedProgress` totalCount>1) では indeterminate を `SetNotice` に降格してバーの marquee 点滅を防ぐ。**主因はライブラリ ≤1.0.78 の `UpdateCallback.SetCompleted` 過剰計上**（completeValue はグローバル累積値なのに「ファイル毎リセット検出」で二重加算 → データ処理の 58% 時点で表示 100% 到達）— ライブラリ側で単調最大値方式に修正済み (要 1.0.79+ への PackageReference 更新)。なおライブラリは圧縮中、入力ファイルを `FileShare.Read` で writer Dispose まで保持するため、**圧縮中の対象ファイルは書き込みロックされる**（上流設計妥協、ライブラリ AGENTS.md 参照）。

**展開時の出力先決定** (`ArchiveProcessor`):
- `CreateArchiveNameFolder=ON` + ルートフォルダがアーカイブ名と一致 → フォルダ作成スキップ（`ShouldSkipFolderCreation`）
- `CreateArchiveNameFolder=ON` + それ以外 → `baseDir/アーカイブ名/` フォルダを作成
- `CreateArchiveNameFolder=OFF` → `baseDir` に直接展開
- 複合拡張子（`.tar.gz` 等）は `GetArchiveBaseName()` で正しく処理

**展開後にフォルダを開く**: フォルダ決定ロジックは `FolderOpener.GetExtractionFolderToOpen` に集約。呼び出し側は展開時の `createArchiveNameFolder` 設定値を渡す（展開中の設定変更による不整合を防止）。explorer 起動は **2 系統**: (1) 常駐インスタンス（ドラッグ&ドロップ / `FileConflictDialog`）は `FolderOpener.OpenFolder` / `OpenExtractionResult`（fire-and-forget、`ShellOpener` の `Task.Run` で UI スレッド非ブロック、Issue #54 対策）。(2) **処理後に自己終了する CLI / ファイル関連付け / アイコンドロップ経路は `OpenFolderAsync` / `OpenExtractionResultAsync` を `await`** してから `desktop.Shutdown()` する（`App.axaml.cs` の `ProcessFileExtraction` / `ProcessMultipleExtractions` / `ProcessCompression` / `ProcessMergedCompression`）。await しないと explorer 起動 `Task` がバックグラウンドで走り切る前にプロセスが落ち「展開先を開く」が効かない競合になる（fire-and-forget 化した v1.0.171 で混入した回帰）。**ただし await 化（#61）だけでは不十分だった**: App.axaml に `ShutdownMode` 指定が無く既定が `OnLastWindowClose` のため、`ProgressWindow` のクローズ（`ArchiveProcessor` が `closeWindowOnCompletion` 経由で `CloseSafe` → Dispatcher に Post）が `await OpenExtractionResultAsync`（別スレッドの `Process.Start`）の最中に処理されると、「最後のウィンドウクローズ → 自動シャットダウン」が explorer 起動と競合し、起動し切る前にプロセスが落ちてフォルダが開かない（#61 の await は明示 `ShutdownIfNeeded` 経路しか守れず暗黙の自動シャットダウンが残っていた）。対策として `RunWithProgressWindowAsync` は自己終了経路（`shouldShutdown=true`）の操作中だけ `ShutdownMode.OnExplicitShutdown` に切替えて自動シャットダウンを抑止し、explorer 起動完了後に明示 `ShutdownIfNeeded`、`finally` で元の `ShutdownMode` に戻す（戻した後の `OnLastWindowClose` が「ダイアログ表示中でシャットダウンを見送ったケース」の終了の安全網になる）。IPC 経路（`shouldShutdown=false`）は常駐 MainWindow が居るため `ShutdownMode` を触らない。ドロップ経路の設定スナップショットは `ProcessDroppedPathsAsync` が `ApplySettingsToManager()` で VM 全設定を確定してから `CreateSnapshot()` する（300ms デバウンス中の設定切替で `OpenExtractionOutputFolder` 等が陳腐化するのを防止）

**関連付け設定の適用タイミング** (`MainWindowViewModel.ApplyAssociationSettings`): 関連付けの実適用（レジストリ書込 + `FileAssociation.NotifyExplorer` の `SHChangeNotify`）はチェックボックス操作・全選択/解除・アイコンバリアント変更時の **3 経路のみ**で、起動時・終了時には行わない。アイコンバリアント変更（`OnSelectedFileIconVariantChanged` → `ApplyAssociationSettings(refreshAssociatedIcons:true)` で関連付け済み全拡張子のアイコンを再登録）は、関連付けタブの `ComboBox`（`SelectedValue` + `SelectedValueBinding`）が起動/タブ初表示時に `SelectedFileIconVariant` を「現在値 → null/空 → 現在値」と書き戻して `OnSelectedFileIconVariantChanged` を多重発火させるため、`_appliedFileIconVariant`（`LoadFromSettings` で初期化する正規化済みの適用済み値）と照合し **実際にバリアントが変わったときだけ** 再適用する（空値・同値は無視）。このガードが無いと起動のたびに全 18 拡張子を再登録 + `SHChangeNotify` する『常時関連付け』スパムになる（ログが関連付け成功で埋まる回帰）。

**圧縮後にフォルダを開く** (`OpenCompressionOutputFolder`): 開く出力フォルダは `MainWindowViewModel.ResolveCompressionOutputFolder` で決定。「元と同じ場所に保存」(`CompressionOutputToSameDirectory`) ON のときはソースのあるディレクトリ（＝アーカイブが作られる場所、`Path.GetDirectoryName(firstSource)`）、OFF のときは `CompressionOutputDirectory` を開く。**ドラッグ&ドロップ経路も SameDirectory ON で開く**（以前は `&& !CompressionOutputToSameDirectory` ガードで開かず、CLI 経路だけ開く非対称があった。CLI の `App.ProcessCompression` / `ProcessMergedCompression` と挙動統一）

**圧縮時のロック中ファイル対応**: ソースファイルは元パスのまま `ArchiveWriter.Add()` に渡す。ロック中のファイルはライブラリ（`1llum1n4t1s.Sevenzip`）の `UpdateCallback` が自動的に一時コピーして処理する。スキャン後に削除されたファイルは `File.Exists()` チェックでスキップし、残りのファイルで圧縮を続行する。**`writer.Add()` が `AccessException` を投げた場合（VS の `.vsidx` のように `FileShare.None` で握られていてライブラリの 2 段試行（`FileShare.Read` → `FileShare.ReadWrite|Delete`）で両方失敗するケース）は、当該ファイルのみログに warning を出してスキップし、残りで圧縮を続行する**（1 ファイルアクセス不能で全体を死なせない）。スキップ件数は完了直前に集約ログを残し、`CompressFilesAsync` の戻り値（`Task<int>`）として呼び出し側へ返す。**パスワード保護圧縮でスキップ > 0 のときは `ArchiveProcessor` が `Notify.PartialSkipWithPassword` を UI 通知する**（スキップされたファイルは暗号化アーカイブに含まれず平文のまま残るため。非保護圧縮は従来どおりログのみで resilience を維持、codex P2 #3386876544）。

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
| `App.Check4Update` | Velopack 自動更新の **UI 経路**。`Settings.Check4UpdatesOnStartup=true` のときメイン画面起動直後 + 「アップデート確認」ボタンから手動起動。`VelopackUpdateDialog.UpdateDialogWindow` をオーナー付きで表示し、30 秒タイムアウトで動作。Velopack は `VelopackUpdateDialog.Avalonia` 経由の transitive 参照で常に最新へ追従 (release-local.ps1 の vpk も実行時に NuGet 最新を解決)。`SimpleWebSource` 経由 R2 取得 |
| `App.UpdateCheckStateChanged` 静的イベント | `_isCheckingUpdate` フラグ遷移を `TryBeginUpdateCheck` / `EndUpdateCheck` ヘルパーで発火。`MainWindowViewModel.IsCheckingUpdate` を駆動し、起動時自動チェック中も「アップデート確認」ボタンが自動 disabled (並走実行防止) |
| `LhamielUpdateStrings` | `VelopackUpdateDialog.IUpdateDialogStrings` の Lhamiel 実装 (Models/)。`Text.SelfUpdate.*` / `Text.Close` を `App.Text()` 経由で動的解決 (シングルトン、`NotifyLocaleChanged()` でロケール切替即時反映) |
| `AcrylicFallbackHelper` | アクリルブラーが実際には効かない環境（Windows 透過効果 OFF・リモートデスクトップ・非対応プラットフォーム）を検出し、`ExperimentalAcrylicBorder` を隠してテーマ色 (`Brush.Window`) の不透明背景に差し替える（透過 OFF だとアクリル背後が不透明黒になり、ライトの薄い tint が黒と混ざって灰色化するため）。Windows は透過 OFF でも `ActualTransparencyLevel`=AcrylicBlur を返すため、レジストリ `EnableTransparency` + `GetSystemMetrics(SM_REMOTESESSION)` で補正。全 7 ウィンドウの ctor から `Attach(this)` で取付（Opened / Activated / 透過レベル変化で再評価、テーマ切替は共有ブラシ `Brush.Window` で自動追従） |
| `AccentTintHelper` | OS のアクセントカラーをテーマ基調色の上に α 0x18 (≈9%) でごく薄く上乗せするオーバーレイ（ライト + 青アクセント → 薄い水色）。ルート Panel のアクリル直後に挿入するため、アクリル有効時もフォールバック時も同様に効く。`PlatformSettings.ColorValuesChanged` で OS 側のアクセント変更に即追従（ダイアログは Closed で購読解除）。全 7 ウィンドウの ctor から `Attach(this)`。旧 MainWindow 専用 `AccentOverlay` (Opacity 0.04・ほぼ知覚不能) はこれに置換済み |
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
- **Windows 11 右クリックメニュー** — 新メニューは `src/Lhamiel.ShellExtension` のネイティブ `IExplorerCommand` DLL と `Nephilim.Lhamiel.ContextMenu` sparse MSIX（外部配置パッケージ）で登録する。`ShellContextMenu.SetEnabled` は Windows 11 で package を登録／解除し、成功時は従来の `HKCU\Software\Classes\{*,Directory}\shell\Lhamiel.SendTo` verb を削除してクラシックメニューの重複を防ぐ。Windows 10・Shell 配布物の無い開発ビルドでは静的 verb を使う。`scripts/build-shell-integration.ps1` が x64/ARM64 DLL と sparse MSIX を生成し、`release-local.ps1` が Authenticode 署名後に Velopack 出力へ含める。パッケージの Publisher は Certum 証明書 Subject と完全一致させ、CLSID `ABB8423C-A40B-4259-9F8A-6C62435C29CA` は manifest と C++ 実装で共通に保つ
- **7z.dll** — `1llum1n4t1s.Sevenzip` NuGet が同梱。`NativeLibraryManager` が起動時に `LoadLibrary` で固定
- **ネイティブ操作の直列化** — `1llum1n4t1s.Sevenzip` の共有シングルトン `SevenZipLibrary` は `ArchiveReader`/`ArchiveWriter` の並行動作をサポートしない (refcount + COM 追跡が直列前提)。Lhamiel はバッチ展開・圧縮で `ArchiveProgressHelper.IoBoundParallelism` (2〜4) の並列度を使うため、**全ネイティブ接触点 (reader/writer の生成→使用→Dispose) を `NativeArchiveGate` (`SemaphoreSlim(1,1)`) で 1 スロットに直列化**する。純 I/O 後処理 (最終移動・MotW 伝播) はゲート外で並行のまま。新たなネイティブ接触点を追加するときは既存ゲートスコープの内側で取得しない (非リエントラントなので入れ子はデッドロック)
- **Logger** — `SuperLightLogger` File Target, `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log`。**ログ経路では `Environment.UserName` を呼ばない** — 内部の GetUserNameExW (secur32 → LSA RPC) が RDP セッション等で無期限ブロックし、ログ 1 行で全スレッドが凍結する (実機 dump で確認済み、テストの断続的ハングの原因だった)。`MaskUserPath` は環境変数 USERNAME + プロファイルフォルダ名の事前コンパイル済みパターン (`_userNameMaskPatterns`、起動後 1 回構築) を使う
- **Velopack** 自動更新 — 配信元は **Cloudflare R2 単独** (`https://lhamiel.nephilim.jp`、`SimpleWebSource` 経由)。通常リリース (`/vava`) は R2 のみに配信する。配信ドメインは中立ドメイン `lhamiel.nephilim.jp` に移行済み (旧 `lhamiel.1llum1n4t1.com` はクラウド/企業 egress の SNI フィルタで false positive を起こすため)。**旧 `lhamiel.1llum1n4t1.com` は配信期間が短かったためクリーンに廃止** (R2 踏み台として残さない)。旧 `GithubSource` クライアント (v1.0.167 以下) 救済のため、**GitHub Releases には `nephilim.jp` 版を「踏み台」として publish する** (`GithubSource` は最新版を選ぶので、それ経由で更新 → 再起動後に `nephilim.jp` を見るようになる。踏み台は削除せず永続保持)。継続的な GitHub Releases 併用配信はしない。2 系統: (1) `Program.cs --update-check` サイレント CLI 経路 (Windows ログイン時 `StartupRegistration` から発火、UI 無し)、(2) `App.Check4Update` UI 経路 (`VelopackUpdateDialog.Avalonia` 1.0.3 経由のダイアログ表示、`Settings.Check4UpdatesOnStartup=true` で起動時自動 + メニューから手動)
- **Code signing (Authenticode)** — `v1.0.183+` 全リリースバイナリ (Lhamiel.exe / Setup.exe / Portable 内含む) を Certum **Open Source Code Signing in the cloud** 証明書で署名する。CN=`Open Source Developer Yuichiro Shinozaki` (個人 OSS 開発者向け、年次更新で thumbprint が変わるため signtool は `/n` の Subject 名選択を使う)。鍵は SimplySign クラウド (DPAPI 不可・エクスポート不可)、署名には **SimplySign Desktop のトークンログイン中セッションが必須** → リリースは `scripts/release-local.ps1` でローカル実行 (CI 署名不可、§CI/CD 参照)。タイムスタンプは `http://time.certum.pl` (RFC3161) — 証明書期限 (1 年) 切れ後も署名済みバイナリは有効。単発署名: `signtool sign /n "Open Source Developer Yuichiro Shinozaki" /fd SHA256 /td SHA256 /tr http://time.certum.pl <file>` (bash から叩く場合は `MSYS2_ARG_CONV_EXCL='*'` 必須)
- **AllowUnsafeBlocks** for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`, `CrashHandler`)
- **Acrylic blur** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint`
- Async/await + CancellationToken throughout all I/O operations
- Version: `Directory.Build.props` の `<Version>` タグで全プロジェクト共有
- **Unicode NFC normalization**: macOS HFS+ の NFD ファイル名を展開・圧縮時に NFC 正規化（`Settings.NormalizeUnicodeFileNames`）
- **Long path support**: `app.manifest` で `longPathAware` + `PathValidator.EnsureLongPathPrefix`
- **Mark of the Web**: 元アーカイブの Zone.Identifier ADS を展開ファイルに伝播（`Settings.PropagateMarkOfTheWeb`）
- **Password-protected compression** (`v1.0.181+`): `Settings.IsPasswordProtectionEnabled`=true で ZIP=AES-256 (WinZip AE-2)、7z=AES-256 を強制 (`ArchiveCompressor.CreateArchiveWriter` で `EncryptionMethod.Aes256` + `CustomParameters["he"]="on"`)。TAR は非対応 — 3 層ガード: (1) UI は TAR 選択時に checkbox を disable、(2) `TryResolveCompressionPasswordAsync` は formatHint=TAR で password 解決をスキップして「保護なし」に coerce (明示 `--format TAR` の CLI/シェル経路が ZIP/7z 用の保存済み保護選好で誤爆しないため)、(3) `ArchiveCompressor.CreateArchiveWriter` は非 null password + TAR で `InvalidOperationException` (本物のバグ検知用 fail-loud)。パスワード入力は `Settings.PasswordMode` で 2 モード: `"PromptEachTime"` (ドロップ毎に確認) と `"Remember"` (DPAPI 暗号化済バイト列を `Settings.EncryptedCompressionPassword` に保存)。`ArchiveProcessor.TryResolveCompressionPasswordAsync` がモード分岐を集約し、復号失敗 (別 PC/Win パスワードリセット) は通知 + 再プロンプトで対応 (Settings 側は自動 wipe しない、サイレント wipe による失念事故を防ぐ)。**復号できても `MinCompressPasswordLength` (4 文字) 未満の保存値は使用しない** — redaction 不能なまま圧縮スコープに入るのを防ぐため、`Notify.SavedPasswordTooShort` を通知して CompressNew 再プロンプト → 新しい値を保存して移行する (4 文字フロア導入前ビルドの legacy 保存値対策、codex P2 #3390183195)。`EncryptFileNames` は `[JsonIgnore]` で永続化対象外 — VM の `ObservableProperty` が真の源、`IsPasswordProtectionEnabled` OFF→ON 遷移で毎回 `true` に強制リセット (`OnIsPasswordProtectionEnabledChanged`)。**in-memory の `Settings` へは 2 経路で同期**: ドロップ経路は snapshot 直前の `Mutate`、シェル/IPC 経路は `FlushPendingAutoSave` → `ApplySettingsToManager`（`Snapshot()` は `MemberwiseClone` なので `[JsonIgnore]` 値も snapshot に乗る。同期しないと UI でチェックを外しても snapshot がデフォルト `true` を引きずり he=on で圧縮される）。ZIP は仕様上ヘッダ暗号化不可なので `EncryptFileNames` は 7z 限定 (UI で disable + 警告)。**ZIP パスワードは ASCII 限定** — 同梱 7-Zip 26.00 が ZIP 作成時に非 ASCII パスワードを `E_INVALIDARG` で拒否する upstream regression のため (7z は非 ASCII OK、実機確認済み)。2 層対応: (1) `TryResolveCompressionPasswordAsync` が formatHint=ZIP で入力検証 + `Error.ZipPasswordAsciiOnly` 通知 + 再プロンプト (上限 5 回、`PromptCompressionPasswordAsync`)。Remember の保存済みパスワードが非 ASCII + ZIP のときは `Notify.SavedPasswordZipAsciiOnly` を出して**今回限りの一時パスワード**を再プロンプト (7z 用に有効な保存値は上書きしない)、(2) `ArchiveCompressor.CreateArchiveWriter` の fail-fast guard (`ContainsNonAscii`、バッチ override 等の迂回経路の防御線)。本家修正で 26.00 regression が解消されたら `SevenZipFormat_WithNonAsciiPassword_Succeeds` sentinel テストを参考に制約解除を検討。空アーカイブ防止: `addedCount==0` で `InvalidOperationException`、`Error.AllSourcesInaccessible` を表示 (スキャン後の全ファイル消失・全件アクセス不能が対象)。空ディレクトリ単体のドロップは `IncludeRoot` モードなら root 自身を空ディレクトリエントリとして追加し有効なアーカイブを作る (`ScanSourceFiles` の root マーカー、ExcludeRoot/Flat は相対パスが `.` になるため対象外)。パスワード平文は `Logger.RegisterRedactionToken` で全ログ自動マスク (defense-in-depth、4 文字以上の token のみ)、`DiagnosticsCollector` の `_sensitiveKeys` に `EncryptedCompressionPassword` を追加して support ZIP からも除外 (dumps は `v1.0.181+` で常時除外)。**新規圧縮パスワードは 4 文字以上を強制** — `PasswordDialog.MinCompressPasswordLength` (CompressNew モードのみ、Extract は既存書庫互換のため制限なし)。redaction の 4 文字下限と連動し「マスクされない圧縮パスワード」の存在をなくす (連動契約は `MinCompressPasswordLength_PasswordIsAlwaysRedactable` テストが担保)。設定パネルの「パスワード変更」(`ChangeSavedPasswordAsync`) も `ArchiveProcessor.PromptCompressionPasswordAsync` (internal) を共用し、ZIP 選択中は同じ ASCII 検証 + 再プロンプトを通す。
- **Header-encrypted (he=on) 7z の展開**: 同梱 7-Zip 26.00 はヘッダ暗号化アーカイブをパスワード無しで開くと **ctor 時点で `SevenZipException` (IsNotArc) を投げ「破損」と区別できない**（実機確認済み）。このため `GetArchiveStructureInfo` は開けなかったとき `ArchiveStructureInfo.OpenFailed=true` を返し、`ArchiveProcessor.ExtractArchiveAsync` が **拡張子 .7z/.rar に限り**パスワード確認 → password 付き再解析（最大 3 回）を行う。成功したパスワードは `knownPassword` として `ExtractArchiveAsync` → `ExtractArchive` の AsyncPasswordQuery 初回応答・`DetectExtractionConflicts` に引き回し、展開中の再ダイアログを防ぐ + ヘッダ暗号化でもフォルダ二重ネスト防止 (`ShouldSkipFolderCreation`) が機能する（CRC は展開中に 7z.dll が照合、v1.0.183 で展開後の二度読み検証は廃止）。全試行失敗時は従来経路（パスワード無し）に合流するが、**`suppressPasswordPrompt` フラグで展開中の AsyncPasswordQuery の再プロンプトを抑止**する（「構造解析 3 回 + 展開段 3 回」の二重プロンプト防止。本当に破損したアーカイブはパスワードコールバック自体が呼ばれずエラー表示経路に進むため UX 不変）。**明示キャンセルは展開経路と同じ `OperationCanceledException` でこのアーカイブの展開ごと中止する**（バッチ側は OCE を失敗ではなくスキップ扱い）。**open 時のパスワード中止は `CryptoGetTextPassword` が `SevenZipCode.Cancel` を返すだけで `SevenZipException` (IsNotArc) になる**（EncryptionException ではない）ため、`ExtractArchive` は `passwordAcquisitionCancelled` フラグ + 種別 (EncryptionException or SevenZipException) で OCE に変換する。**プロンプトループ全体は `ArchiveProcessor.StructurePasswordPromptGate` (`SemaphoreSlim(1,1)`) で直列化** — バッチ展開の `IoBoundParallelism` 並列でモーダルダイアログが積み重なるのを防ぐ（`NativeArchiveGate` は `GetArchiveStructureInfo` 内部で取得される非リエントラント構造のため流用不可）。さらに**ダイアログ表示そのものは `ArchiveProcessor.ExtractionPasswordDialogGate`（葉ゲート、保持中に他ゲートを取得しない）で構造解析プロンプトと展開中プロンプトを横断直列化** — he=on と通常暗号化の混在バッチでもモーダルが積み重ならない。取得順は「StructurePasswordPromptGate → NativeArchiveGate → ExtractionPasswordDialogGate」の一貫階層でデッドロックなし。redaction はパスワードを渡す再解析の**前**に試行ごと登録（解析例外メッセージ経由の平文混入防止）+ 確定後は `Logger.RegisterRedactionToken(knownPassword)` がメソッド終端まで引き継ぐ。redaction 対象外の 1〜3 文字パスワード（Extract は既存書庫互換で受理）は、**パスワードが scope にある全ログサイトで例外詳細を生ログしない契約**（`Logger.CanRedactToken` で判定し、型名 + HResult の要約に置換）で全長カバーする — 対象: `GetArchiveStructureInfo` / `DetectExtractionConflicts` / `ExtractArchive` の汎用 catch と非キャンセル OCE 昇格 catch（判定は共通ローカル関数 `HasUnredactablePasswordInScope`、codex P2 #3390292697）/ `ArchiveProcessor.ExtractArchiveAsync` の catch（LogException を要約ログに切替 + **ダイアログ本文の Details も要約に置換** — `MessageService.ShowError` が本文を `Logger.Log` で永続化するため、codex P2 #3389751077）。`PasswordDialog.MinCompressPasswordLength` は `Logger.MinRedactionTokenLength` を直接参照し連動を構造的に固定。**ヘッダ可視の暗号化アーカイブ（パスワード ZIP / he=off 7z）は構造解析プロンプトを通らず `knownPassword` が null のまま**のため、展開中の AsyncPasswordQuery で入力されたパスワードを `onPasswordPrompted` コールバック（`ExtractArchiveAsync` → `ExtractArchive` に引き回し、7z.dll 由来スレッドから発火）で `ArchiveProcessor` に通知する（codex P2 #3386876537）: `ExtractArchive` ローカルと `ArchiveProcessor` の両層で全試行を redaction 登録（refcount 式なので二重登録安全、各層の catch ログ完了後に finally で解放）、1〜3 文字入力は両層の catch 詳細抑止フラグに反映。なおパスワード付きアーカイブの CRC も展開中に 7z.dll が照合する（展開後の二度読み `reader.Test()` 検証は v1.0.183 で廃止）。
- **VM 設定の即時フラッシュ**: `MainWindowViewModel` の AutoSave は 300ms デバウンスのため、永続層 (`SettingsManager`) スナップショットを取る直前の UI 変更が未反映のことがある。対策として `MainWindowViewModel.Current` (static、ctor で設定) + `FlushPendingAutoSave()` を、(1) `ArchiveProcessor` の Remember パスワード保存直前 (`MutateAndSave` の live 再チェックが古い値を見ないように)、(2) `App.ProcessCommandLineFiles` 冒頭 (シェル/IPC 経由圧縮のスナップショット鮮度) の 2 箇所で呼ぶ。どちらも UI スレッドから呼ぶこと。テストは VM を構築しないので `Current` は null → no-op。
- **Compression scan attributes**: `Settings.IncludeHiddenAndSystemEntries` が圧縮スキャン時の `EnumerationOptions.AttributesToSkip` を制御する。
- **Compression exclusions**: `%LocalAppData%\Lhamiel\.lhaignore` に `.gitignore` 互換構文で記述。`GitignoreMatcher` が compile して `ArchiveCompressor` 側でディレクトリ枝刈り付きマッチを行う。UI 追加・削除・既定値リセット + 「除外設定ファイルを開く」（既定エディタで開く）の 4 操作で管理し、`FileSystemWatcher` が外部編集を検知して UI を再同期する。
- **Nested .gitignore**: `Settings.RespectNestedGitignore` (default **false / オプトイン**) が ON なら、圧縮対象のサブディレクトリ内の `.gitignore` をスキャン前に発見し、各 `.gitignore` をそのスコープで `GitignoreMatcher.CompileLayered` に追加。`.lhaignore` の枝刈り後のディレクトリのみ探索する（node_modules 内の .gitignore は読まない）。除外判定は DFS 枝刈り + `IsExcluded(traversalMode: true)` の「自身レベル照合」で行うため、git と同様に**ディレクトリ否定再包含**（`*.xcodeproj/*` + `!*.xcodeproj/xcshareddata/` で配下の共有ファイルを救い、`d/*`+`!d/sub/` と `d/`+`!d/sub/` の差も区別）を正しく扱う。なお `.gitignore` パターン一致のみで判定するため、git の「追跡済みファイルは ignore に勝つ」例外（`git add -f` した除外パターン該当ファイル）は再現しない。

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test + code coverage on every PR
- **Release (ローカル実行)**: `pwsh scripts/release-local.ps1` — **v1.0.183 から CI リリースを廃止しローカル実行に移行** (コード署名に SimplySign Desktop 接続 + スマホ OTP が必要で GitHub Actions からは署名できないため。velopack-release.yml は削除済み)。スクリプトが publish (Native AOT) → `vpk pack` + **Authenticode 署名** (`--signParams`) → 署名検証 → `wrangler@4.92.0` (pnpm dlx) で Cloudflare R2 バケット `lhamiel-updates` にアップロード → **Cloudflare エッジキャッシュのパージ** (固定名ファイル Setup.exe / Portable.zip / RELEASES / `releases.*.json` / `assets.*.json` は毎リリースで中身が変わるのに URL が不変なため、アップロード直後に Cloudflare API V4 `purge_cache` で該当 URL をパージし旧キャッシュの伝播を断つ。バージョン付き nupkg は URL が一意でパージ不要。1 リクエスト最大 30 URL のため 30 件単位で分割送信。**R2 アップロードは既に成功済みのためパージ失敗で全体は止めず Step 5 と同じ warning-and-continue 方針**（CDN は max-age 経過で自然に新版へ追従するため致命的ではない）) → 配信確認 (`releases.{channel}.json` HTTP 200) → **manifest 外の旧 `*.nupkg` を Cloudflare API V4 で自動削除** (Aggressive 保持戦略: `releases.{channel}.json` に書かれない nupkg は削除、Setup.exe / Portable.zip / RELEASES* / assets.*.json / releases.*.json は固定ファイル名で上書きされる Velopack 内部ファイル & ランディング DL 用なので保護) まで一括実行。**R2 単独配信** (GitHub Releases への継続 publish はしない。旧クライアント救済の踏み台は `/transfer-cf` 移行作業で publish 済み)。Cloudflare トークンは `C:\Users\IMT\dev\Secret\secrets.json` の `cloudflare.api_token` を実行時に読み、**取得直後のプリフライトで zone ID 解決まで行って権限不足を fail fast 検知する**（R2 アップロード後に zone 取得が失敗すると、新ファイルだけ R2 に乗ってパージ・クリーンアップが走らない半端なリリース状態になるため、何もアップロードしていない時点で落とす）。動作確認は `-SkipUpload` (ビルド + 署名のみ)、RID 絞り込みは `-Runtimes win-x64`。**実行前提: SimplySign Desktop がトークンログイン済み** (証明書が CurrentUser\My に見えること。スクリプトがプリフライトで検査して落とす)。**`/vava` は `vava.config.json` の `localRelease` キーを読んでこのスクリプトを自動実行する** (Step 0-8 で署名証明書の前提チェック → Step 10.5 でリリース実行。CI 監視ステップはスキップされる)
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
