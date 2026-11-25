# Critical invariants

- Taskbar identity is shared by processes and shortcuts without changing package names or lifetimes. See [taskbar identity](purpose-and-platform-boundary.md#taskbar-identity).

- A SevenZip native archive reader or writer is never used outside `NativeArchiveGate`, and the gate is never acquired recursively. UnLhaRe owns per-call state and does not need the SevenZip singleton gate for payload processing.
- Every filesystem-writing `ArchiveReader.Save` call is preceded by complete `FullName` validation. Archive `RawName` is diagnostic input, not an output path.
- Reparse points are boundaries: do not traverse links below selected roots; validate staging before backup or publication. See [reparse boundaries](security.md#reparse-boundaries).
- Compression and extraction requests from every entry run in independent workers. Multi-archive extraction uses separate native-library instances; identical or nested extraction outputs and identical compression outputs are protected across processes.
- Existing outputs use backup/restore semantics so preparation or move failure does not silently discard the original.
- `LockedFileRetryPolicy` retries only transient failures, within [bounded attempts](troubleshooting.md#transient-locks).
- Capacity checks use the Windows volume API and fail closed when the target volume or available space cannot be resolved; an inspection failure is never treated as unlimited free space.
- Unknown archive sizes use -1, distinct from empty; nonzero estimates including -1 require a 100 MiB preflight floor. See [capacity estimates](data-model.md#capacity-estimates).
- Compression filters are enforced by Lhamiel's resolved file list; the SevenZip writer is not trusted to reapply `.lhaignore` or attribute filters.
- Long-running operations use settings snapshots. UI debounce state is flushed before creating snapshots for CLI/IPC and remembered-password paths.
- Error workflows close any transient progress window before opening a message dialog, await that dialog, and only then permit self-terminating CLI or shell launches to shut down.
- Logs, support bundles, and user-visible error details must not contain plaintext passwords, encrypted password blobs, tokens, or user-identifying path segments.
- Unobserved exceptions never create live-process dumps, and deferred logs omit exception messages. See [unhandled exceptions](troubleshooting.md#unhandled-exceptions).
- Update manifests and packages come from the fixed Kagayoi R2 domain. Runtime settings cannot select another update host.
- Released executables, installers, portable packages, shell-extension DLLs, and MSIX packages are Authenticode signed and timestamped.
- Localization uses dynamic Avalonia resources so changing locale updates existing views without restarting.
