# Adopted design decisions and trade-offs

## MVVM without a DI container

Manual wiring keeps Native AOT explicit. Static interface-backed seams on `ArchiveProcessor` provide test substitution: `PasswordDialogImpl` (`IPasswordDialogService`) serves both processor and extractor, using `PasswordDialogMode.Extract` or `CompressNew` (with confirmation). Tests must reset seams carefully.

## Temporary extraction plus final move

Temporary output permits validation and recoverable overwrite at the cost of storage and cleanup. On preparation failure or cancellation, `PrepareExistingTargetsForOverwrite` restores moved originals in reverse order, best effort; unrestorable backups remain for manual recovery.

## Shell command visibility separate from package lifetime

Disabling commands retains the sparse package to avoid pending removal. See [shell commands](commands.md).

## Layered concurrency gates

Process isolation is combined with output hierarchy locks. See [concurrency architecture](architecture.md), [worker lifecycle](workflow.md) and [registry data](data-model.md).

## Caller-owned compression filtering

SevenZip would rescan directories without exclusions, so Lhamiel supplies resolved files and empty-directory markers. Flat mode omits empty directories and their tracking sets. See [compression flow](compression-flow.md).

## Versioned UnLhaRe integration

LHA/LZH uses the pinned NuGet package, without sibling sources or decoder fallback. See [UnLhaRe integration](api-reference.md).

## Format-specific password support

Only ZIP and 7z support passwords. The UI and saved settings disable them for other formats, and `ArchiveProcessor` skips resolution for TAR/LZH. Direct writes with a password still fail explicitly to prevent unencrypted output.

## Acrylic contrast layering

A theme scrim bounds contrast over acrylic. See [UI conventions](conventions.md#acrylic-contrast-layering).

## Settings navigation assets

Tabs and headings share licensed images. See [UI conventions](conventions.md#settings-navigation-assets).

## Diagnostic log retention

Worker logs are separated and pruned by filename date. See [diagnostics](troubleshooting.md#log-retention).

## Shared dialog chrome

Common chrome leaves workflow behavior with each dialog. See [UI conventions](conventions.md#shared-dialog-chrome).

## Shared support SDK with two resolution modes

Sibling references support coordinated development; a fixed NuGet version keeps clones and CI reproducible. Local builds use `packages.local.lock.json` under `obj/`, or `<ArtifactsPath>/obj/<ProjectName>/`. Tracked lockfiles are the package-mode source of truth, based on `win-x64`; ARM64 restores do not change it. Release cleanup in sibling mode validates only the local lockfile, so [package-mode validation](../agents/build-development-commands.md#build--development-commands) is separate.
