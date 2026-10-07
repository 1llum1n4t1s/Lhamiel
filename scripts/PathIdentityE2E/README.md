# Path identity E2E

The final run also holds canonical and original lexical output identities in a common ordinal order. Mixed-version cases exercise both holder directions for same spelling, containing paths, aliases with the same spelling, canonical GUID spelling and unstripped long-path-prefix spelling, plus cancellation and independent siblings. `Legacy*Gate.cs` are the captured pre-R1 working-tree gate sources with only their class names changed. That legacy hierarchy implementation already used cross-process lock files; shipped HEAD's extraction gate was process-local, so the fixture does not imply protection against a shipped old extraction process which never takes these shared locks. Shipped old compression output mutexes retain same-spelling compatibility; cross-alias identity protection requires both workers to use the new canonical contract.

Failure conditions fixed before implementation: a legal selected-root/ancestor junction bypasses the same-output gate or mutex; parent extraction bypasses child compression; rollback displaces another worker's successful archive; independent siblings stop being parallel; a canceled waiter leaves handles held; missing suffix/long-prefix roots yield differing identities; directory identity cannot be resolved but processing continues under a lexical fallback.

Create a fresh isolated directory under `.codex/gogo-rere-parallel-20261007/alias-*`, create `real`, then `New-Item -ItemType Junction -Path <root>/alias -Target <root>/real`. For `after`, create `real/result.zip`, a hardlink `real/hardlink.zip` to that file, and a symbolic link `real/file-symlink.zip` to that file. Symbolic-link creation on this PC required a hidden elevated PowerShell process (`Start-Process pwsh -Verb RunAs -WindowStyle Hidden -Wait`); verify its exit code and the resulting `LinkType` before running.

Run `dotnet run --project scripts/PathIdentityE2E -c Release -- <root> after`. The captured pre-fix gate and registry sources can replay `before` with:

```powershell
dotnet run --project scripts/PathIdentityE2E -c Release -p:GateSourceDir=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/backups/alias -p:RegistrySource=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/backups/registry/CompressionOutputRegistry.cs -- <fresh-root> before
```

Results and dummy files remain under the fixture. The baseline captures displaced successful output as `result.zip.lost-success` instead of deleting it. Gate workers are real child processes; registry operations use the real source with isolated settings, log and temp-tracking services. Native archive payloads and GUI behavior are verified separately by the parent.

`OutputPathIdentity` uses the documented [GetFinalPathNameByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew) directory-handle contract, local volume GUID paths and a DOS/UNC fallback when the volume has no GUID. SMB/UNC runtime and independent external retargeting/renaming of links during a request were not exercised. Cooperative file backup/restore preserves the parent-directory-plus-leaf key; hardlinks/file symlinks keep separate replacement namespace keys.

Only fixture-owned workers are launched; no GUI, real user archives, or external writes. Fixtures (including links), build outputs and cross-process gate lock files remain for parent cleanup through the required recycle workflow.

Original file backups: `C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/backups/alias/{ArchiveCompressor.cs,ExtractionDestinationGate.cs,CrossProcessResourceGate.cs}`. Restore a file with `Copy-Item -LiteralPath <backup-file> -Destination C:/Users/IMT/dev/Lhamiel/src/Lhamiel/Util/<filename>` after coordinating ownership with the parent. New `OutputPathIdentity.cs` and this harness have no preexisting content. The per-run `residual-manifest.json` records the absolute fixture/link and build paths retained for parent cleanup.
