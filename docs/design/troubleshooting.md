# Diagnostics and recovery

## Log retention

`Logger` separates worker log files by operation and PID while retaining the common application prefix. Retention uses the date in standard application and extraction/compression worker filenames, including numeric rotation suffixes; it does not interpret the worker PID as a date. Files dated before the local-date cutoff are deleted, while the cutoff day is retained. Emergency logs and unrecognized filename forms are excluded to avoid deleting unrelated diagnostics.

## Transient locks

`LockedFileRetryPolicy` retries transient locks with jittered exponential backoff: `SHARING_VIOLATION` (`0x80070020`), `LOCK_VIOLATION` (`0x80070021`), and other `IOException` / `UnauthorizedAccessException` except permanent HRESULTs (disk full, not found, already exists, CRC/corrupt data, disconnected device or network). Default maximum attempts, including the first, are 6 for synchronous calls (50 ms initial delay) and 3 for asynchronous calls (200 ms).

## Unhandled exceptions

Unobserved task exceptions are logged and marked observed without writing a live-process dump. Self-dumps are limited to terminating unhandled exceptions. Deferred exception logs and dump companion text retain exception types, HRESULTs, and stacks (including inner exceptions), without exception messages whose redaction scopes may have expired.

## Unreadable settings

`Settings.LoadWithStatus` reports `Created` (missing; defaults written), `Loaded`, `CorruptReset` (only `JsonException`; quarantined) or `Unreadable` (exclusive lock, access denied, or attributes cannot be queried). `Load()` returns defaults for `Unreadable` without writing them. Reads retry transient locks 3 times (about 0.1 s total).

An `Unreadable` startup refuses every save until restart, and an `Unreadable` pre-save re-read aborts that save. See [saves after an unreadable load](configuration.md#saves-after-an-unreadable-load).

A legacy `settings.json` beside the executable is adopted by an atomic copy, then deleted best-effort; it is never read while the current file exists. If it cannot be read or copied, the session is `Unreadable` and no default file is created. An `Unreadable` UI session warns once after the main window appears (never CLI, worker, or silent-update runs) and refuses context-menu, file-association, icon-shortcut, and shortcut changes before touching external state. `.lhaignore` initialization and the Velopack hook's context-menu refresh and icon restore are skipped; the icon-restore marker survives for the next readable startup. A skipped context-menu refresh is retried through its [pending marker](data-model.md#pending-context-menu-refresh-marker).
