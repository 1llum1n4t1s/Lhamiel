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

**圧縮時のロック中ファイル対応**: ソースファイルは元パスのまま `ArchiveWriter.Add()` に渡す。ロック中のファイルはライブラリ（`1llum1n4t1s.Sevenzip`）の `UpdateCallback` が自動的に一時コピーして処理する。スキャン後に削除されたファイルは `File.Exists()` チェックでスキップし、残りのファイルで圧縮を続行する。

**圧縮時のファイル列挙・除外設定**:
- `ArchiveCompressor.ScanSourceFiles` は `Settings` スナップショットを元に対象一覧を構築する。
- `IncludeHiddenAndSystemEntries=true`（デフォルト）では `EnumerationOptions.AttributesToSkip = 0` とし、Hidden/System 属性のファイル・フォルダも含める（例: `.git`）。
- `IncludeHiddenAndSystemEntries=false` では Hidden/System 属性をスキップする。
- `ExcludedFilePatterns` は圧縮時の除外リスト。glob ではなくパスセグメント完全一致で判定する（例: `.DS_Store`, `Thumbs.db`, `.git`, `node_modules`, `__MACOSX`）。
- 除外リストは圧縮設定 UI で追加・削除・既定値リセット可能。保存前に trim / 空文字除外 / case-insensitive 重複排除を行う。

### Key Util Classes

| Class | Responsibility |
|-------|---------------|
| `ArchiveProcessor` | Orchestrator — decides extract vs compress, manages workflow |
| `ArchiveExtractor` | Extraction with `ShouldSkipFolderCreation`, `TryExtractEntryAsync` (retry with exponential backoff) |
| `ArchiveCompressor` | Compression with Unicode NFC normalization, Hidden/System enumeration control, and exclusion pattern filtering |
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
| `UpdateChecker` | Velopack 自動更新の **`--update-check` サイレント CLI 経路** (Program.cs から `StartupRegistration` の HKCU\Run 登録経由で発火、UI 不要なバックグラウンド自動更新)。配信元は `Settings.UpdateBaseUrl` (= Cloudflare R2 カスタムドメイン) を **`Velopack.Sources.SimpleWebSource`** で取得 (v1.0.168 で `GithubSource` から切替) |
| `App.Check4Update` | Velopack 自動更新の **UI 経路**。`Settings.Check4UpdatesOnStartup=true` のときメイン画面起動直後 + 「アップデート確認」ボタンから手動起動。`VelopackUpdateDialog.UpdateDialogWindow` をオーナー付きで表示し、Velopack 0.0.1369-g1d5c984 と組み合わせて 30 秒タイムアウトで動作。`SimpleWebSource` 経由 R2 取得 |
| `App.UpdateCheckStateChanged` 静的イベント | `_isCheckingUpdate` フラグ遷移を `TryBeginUpdateCheck` / `EndUpdateCheck` ヘルパーで発火。`MainWindowViewModel.IsCheckingUpdate` を駆動し、起動時自動チェック中も「アップデート確認」ボタンが自動 disabled (並走実行防止) |
| `LhamielUpdateStrings` | `VelopackUpdateDialog.IUpdateDialogStrings` の Lhamiel 実装 (Models/)。`Text.SelfUpdate.*` / `Text.Close` を `App.Text()` 経由で動的解決 (シングルトン、`NotifyLocaleChanged()` でロケール切替即時反映) |
| `IpcService` | Single-instance enforcement via Named Pipe (`PipeOptions.CurrentUserOnly`) |
| `FolderOpener` | Opens explorer to extraction/compression output folder |

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
- **Logger** — `SuperLightLogger` File Target, `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log`
- **Velopack** 自動更新 — 配信元は **Cloudflare R2 単独** (`https://lhamiel.1llum1n4t1.com`、`SimpleWebSource` 経由)。通常リリース (`/vava`) は R2 のみに配信する。旧 `GithubSource` クライアント (v1.0.167 以下) 救済のため、**GitHub Releases には R2 対応版 (v1.0.169) を「踏み台」として 1 つだけ publish 済み** (削除せず永続保持。旧クライアントはこれ経由で R2 へ 2 段階移行する)。継続的な GitHub Releases 併用配信はしない。2 系統: (1) `Program.cs --update-check` サイレント CLI 経路 (Windows ログイン時 `StartupRegistration` から発火、UI 無し)、(2) `App.Check4Update` UI 経路 (`VelopackUpdateDialog.Avalonia` 1.0.3 経由のダイアログ表示、`Settings.Check4UpdatesOnStartup=true` で起動時自動 + メニューから手動)
- **AllowUnsafeBlocks** for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`, `CrashHandler`)
- **Acrylic blur** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint`
- Async/await + CancellationToken throughout all I/O operations
- Version: `Directory.Build.props` の `<Version>` タグで全プロジェクト共有
- **Unicode NFC normalization**: macOS HFS+ の NFD ファイル名を展開・圧縮時に NFC 正規化（`Settings.NormalizeUnicodeFileNames`）
- **Long path support**: `app.manifest` で `longPathAware` + `PathValidator.EnsureLongPathPrefix`
- **Mark of the Web**: 元アーカイブの Zone.Identifier ADS を展開ファイルに伝播（`Settings.PropagateMarkOfTheWeb`）
- **Compression scan attributes**: `Settings.IncludeHiddenAndSystemEntries` が圧縮スキャン時の `EnumerationOptions.AttributesToSkip` を制御する。
- **Compression exclusions**: `Settings.ExcludedFilePatterns` は glob ではなくパスセグメント完全一致。UI から管理でき、保存時に正規化される。

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test + code coverage on every PR
- **Release**: `.github/workflows/velopack-release.yml` — `release/*` ブランチへの push でトリガー。`vpk pack` で win + win-arm64 を並列ビルド後、`r2-upload` job が `wrangler@4.x` (Node.js 22) で Cloudflare R2 バケット `lhamiel-updates` にアップロード + `curl --fail` で配信確認 (`releases.{channel}.json` HTTP 200 検証)。**R2 単独配信** (GitHub Releases への継続 publish はしない。旧クライアント救済の踏み台は `/transfer-cf` 移行作業で publish 済み)。必要 Secrets: `CLOUDFLARE_API_TOKEN` / `CLOUDFLARE_ACCOUNT_ID`
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
