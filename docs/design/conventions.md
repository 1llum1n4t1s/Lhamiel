# UI design conventions

## Acrylic contrast layering

Each window layers theme color between the acrylic material and interactive content. This preserves acrylic appearance while bounding contrast variation from bright desktop backgrounds; fallback detection still owns replacement of the acrylic layer when blur is unavailable. `AcrylicFallbackHelper` replaces it with the opaque `Brush.Window` background when the granted level is not `AcrylicBlur`, or on Windows when `GetSystemMetrics(SM_REMOTESESSION)` reports a remote session or HKCU `...\Themes\Personalize\EnableTransparency` is 0. Windows reports `AcrylicBlur` even in those cases; a missing value or a query failure is treated as transparency enabled and not remote. The trade-off is that window roots must preserve the shared acrylic, scrim, and content ordering.

## Shared dialog chrome

`DialogChrome` owns the shared 32-pixel title bar and rounded content surface for application dialogs and progress windows. Body and action controls share the same `Brush.Container` background with no divider or separate action-area color. It reparents existing body and action controls while preserving their events and bindings, so workflow behavior remains with each dialog. Background layers remain owned by the window and follow the acrylic layering above.

`UpdateDialogAppearance` adapts the Velopack SDK window during the awaited update-dialog lifetime, preserving SDK state visibility, button events, and background controls. Required root elements are checked before mutation; a mismatch leaves the SDK's standard screen in place so updating remains available. This avoids duplicating the SDK workflow but couples appearance adaptation to its control structure; implementation checks belong in [AGENTS.md](../agents/architecture.md#testability-pattern).

`PasswordDialog.OnClosed` clears input controls for every close route, including the title bar, Alt+F4, and external cancellation. An accepted password remains available as the result until the caller retrieves it and calls `ClearPassword`, separating control cleanup from result ownership.

## Settings navigation assets

Settings tab headers and their body headings share Microsoft Fluent UI System Icons color PNGs from `Resources/FluentSystemIcons`, embedded as Avalonia resources. SVG sources are kept alongside the displayed PNGs. The icon license is embedded separately as `Lhamiel.FluentSystemIcons.LICENSE` and appended to the application's license text by `MainWindowViewModel.LoadVersionInfo`, so the version screen carries the third-party notice with the application license.
