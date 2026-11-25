# Worker lifecycle and progress

## Worker requests

The receiver consumes shell selection tokens once, flushes pending UI settings, and captures resolved paths and a settings snapshot. A bounded JSON request is passed by GUID token to `--compression-worker` or `--extraction-worker`; the worker consumes it with exclusive access and `DeleteOnClose`. The existing `--compression-request` wire name and DTO remain shared for compatibility. `IsExtraction` chooses the operation; runtime-only `EncryptFileNames` is transported explicitly. Workers skip the main-window mutex, IPC server, and orphan extraction cleanup, show their own progress window, and exit independently. CLI dispatch waits for all workers before shutting down.

## Completion and cancellation

Workers return 0 for success, 10 for a handled logical failure, and 11 for cancellation. Extraction aggregates success/failure/cancellation separately and advances success progress only for successful children; unknown process exits remain startup/runtime errors. Self-terminating CLI parents retain failure status, while UI requests keep the main window open. Group cancellation waits for every started child before propagating cancellation. UI compression carries `ProcessAsBatch` to retain one password prompt and shared cancellation. Multi-archive extraction has a group progress/cancel surface and separate per-archive windows; a named manual-reset event propagates group cancellation, and the parent awaits all started workers. Cancellation closes worker dialogs, including those opened after the signal. Empty launch requests show the main window.

Compression acquires the hierarchy gate for its output file before the existing output mutex, holding both through commit or rollback, so extraction of its parent directory waits while differently named compression outputs remain concurrent. Extraction destinations stay protected from conflict checks through final move and MotW. See [concurrency architecture](architecture.md).

Progress windows carry the accepted target paths independently of progress text. The title includes the source name and parent directory, the body/tooltip and accessible names retain full targets, and group cancellation is labeled separately. Normal-icon handoff obtains the actual connected pipe server PID and grants that process foreground permission before sending the request; process-name enumeration is not used to select the owner.
