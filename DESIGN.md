# Lhamiel Design

This document is the source of truth for the current system structure, responsibility boundaries, data flows, invariants, and adopted design decisions. Operational commands and agent-specific implementation rules belong in [AGENTS.md](AGENTS.md).

## Purpose and platform boundary

Lhamiel is a Japanese-language desktop application for compressing and extracting archives on Windows. It targets .NET 10 and Avalonia 12, publishes Native AOT binaries for x64 and ARM64, and intentionally depends on Windows facilities such as named pipes, Explorer shell integration, COM, registry associations, DPAPI, Authenticode, and `LoadLibraryW`. Portability to Linux or macOS is not an active system boundary.

## Major components and responsibilities

| Component | Responsibility and boundary |
| --- | --- |
| `Program` / `App` | Process startup, Velopack bootstrap, single-instance handoff, CLI/IPC dispatch, window lifetime, localization, theme, and update entry points. They choose a workflow but do not implement archive formats. |
| `View/` | Avalonia windows and dialogs. Code-behind is limited to UI interaction and service calls; archive and persistence rules remain under `Util/`. |
| `MainWindowViewModel` | UI state, settings projection, drag-and-drop command entry, and debounced persistence. It delegates archive work to `ArchiveProcessor`. |
| `ArchiveProcessor` | Application-level orchestration for extraction, compression, password acquisition, disk-space checks, conflict UI, batching, and completion behavior. |
| `ArchiveExtractor` / `ArchiveCompressor` | Filesystem-facing archive operations around `1llum1n4t1s.Sevenzip`. They own safe path resolution, temporary outputs, scanning, format options, progress adaptation, and cleanup. |
| `NativeArchiveGate` | The single process-wide slot around each native reader/writer lifecycle. It isolates the non-concurrent shared SevenZip library state. |
| `ArchiveOperationGate` | Serializes top-level operations started by drag-and-drop, CLI, or IPC while preserving safe parallelism inside a batch. |
| `ExtractionDestinationGate` | Serializes operations that converge on the same final extraction path while allowing unrelated destinations to proceed independently. |
| `Settings` / `SettingsManager` | JSON persistence under `%LocalAppData%\Lhamiel`, recovery from damaged settings, synchronized mutation, and immutable snapshots for long-running operations. |
| `IpcService` | Current-user-only named-pipe transport used to forward later launches to the resident instance. |
| `ShortcutCreator` / `ShellLinkNative` | Native-AOT-compatible `.lnk` creation for automatic, extraction, and compression routes. Links carry the process AppUserModelID; icon refresh updates known links in place without creating missing shortcuts. |
| `ShellContextMenu` / `Lhamiel.ShellExtension` | Windows file associations and Explorer integration. Extraction and compression are independent commands; the native extension reads their enabled state from `HKCU\Software\Classes\Lhamiel.ContextMenu`. Windows 11 uses signed `IExplorerCommand` handlers plus sparse MSIX; older or development environments fall back to classic registry verbs. |
| `MessageService` / `MessageDialog` | UI-thread dispatch, active-owner resolution, and the shared Lhamiel modal message surface. It also orders transient-window closure and dialog completion for self-terminating workflows. |
| `UpdateChecker` / `App.Check4Update` | Silent login-time and interactive UI update paths. Both consume Velopack manifests from the fixed R2 custom domain through `SimpleWebSource`. |
| `SupportDialog` | Email-code-verified support submission through `Kagayoi.Support.Client` using product ID `lhamiel`; it does not expose other users' tickets. |
| `Logger`, `CrashHandler`, `DiagnosticsCollector` | Masked diagnostics, bounded crash dumps, and support bundles. Passwords and sensitive settings must not cross this boundary in plaintext. |

## Runtime data flows

### Launch and operation dispatch

1. `Program` performs Velopack startup handling and starts Avalonia.
2. `App` acquires the single-instance mutex. Later processes forward arguments through `IpcService` and exit.
3. UI drag-and-drop enters through `MainWindowViewModel.ProcessDroppedPathsAsync`; CLI, shortcut, and file-association launches enter through `App.ProcessCommandLineFiles`. `--extract` forces extraction (including self-extracting `.exe` archives), `--compress` forces compression, and a route flag without paths opens the normal main window.
4. `ArchiveOperationGate` queues the top-level request, then `ArchiveProcessor` selects extraction or compression and creates immutable settings snapshots.

### Extraction

