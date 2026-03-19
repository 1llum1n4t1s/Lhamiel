# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Lhamiel is a Windows archive compression/decompression desktop app built with **Avalonia 11** (not WPF) and **.NET 10.0**. The UI language is Japanese.

## Build & Development Commands

```bash
# 7z.dll をダウンロード（初回 or 更新時のみ）
pwsh scripts/download-7z.ps1

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
```

The solution file is `Lhamiel.slnx` (VS 2026 format). Supports **x64** (default) and **ARM64** builds.

### 7z.dll ネイティブライブラリ

7z.dll は 7-Zip 公式サイトからダウンロードして `lib/native/{rid}/` に配置する（`.gitignore` で除外済み）。

- `lib/native/win-x64/7z.dll` — x64 ビルド用
- `lib/native/win-arm64/7z.dll` — ARM64 ビルド用

初回セットアップ: `pwsh scripts/download-7z.ps1`（7zr.exe を自動ダウンロードして展開）

`Directory.Build.targets` が `$(RuntimeIdentifier)` に基づいて適切な 7z.dll をビルド出力にコピーする。

## Architecture

**MVVM with CommunityToolkit.Mvvm** — no DI container; dependencies are wired manually.

### Layers

- **View/** — Avalonia XAML + code-behind (MainWindow, ProgressWindow, OverwriteConfirmDialog, ErrorRecoveryDialog)
- **ViewModels/** — MainWindowViewModel with `[ObservableProperty]` source generators
- **Util/** — All business logic (archive operations, settings, logging, file association, updates)
- **Models/** — Data models

### Core Processing Flow

Drag-and-drop drives the app:
1. `MainWindow.DropZone_Drop` → `MainWindowViewModel.ProcessDroppedPathsAsync`
2. ViewModel delegates to `ArchiveProcessor` which orchestrates extraction/compression
3. `ArchiveExtractor` / `ArchiveCompressor` wrap `1llum1n4t1s.Sevenzip`
4. `ProgressWindow` shows real-time progress via `IProgress<T>`

### Key Util Classes

| Class | Responsibility |
|-------|---------------|
| `ArchiveProcessor` | Orchestrator — decides extract vs compress, manages workflow |
| `ArchiveExtractor` | Extraction logic with smart double-folder prevention |
| `ArchiveCompressor` | Compression with parallel processing support |
| `ArchiveErrorHandler` | Error classification and recovery strategy |
| `PartialExtractionHandler` | Selective extraction (skip corrupted files) |
| `Settings` / `SettingsManager` | JSON config at `%LocalAppData%\Lhamiel\settings.json` |
| `NativeLibraryManager` | 7z.dll lifecycle management |
| `UpdateChecker` | Velopack auto-update integration |

### Static Singletons

`Logger`, `MessageService`, and `SettingsManager` are used as static/singleton utilities throughout the codebase.

## Localization System

**17 languages** supported via Avalonia ResourceDictionary (XAML-based, not .resx).

### How it works

1. Each locale is a `.axaml` file in `Resources/Locales/` (e.g., `ja_JP.axaml`, `en_US.axaml`)
2. Locale files are registered in `App.xaml` as `ResourceInclude` with keys matching locale codes
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
2. Add `ResourceInclude` entry in `App.xaml` with `x:Key="{xx_YY}"`
3. Add to `App.SupportedLocales` array and `App.LocaleDisplayNames` dictionary
4. Add corresponding option in `MainWindowViewModel.LocaleOptions`

### Common pitfalls

- Avalonia's `ResourceInclude` implements `IResourceProvider`, NOT `ResourceDictionary` — type casts must use `IResourceProvider`
- Locale dictionaries are retrieved via `app.Resources[localeKey]` (by the x:Key set in App.xaml)
- Fallback chain: active locale dictionary → Application-wide resources → raw key name

## Key Technical Details

- **Avalonia, not WPF** — uses `AvaloniaResource` items, Actipro theme, compiled bindings (`x:CompileBindings="True"`)
- **Native AOT** enabled (`PublishAot=true`) — avoid reflection-heavy patterns; `TrimmerRoots.xml` preserves the main assembly
- **7z.dll dependency** — native library requires special build handling (see MSBuild targets in `Directory.Build.targets`)
- **Velopack** for auto-updates (`Program.cs` bootstrap)
- **AllowUnsafeBlocks** enabled for P/Invoke (COM interop in `ShortcutCreator`)
- Async/await + CancellationToken throughout all I/O operations
- Version is managed in `Directory.Build.props` (`<Version>` tag), shared across all projects

## CI/CD

- **PR builds**: `.github/workflows/dotnet-build.yml` — restore, build, test on every PR (excludes `release/*`)
- **Release**: `.github/workflows/velopack-release.yml` — triggered by push to `release/*` branches; reads version from `Directory.Build.props`, publishes Native AOT, packages with Velopack, uploads to GitHub Releases

## Testing

Tests are in `Lhamiel.Tests.Unit/` using xUnit 3 + Moq. The test project references the main project directly.

## Documentation

- `ARCHITECTURE.md` — detailed system design and data flows
- `SETTINGS_SCHEMA.md` — complete settings.json reference
- `docs/PARALLEL_IMPLEMENTATION_REPORT.md` — parallel processing research
