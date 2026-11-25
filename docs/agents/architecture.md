# Architecture

MVVM with CommunityToolkit.Mvvm, manually wired without a DI container. Read [DESIGN.md](../design/major-components-and-responsibilities.md#major-components-and-responsibilities) and its [runtime flows](../design/runtime-data-flows.md#runtime-data-flows) before changing a flow; its [critical invariants](../design/critical-invariants.md#critical-invariants) are mandatory. `ArchiveProcessor` coordinates `ArchiveExtractor` / `ArchiveCompressor`; SevenZip handles other formats and `LzhArchiveBackendProvider` uses UnLhaRe for LHA/LZH. CRC and error-classification rules are in [workflow.md](workflow.md).

- Compression progress/failure handling and extraction output/folder-opening rules are in [workflow.md](workflow.md).
- Apply file association changes only on checkbox/select-all changes or an actual icon variant change, not startup or exit; ignore empty/equal variant changes. Preserve registered extensions and notify Explorer only when applying.
- Compression filtering, empty-directory markers and safety rules are in [DESIGN.md](../design/adopted-design-decisions-and-trade-offs.md#caller-owned-compression-filtering). `ScanSourceFiles` runs once before capacity checking; use the same filtered list for compression. Common `.lhaignore` and optional folder-local rules are reloaded for each scan. Hidden/System inclusion follows `IncludeHiddenAndSystemEntries`.
- Keep the [layered concurrency gates](../design/adopted-design-decisions-and-trade-offs.md#layered-concurrency-gates), including native reader/writer lifetime and cross-process output protection. Do not recursively acquire the native gate. Settings and operation snapshots must preserve pending UI changes and remembered-password updates.

## Testability Pattern

- ArchiveProcessor uses interface-backed static seams (see `ServiceContracts.cs`) for tests; tests replacing them must restore them and run in `[Collection("ArchiveProcessor")]`.
- Dialogs follow [shared dialog chrome](../design/adopted-design-decisions-and-trade-offs.md#shared-dialog-chrome): `DialogBody` holds content, `DialogActions` holds actions (progress uses `CancelButton`), and `DialogChrome.Attach` follows loading. Preserve background fallback and bindings; password controls clear on every close route and the accepted result is cleared after retrieval. Verify SDK update-dialog controls and state when updating the SDK.
- `DiskSpaceChecker` fails closed when space cannot be determined. Do not log `Environment.UserName`; path masking uses the cached USERNAME/profile patterns. `IpcService` uses a current-user-only named pipe with bounded connect and send/flush; tests use a distinct pipe name.
