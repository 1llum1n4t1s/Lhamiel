# AGENTS.md

This file provides guidance to coding agents (Claude Code, Codex, etc.) working in this repository.
The content mirrors CLAUDE.md — keep them in sync when updating project-wide conventions.

## Project Overview

Lhamiel is a Windows archive compression/decompression desktop app built with **Avalonia 12** (not WPF) and **.NET 10.0**. The UI language is Japanese.

## Build & Development Commands

```bash
# Build (x64, デフォルト)
dotnet build Lhamiel.slnx -c Debug
dotnet build Lhamiel.slnx -c Release

# Build (ARM64)
dotnet build src/Lhamiel/Lhamiel.csproj -c Debug -p:RuntimeIdentifier=win-arm64 -p:PlatformTarget=ARM64

# Run tests (xUnit)
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

> **Note**: Native AOT ビルド（`dotnet publish -r win-x64`）は VS の C++ ツールチェーン（`vswhere.exe`）が必要。ローカルテストでは `-p:PublishAot=false --self-contained` を使う。

The solution file is `Lhamiel.slnx` (VS 2026 format). **Windows x64 / ARM64** の両方に対応（`1llum1n4t1s.Sevenzip` が両 RID の 7z.dll を同梱）。

### 7z.dll ネイティブライブラリ

7z.dll は `1llum1n4t1s.Sevenzip` NuGet パッケージに同梱される（`runtimes/win-x64/native/7z.dll` および `runtimes/win-arm64/native/7z.dll`）。.NET SDK が `$(RuntimeIdentifier)` に基づいて対応する 7z.dll をビルド出力に自動配置するため、手動でのダウンロード・配置は不要。

## Architecture

**MVVM with CommunityToolkit.Mvvm** — no DI container; dependencies are wired manually.

### Layers

- **View/** — Avalonia XAML + code-behind (MainWindow, ProgressWindow, FileConflictDialog, ErrorRecoveryDialog, DiskSpaceDialog)
- **ViewModels/** — MainWindowViewModel with `[ObservableProperty]` source generators
- **Util/** — All business logic (archive operations, settings, logging, file association, updates)
- **Models/** — Data models

### Core Processing Flow

Drag-and-drop drives the app:
1. `MainWindow.DropZone_Drop` → `MainWindowViewModel.ProcessDroppedPathsAsync`
2. ViewModel delegates to `ArchiveProcessor` which orchestrates extraction/compression
3. `ArchiveExtractor` / `ArchiveCompressor` wrap `1llum1n4t1s.Sevenzip`
4. `ProgressWindow` shows real-time progress via `IProgress<T>`

**展開時の出力先決定** (`ArchiveProcessor`):
- `CreateArchiveNameFolder=ON` + ルートフォルダがアーカイブ名と一致 → フォルダ作成スキップ（`ShouldSkipFolderCreation`）
- `CreateArchiveNameFolder=ON` + それ以外 → `baseDir/アーカイブ名/` フォルダを作成
- `CreateArchiveNameFolder=OFF` → `baseDir` に直接展開
- 複合拡張子（`.tar.gz` 等）は `GetArchiveBaseName()` で正しく処理

**展開後にフォルダを開く** (`OpenExtractionOutputFolder` 設定ON時):
- `CreateArchiveNameFolder=ON`（通常）→ 作成されたアーカイブ名フォルダを開く
- `CreateArchiveNameFolder=ON`（二重ネスト防止スキップ時）→ アーカイブのルートフォルダを開く
- `CreateArchiveNameFolder=OFF` → 展開先の親フォルダを開く
- フォルダ決定ロジックは `FolderOpener.GetExtractionFolderToOpen` に集約。呼び出し側は展開時の `createArchiveNameFolder` 設定値を渡す（展開中の設定変更による不整合を防止）

**圧縮時のロック中ファイル対応**: ソースファイルは元パスのまま `ArchiveWriter.Add()` に渡す（事前の全ファイルコピーは廃止）。ロック中のファイルはライブラリ（`1llum1n4t1s.Sevenzip`）の `UpdateCallback.Open()` が `Save()` 時に自動検出し、`%TEMP%\SevenZip_*` に一時コピーして処理する。一時ディレクトリは `UpdateCallback.Dispose()` で削除される。スキャン後に削除されたファイルは `File.Exists()` チェックでスキップし、残りのファイルで圧縮を続行する。

### Key Util Classes

| Class | Responsibility |
|-------|---------------|
| `ArchiveProcessor` | Orchestrator — decides extract vs compress, manages workflow |
| `ArchiveExtractor` | Extraction with folder creation decision (`ShouldSkipFolderCreation`) and `GetArchiveBaseName` for compound extensions |
| `ArchiveCompressor` | Compression — locked file handling is delegated to library side |
| `ArchiveErrorHandler` | Error classification and recovery strategy |
| `PartialExtractionHandler` | Selective extraction (skip corrupted files) |
| `Settings` / `SettingsManager` | JSON config at `%LocalAppData%\Lhamiel\settings.json` |
| `NativeLibraryManager` | 7z.dll lifecycle management |
| `UpdateChecker` | Velopack auto-update integration |
| `FileIconHelper` | Windows Shell API (SHGetFileInfo) でファイルアイコン取得（P/Invoke） |
| `FolderOpener` | 展開/圧縮完了後にエクスプローラーでフォルダを開く。`GetExtractionFolderToOpen` で開くべきフォルダを決定（二重ネスト防止スキップ時のパス補正含む） |

### File Conflict Resolution System

ファイル衝突解決は `FileConflictDialog` に統合されている（旧 `OverwriteConfirmDialog` は削除済み）。

- **展開時**: アーカイブ内ファイルと既存ファイルを個別比較。2ペイン構成（現在の場所 vs 宛先の場所）
- **圧縮時**: 同名ファイルが複数フォルダから来た場合、縦1列リストでグループ表示。グループヘッダーのチェックで一括選択
- **スキップ機能**: 「日付とサイズが同じファイルをスキップ」チェックボックスで同一ファイルをフィルタリング

モデル: `FileConflictEntry` / `FileConflictGroup` / `FileConflictResult`（`Models/FileConflictInfo.cs`）
ビューモデル: `ConflictRowViewModel` / `ConflictCellViewModel`（`FileConflictDialog.axaml.cs` 内に定義）

### Static Singletons

`Logger`, `MessageService`, and `SettingsManager` are used as static/singleton utilities throughout the codebase.

## Localization System

**17 languages** supported via Avalonia ResourceDictionary (XAML-based, not .resx).

### How it works

1. Each locale is a `.axaml` file in `Resources/Locales/` (e.g., `ja_JP.axaml`, `en_US.axaml`)
2. Locale files are registered in `App.axaml` as `ResourceInclude` with keys matching locale codes
3. `App.SetLocale(localeKey)` swaps the active locale dictionary in `MergedDictionaries`
4. `App.Text(key, ...args)` retrieves localized strings — note it auto-prepends `"Text."` to the key

### Key patterns

```csharp
// In C# code (Util layer, ViewModels)
var msg = App.Text("Error.DuringExtraction", ex.Message);  // looks up "Text.Error.DuringExtraction"

