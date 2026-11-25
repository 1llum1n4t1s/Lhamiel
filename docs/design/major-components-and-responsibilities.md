# Major components and responsibilities

| Component | Responsibility and boundary |
| --- | --- |
| `Program` / `App` | Startup, single-instance handoff, CLI/IPC dispatch, window lifetime, localization, theme and updates; no archive-format implementation. |
| `View/` | Avalonia UI interaction and service calls; archive and persistence rules remain under `Util/`. |
| `MainWindowViewModel` | UI state, settings projection, drag-and-drop and debounced persistence; delegates archive requests. |
| `ArchiveProcessor` | Orchestrates operations, passwords, capacity, conflict UI, batching and completion. |
| `ArchiveExtractor` / `ArchiveCompressor` | SevenZip/UnLhaRe filesystem operations: safe paths, staging, scanning, format options, progress and cleanup. |
| `LzhArchiveBackendProvider` | Synchronous [UnLhaRe API](api-reference.md) boundary, limits and progress/cancellation adapters. |
| `NativeArchiveGate` | Process-wide slot for each SevenZip reader/writer lifecycle. |
| `ArchiveOperationGate` | Serializes work within each worker; requests bypass the resident queue. |
| `CompressionWorkerLauncher` | One process per compression request and 2–4 extraction workers per multi-archive request, using [token files](workflow.md#worker-requests). |
| `ExtractionDestinationGate` | Cross-process [hierarchy locks](architecture.md) for extraction and compression outputs. |
| `CrossProcessResourceGate` | Session-local output and metadata [mutexes](architecture.md); async leases use a dedicated owner thread. |
| `CompressionOutputRegistry` | [Registers](data-model.md#compression-output-registry) output, staging and backup paths across workers. |
| `Settings` / `SettingsManager` | JSON under `%LocalAppData%\Lhamiel`, recovery, synchronized mutation and [snapshots](configuration.md). |
| `IpcService` | Current-user pipe with two-second connect and write/flush deadlines; rejects null array elements. Tests use unique pipe names. |
| `ShortcutCreator` / `ShellLinkNative` | AOT-compatible links with AppUserModelID; icon refresh updates existing links only. |
| `ShellContextMenu` / `Lhamiel.ShellExtension` | File associations and Explorer commands; enabled state is in `HKCU\Software\Classes\Lhamiel.ContextMenu`. Build 19041+ uses sparse MSIX COM; Windows 11 also uses `IExplorerCommand`. See [shell commands](commands.md). |
| `MessageService` / `MessageDialog` | Modal dispatch, owner resolution and completion ordering. |
| `UpdateChecker` / `App.Check4Update` | Silent and interactive updates via fixed-domain `SimpleWebSource`. |
| `SupportDialog` | Email-verified `Kagayoi.Support.Client` submission for `lhamiel`; no access to others' tickets. |
| `Logger`, `CrashHandler`, `DiagnosticsCollector` | Masked logs, bounded dumps and support bundles; secret-named JSON containers are wholly masked and bundles exclude dumps. |
