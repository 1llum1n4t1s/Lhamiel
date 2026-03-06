# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Lhamiel is a Windows archive compression/decompression desktop app built with **Avalonia 11** (not WPF) and **.NET 10.0**. The UI language is Japanese.

## Build & Development Commands

```bash
# Build
dotnet build Lhamiel.slnx -c Debug
dotnet build Lhamiel.slnx -c Release

# Run tests (xUnit)
dotnet test Lhamiel.slnx -c Release

# Run a single test
dotnet test Lhamiel.slnx --filter "FullyQualifiedName~TestMethodName"

# Publish native AOT executable
dotnet publish -c Release -r win-x64 --self-contained

# Run the app
dotnet run --project Lhamiel.csproj
```

The solution file is `Lhamiel.slnx` (VS 2026 format). Platform target is **x64 only**.

After build, 7z.dll is automatically moved from `runtimes/` to the output root via MSBuild targets in both the main and test `.csproj` files.

## Architecture

**MVVM with CommunityToolkit.Mvvm** — but no DI container; dependencies are wired manually.

### Layers

- **View/** — Avalonia XAML + code-behind (MainWindow, ProgressWindow, OverwriteConfirmDialog, ErrorRecoveryDialog)
- **ViewModels/** — MainWindowViewModel with `[ObservableProperty]` source generators
- **Util/** — All business logic (archive operations, settings, logging, file association, updates)
- **Models/** — Data models

### Core Processing Flow

Drag-and-drop drives the app:
1. `MainWindow.DropZone_Drop` → `MainWindowViewModel.ProcessDroppedPathsAsync`
2. ViewModel delegates to `ArchiveProcessor` which orchestrates extraction/compression
3. `ArchiveExtractor` / `ArchiveCompressor` wrap `Cube.FileSystem.SevenZip` (package: `1llum1n4t1s.Sevenzip`)
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

## Key Technical Details

- **Avalonia, not WPF** — uses `AvaloniaResource` items, Actipro theme, compiled bindings (`x:CompileBindings="True"`)
- **Native AOT** enabled (`PublishAot=true`) — avoid reflection-heavy patterns
- **7z.dll dependency** — native library requires special build handling (see MSBuild targets in `.csproj`)
- **Velopack** for auto-updates (`Program.cs` bootstrap)
- **AllowUnsafeBlocks** enabled for P/Invoke (COM interop in `ShortcutCreator`)
- Async/await + CancellationToken throughout all I/O operations

## Testing

Tests are in `Lhamiel.Tests.Unit/` using xUnit 3 + Moq. The test project references the main project directly.

## Documentation

- `ARCHITECTURE.md` — detailed system design and data flows
- `SETTINGS_SCHEMA.md` — complete settings.json reference
- `docs/PARALLEL_IMPLEMENTATION_REPORT.md` — parallel processing research
