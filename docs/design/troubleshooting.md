# Diagnostics and recovery

## Log retention

`Logger` separates worker log files by operation and PID while retaining the common application prefix. Retention uses the date in standard application and extraction/compression worker filenames, including numeric rotation suffixes; it does not interpret the worker PID as a date. Files dated before the local-date cutoff are deleted, while the cutoff day is retained. Emergency logs and unrecognized filename forms are excluded to avoid deleting unrelated diagnostics. Validation belongs in [AGENTS.md](../agents/testing.md#testing).

## Transient locks

`LockedFileRetryPolicy` retries transient locks with jittered exponential backoff: `SHARING_VIOLATION` (`0x80070020`), `LOCK_VIOLATION` (`0x80070021`), and other `IOException` / `UnauthorizedAccessException` except permanent HRESULTs (disk full, not found, already exists, CRC/corrupt data, disconnected device or network). Default maximum attempts, including the first, are 6 for synchronous calls (50 ms initial delay) and 3 for asynchronous calls (200 ms).

## Unhandled exceptions

Unobserved task exceptions are logged and marked observed without writing a live-process dump. Self-dumps are limited to terminating unhandled exceptions. Deferred exception logs and dump companion text retain exception types, HRESULTs, and stacks (including inner exceptions), without exception messages whose redaction scopes may have expired.
