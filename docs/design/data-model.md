# Persistent and transport data

## Shell selection tokens

Native selections exceeding the Windows command-line limit are passed as a GUID token (`--shell-selection`) referencing a UTF-16LE, NUL-separated file in the current user's temporary directory, bounded to 32 MiB. CLI and IPC carry the same short token; the receiving instance expands and consumes the complete selection once. Compression workers receive the resolved paths through a separate settings-snapshot request token, preserving the entire selection. The receiver accepts GUID tokens and fully qualified paths only, rejects reparse files and malformed payloads, and consumes the file with `DeleteOnClose`. The native sender removes the file if process creation fails. A receiver that never starts or never consumes a forwarded request can leave the temporary file behind; requests are not silently split or partially accepted.

## Compression output registry

The compression output registry stores immutable plan payloads separately from small mutable lease/scanner metadata. Processes reuse decoded payload sets; large batch sets are shared without copying, while small historical sets are combined for bounded per-file membership checks. A caller-owned batch registration covers every supplied item plan through completion, so individual items do not register it again. Version 1 remains readable and writable while its old leases are active; inactive state migrates to version 2 with canonical payloads committed atomically before metadata. A partially written migration payload is rebuilt from the version 1 source. Later scans retain completed archives as ordinary inputs. New-schema state is not silently rewritten by old readers, and malformed or missing payload data fails closed.

## Pending context-menu refresh marker

A context-menu refresh skipped by the Velopack hook leaves `.shell-context-menu-refresh-pending`; the next readable, argument-free primary UI instance reapplies the current setting in the background. It first moves the marker to `.shell-context-menu-refresh-pending.claimed`, applies the setting, and deletes only the claimed file after a successful refresh or an earlier user toggle, so a marker the hook writes during the apply survives for a later startup. A failed, busy, or interrupted run keeps the claimed file, which the next startup treats as pending. A later successful hook or uninstall removes both. Workers, CLI, and silent update never consume it.

## Capacity estimates

Unknown archive sizes use -1, distinct from an empty archive's zero. Compression size aggregation also treats missing, unreadable, or overflowing inputs as unknown: it returns -1 when the known subtotal is below 100 MiB, otherwise preserves that subtotal as a lower bound. Single and merged compression both check nonzero estimates, including -1. Preflight applies the existing 100 MiB free-space floor to unknown sizes; insufficient space also stops operations without an owner window. Runtime monitoring remains active.
