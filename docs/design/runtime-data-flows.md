# Runtime data flows

## Launch and operation dispatch

1. `Program` performs Velopack startup handling and starts Avalonia.
2. Normal launches acquire the single-instance mutex in `App`; later normal processes forward arguments through `IpcService` and exit. Internal compression and extraction workers skip this handoff.
3. UI drag-and-drop enters through `MainWindowViewModel.ProcessDroppedPathsAsync`; CLI, shortcut, and file-association launches enter through `App.ProcessCommandLineFiles`. `--extract` forces extraction (including self-extracting `.exe` archives), `--compress` forces compression, and a route flag without paths opens the normal main window.
4. Every archive entry, including automatic icon/shortcut launches, CLI, shell commands, IPC, and UI drops, dispatches to `CompressionWorkerLauncher` before `ArchiveOperationGate`. Mixed automatic CLI input preserves individual extraction semantics when separate compression is configured; a mixed UI drop keeps its existing compression semantics. Extraction splits the accepted archive list into independent processes, bounded by `IoBoundParallelism` within each request; later requests have independent workers.
5. Workers receive a settings snapshot. See [worker requests and lifecycle](workflow.md).
6. Process-local native gates and cross-process output gates permit safe concurrency. See [concurrency architecture](architecture.md).

## Extraction

Output is fully validated in staging before the final move and MotW propagation. See [extraction safety](security.md).

## Compression

One filtered scan feeds capacity checks and creation; private staging is then committed. See [compression flow](compression-flow.md).

## Settings and secrets

Operations flush pending UI changes and use snapshots; remembered passwords use DPAPI. See [settings and secrets](configuration.md).

## Update application

`--update-check` downloads without the UI mutex, so the UI can start meanwhile. `SetAutoApplyOnStartup(false)` prevents Update.exe from force-terminating install-folder processes at startup. The silent path takes the mutex just before applying and skips if a UI or worker runs; the next silent update or update-screen action applies it, not the next launch. `ApplyUpdatesAndExit` exits immediately, so processes launched during the swap are not blocked and may be terminated.

## Support intake

1. The version tab opens `SupportDialog` with the active locale.
2. The dialog validates the ticket and requests an email verification code through `SupportSubmissionSession`.
3. A verified code and ticket payload are submitted to `support.kagayoi.com`; the returned reference is shown to the user.
4. SDK resolution and lockfile isolation follow the [shared support SDK design](adopted-design-decisions-and-trade-offs.md#shared-support-sdk-with-two-resolution-modes).