1. `ArchiveProcessor` inspects archive structure, resolves the final destination, obtains passwords when required, and acquires the destination gate. Reader-open sharing violations are retried; if structure remains unknown, only `.7z`/`.rar` enter password recovery and other formats fail closed before extraction.
2. `ArchiveExtractor` creates a temporary directory on the destination volume when possible.
3. Every native `ArchiveReader.Save` path validates all `item.FullName` values through `ValidateArchiveEntryPaths` before writing. Boundary traversal, Windows device aliases, and ambiguous trailing characters are rejected.
4. Native reader creation, use, and disposal run inside `NativeArchiveGate`.
5. Extracted content is moved from the temporary directory to the final destination with backup/restore semantics for existing targets. Reparse points are not followed by tree validation, attribute cleanup, MotW propagation, or temporary cleanup. Post-processing resolves normalized Unicode paths first and may use the raw archive representation only after repeating the same destination-boundary validation.
6. MotW is propagated after the final output is stable. Folder opening and process shutdown occur only after the configured shell action completes on self-terminating launch paths.

### Compression

1. `ArchiveCompressor.ScanSourceFiles` builds the complete input list before capacity checks and archive creation.
2. Global `.lhaignore`, optional source-local ignore files, Hidden/System settings, and reparse-point pruning are applied during the explicit DFS scan.
3. Individual files and synthetic empty-directory markers are passed to the writer; real directories are not passed for recursive library traversal because that would bypass the filter contract.
4. Native writer work is serialized by `NativeArchiveGate`. Progress adapts scanning, preparation, byte processing, and finalization into distinct user-visible phases.
5. Inaccessible files may be skipped according to the documented resilience contract; password-protected partial skips are surfaced because skipped plaintext sources remain outside the archive.

### Settings and secrets

1. The view model projects settings to the UI and saves through a short debounce.
2. Operation entry points flush pending UI changes and create a snapshot so an operation does not observe mid-flight setting changes.
3. Remembered compression passwords are DPAPI-protected for the current Windows user. Plaintext passwords exist only in short-lived scopes, are registered with log redaction, and have best-effort memory clearing.
4. The update base URL is a getter-only, non-serialized constant so settings files cannot redirect update traffic to another host.
5. The legacy `AddToContextMenu` value fills only missing `AddExtractToContextMenu` and `AddCompressToContextMenu` keys, preserving explicit new values, and the migrated shape is persisted after load.

### Support intake

1. The version tab opens `SupportDialog` with the active locale.
2. The dialog validates the ticket and requests an email verification code through `SupportSubmissionSession`.
3. A verified code and ticket payload are submitted to `support.kagayoi.com`; the returned reference is shown to the user.
4. The SDK resolves as a sibling `ProjectReference` during local multi-repository development and as the fixed `Kagayoi.Support.Client` package for standalone clones and CI.

### Update and release

1. Installed clients obtain `releases.win.json` or `releases.win-arm64.json` from `https://lhamiel.kagayoi.com`.
2. `scripts/release-local.ps1` builds both Native AOT RIDs in isolated artifact trees, builds the native shell integration, packages with Velopack, and Authenticode-signs all distributed executables through SimplySign/Certum. Per-RID isolation also applies to project references so x64 intermediates cannot be reused by ARM64 publishing.
3. The same script uploads immutable versioned packages and fixed-name manifests/installers to R2. It downloads non-`.nupkg` artifacts with a cache-busting query and compares their size and SHA256 with local outputs, purges only mismatching URLs, and rechecks them. Download, purge, or recheck failure stops release completion even after upload. After public-manifest checks, cleanup preserves manifest-referenced files, fixed names, and the latest two versions of versioned artifacts.
4. The landing-page Worker under `web/` has an independent main-branch deployment workflow and is not part of the desktop binary release path.

## Critical invariants

- A native archive reader or writer is never used outside `NativeArchiveGate`, and the gate is never acquired recursively.
- Every filesystem-writing `ArchiveReader.Save` call is preceded by complete `FullName` validation. Archive `RawName` is diagnostic input, not an output path.
- Reparse points are treated as boundaries: archive scanning does not descend into them, conflict-aware extraction tree enumeration rejects them, and cleanup removes links without enumerating their targets.
- Top-level operations are serialized, but batch-level pure I/O may run concurrently when destinations differ and no native archive object is active.
- Existing outputs use backup/restore semantics so preparation or move failure does not silently discard the original.
- Capacity checks use the Windows volume API and fail closed when the target volume or available space cannot be resolved; an inspection failure is never treated as unlimited free space.
- Compression filters are enforced by Lhamiel's resolved file list; the SevenZip writer is not trusted to reapply `.lhaignore` or attribute filters.
- Long-running operations use settings snapshots. UI debounce state is flushed before creating snapshots for CLI/IPC and remembered-password paths.
- Error workflows close any transient progress window before opening a message dialog, await that dialog, and only then permit self-terminating CLI or shell launches to shut down.
- Logs, support bundles, and user-visible error details must not contain plaintext passwords, encrypted password blobs, tokens, or user-identifying path segments.
- Update manifests and packages come from the fixed Kagayoi R2 domain. Runtime settings cannot select another update host.
- Released executables, installers, portable packages, shell-extension DLLs, and MSIX packages are Authenticode signed and timestamped.
- Localization uses dynamic Avalonia resources so changing locale updates existing views without restarting.