// In XAML — use DynamicResource (not StaticResource) for runtime locale switching
<TextBlock Text="{DynamicResource Text.DropZone.Message}" />
```

### Adding a new locale

1. Create `Resources/Locales/{xx_YY}.axaml` with all `Text.*` keys
2. Add `ResourceInclude` entry in `App.axaml` with `x:Key="{xx_YY}"`
3. Add to `App.SupportedLocales` array and `App.LocaleDisplayNames` dictionary
4. Add corresponding option in `MainWindowViewModel.LocaleOptions`

### Common pitfalls

- Avalonia's `ResourceInclude` implements `IResourceProvider`, NOT `ResourceDictionary` — type casts must use `IResourceProvider`
- Locale dictionaries are retrieved via `app.Resources[localeKey]` (by the x:Key set in App.axaml)
- Fallback chain: active locale dictionary → Application-wide resources → raw key name

## Key Technical Details

- **Avalonia 12, not WPF** — uses `AvaloniaResource` items, FluentTheme, compiled bindings (`x:CompileBindings="True"`). Avalonia 12 で `ExtendClientAreaChromeHints` は削除済み（`WindowDecorations` に統合）
- **Native AOT** enabled (`PublishAot=true`) — avoid reflection-heavy patterns; `TrimmerRoots.xml` preserves the main assembly
- **7z.dll dependency** — `1llum1n4t1s.Sevenzip` NuGet パッケージが `runtimes/win-{x64,arm64}/native/7z.dll` を同梱しており、.NET SDK が RID に基づいて自動配置する。`NativeLibraryManager` が起動時に `LoadLibrary` でプロセスに固定し、AOT ライブラリファイナライザによるアクセス違反を防止
- **Logger** — `SuperLightLogger` の内蔵 File Target を使用。`Logger.Initialize(LoggerConfig)` で `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log` にローリング出力
- **Velopack** for auto-updates (`Program.cs` bootstrap)
- **AllowUnsafeBlocks** enabled for P/Invoke (COM interop in `ShortcutCreator`, `FileIconHelper`)
- **Acrylic blur** — 全ダイアログで `ExperimentalAcrylicBorder` + `ExtendClientAreaToDecorationsHint` を使用。タイトルバー境界線を見えなくする
- Async/await + CancellationToken throughout all I/O operations
- Version is managed in `Directory.Build.props` (`<Version>` tag), shared across all projects

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test on every PR (excludes `release/*`)
- **Release**: `.github/workflows/velopack-release.yml` — triggered by push to `release/*` branches; reads version from `Directory.Build.props`, publishes Native AOT, packages with Velopack, uploads to GitHub Releases

## Testing

Tests are in `Lhamiel.Tests.Unit/` using xUnit 3 + Moq. The test project references the main project directly.

- 通常テスト: `*Tests.cs`
- 嫌がらせテスト (adversarial): `*AdversarialTests.cs` — 境界値、異常入力、状態遷移の矛盾などを検証

## Documentation

- `ARCHITECTURE.md` — detailed system design and data flows
- `SETTINGS_SCHEMA.md` — complete settings.json reference
- `docs/PARALLEL_IMPLEMENTATION_REPORT.md` — parallel processing research
