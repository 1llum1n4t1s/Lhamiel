# Windows platform boundary

Lhamiel targets Windows only; the overview is in [DESIGN.md](../../DESIGN.md#purpose-and-platform-boundary).

## Taskbar identity

Taskbar identity is shared by processes and shortcuts. `TaskbarIdentity` honors the current process's package AUMID, or resolves the registered sparse package's effective external path for unpackaged shortcut launches of the same `Lhamiel.exe`. Other locations keep `velopack.Lhamiel`. Startup migrates only existing shortcuts targeting that executable with a differing ID, preserving their targets, arguments, and icons. Package names and registration lifetimes stay unchanged.