## Adopted design decisions and trade-offs

### MVVM without a DI container

Dependencies are wired manually to keep Native AOT behavior explicit and avoid reflection-heavy startup. Static interface-backed seams on `ArchiveProcessor` provide test substitution. The trade-off is centralized composition and careful reset discipline in tests.

### Temporary extraction plus final move

Archive data is written to a temporary directory before replacing final outputs. This enables complete pre-move validation and recoverable overwrite behavior, at the cost of extra temporary storage and move/cleanup logic. `PrepareExistingTargetsForOverwrite` restores already-moved originals on preparation failure or cancellation before propagating the error. Restoration is best effort in reverse order; backups that cannot be restored remain available for manual recovery.

### Shell command visibility separate from package lifetime

Before deploying an existing sparse package, `ShellContextMenu` reads the distributed MSIX version and queries the current user's exact package family. Matching version, effective external path, and a healthy package status allow a visibility-only update without deployment. Missing, stale, or relocated registrations still use the existing deployment path; this does not make first-time registration asynchronous or time-bounded.

Native selections exceeding the Windows command-line limit are passed as a GUID token (`--shell-selection`) referencing a UTF-16LE, NUL-separated file in the current user's temporary directory, bounded to 32 MiB. CLI and IPC carry the same short token; only the receiving instance expands the complete selection, preserving batch compression. The receiver accepts GUID tokens and fully qualified paths only, rejects reparse files and malformed payloads, and consumes the file with `DeleteOnClose`. The native sender removes the file if process creation fails. A receiver that never starts or never consumes a forwarded request can leave the temporary file behind; requests are not silently split or partially accepted.

Disabling both context-menu commands removes classic verbs and writes zero to the native visibility flags while retaining the sparse MSIX registration. This avoids pending removal while Explorer holds the DLL. Enabling a command registers or updates the package only when the current-registration check above does not match; `0x80073D3C` is treated as a deferred update with the existing registration still usable, so visibility flags can still change. Only product uninstall uses `RemoveAll` to remove classic verbs and state, then unregister main packages selected by the exact package family `Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4`.

### Layered concurrency gates

The application does not disable batch concurrency globally. A top-level operation gate, a native-library gate, and destination-specific gates protect different resources. This preserves throughput for independent pure-I/O work but requires a fixed non-reentrant acquisition order.

### Caller-owned compression filtering

Lhamiel enumerates and filters all archive inputs itself because the SevenZip wrapper recursively rescans real directories without the application's exclusion rules. Synthetic empty-directory markers preserve empty folders without reopening that recursive path.

### Acrylic contrast layering

Each window layers theme color between the acrylic material and interactive content. This preserves acrylic appearance while bounding contrast variation from bright desktop backgrounds; fallback detection still owns replacement of the acrylic layer when blur is unavailable. The trade-off is that window roots must preserve the shared acrylic, scrim, and content ordering.

### Shared dialog chrome

`DialogChrome` owns the shared 32-pixel title bar, rounded content surface, and separated action bar for application dialogs and progress windows. It reparents existing body and action controls while preserving their events and bindings, so workflow behavior remains with each dialog. Background layers remain owned by the window and follow the acrylic layering above.

`UpdateDialogAppearance` adapts the Velopack SDK window during the awaited update-dialog lifetime, preserving SDK state visibility, button events, and background controls. Required root elements are checked before mutation; a mismatch leaves the SDK's standard screen in place so updating remains available. This avoids duplicating the SDK workflow but couples appearance adaptation to its control structure; implementation checks belong in [AGENTS.md](AGENTS.md#testability-pattern).

`PasswordDialog.OnClosed` clears input controls for every close route, including the title bar, Alt+F4, and external cancellation. An accepted password remains available as the result until the caller retrieves it and calls `ClearPassword`, separating control cleanup from result ownership.

### Local signed releases to R2

SimplySign requires a logged-in local session and device approval, so binary releases are produced locally rather than in CI. R2 is the continuing update source; old GitHub Releases are retained only as a migration bridge for legacy clients. This provides signed dual-architecture releases but makes the release workstation and signing session part of the operational boundary.

### Shared support SDK with two resolution modes

Sibling project references give fast coordinated local development, while a fixed public NuGet.org version keeps standalone clones and CI reproducible without repository-specific package credentials. Local sibling builds write `obj/packages.local.lock.json`; tracked `packages.lock.json` files therefore remain the package-mode source of truth used by standalone clones and CI.
