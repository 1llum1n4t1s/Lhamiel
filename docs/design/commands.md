# Shell commands

Before deploying an existing sparse package, `ShellContextMenu` reads the distributed MSIX version and queries the current user's exact package family. The expected external directory includes its trailing separator, matching `EffectiveExternalPath` even for existing DLLs that compare the strings literally. Matching version, effective external path, and a healthy package status allow a visibility-only update without deployment. Missing, stale, or relocated registrations still use the existing deployment path; this does not make first-time registration asynchronous or time-bounded.

## COM and classic verbs

After registration is confirmed, every enabled command's COM class must support `IExecuteCommand` and `IObjectWithSelection` before classic verbs publish `DelegateExecute`; an older DLL retained by a host must not receive a command it cannot execute. The shared native class passes the complete selection; `MultiSelectModel=Player` alone does not combine invocations. Classic verbs use the native canonical GUIDs. Registration and unregistration remove the old `Lhamiel.Extract` / `Lhamiel.Compress` / `Lhamiel.SendTo` names.

Raw commands remain for missing native artifacts and hosts ignoring `DelegateExecute`, which may invoke once per item. All enabled commands are checked. If any returns `E_NOINTERFACE` (`0x80004002`), log the class ID and HRESULT and publish the requested visibility with GUID-based raw verbs for all enabled commands, without `DelegateExecute`; a later setting application rechecks. This does not claim the server was updated. Other activation and deployment failures preserve the previous registration and visibility. Identifier matching is not a documented deduplication guarantee and must be verified in the actual shell UI.

## Selection token handling

The receiver accepts only GUID tokens and fully qualified paths, rejects reparse files and malformed payloads, and consumes the file with `DeleteOnClose`. The native sender removes it if process creation fails; a receiver that never starts or consumes the request can leave it behind. Requests are never split or partially accepted.

## Visibility and removal

Disabling both context-menu commands removes classic verbs and writes zero to the native visibility flags while retaining the sparse MSIX registration. This avoids pending removal while Explorer holds the DLL. Enabling a command registers or updates the package only when the current-registration check above does not match. `0x80073D3C` (`ERROR_BLOCKED_BY_PENDING_PACKAGE_REMOVAL`) rejects deployment rather than reporting a successful deferred update: its failure propagates before classic verbs or visibility flags change, and registration can be retried after logging off and on. Only product uninstall uses `RemoveAll` to remove classic verbs and state, then unregister main packages selected by the exact package family `Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4`.
