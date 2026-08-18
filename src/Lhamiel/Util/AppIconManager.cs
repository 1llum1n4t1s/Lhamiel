using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Lhamiel.Util;

/// <summary>
/// 選択されたアプリアイコンをウィンドウとショートカットへ反映する。
/// 実行ファイル自体の埋め込みアイコンはビルド時に固定されるため、実行中に変更可能な表示だけを扱う。
/// </summary>
internal static class AppIconManager
{
    internal const string ClassicIconFileName = "app_classic.ico";
    internal const string CrystalIconFileName = "app_crystal.ico";
    internal const string ClassicPreviewResourceUri = "avares://Lhamiel/icon/app_icon_classic.png";
    internal const string CrystalPreviewResourceUri = "avares://Lhamiel/icon/app_icon_crystal.png";

    internal static string GetIconFileName(string? variant) =>
        Settings.NormalizeAppIconVariant(variant) == Settings.AppIconVariantClassic
            ? ClassicIconFileName
            : CrystalIconFileName;

    internal static string GetPreviewResourceUri(string? variant) =>
        Settings.NormalizeAppIconVariant(variant) == Settings.AppIconVariantClassic
            ? ClassicPreviewResourceUri
            : CrystalPreviewResourceUri;

    internal static string ResolveIconPath(string? variant = null)
    {
        var normalized = Settings.NormalizeAppIconVariant(
            variant ?? SettingsManager.Instance.Current.AppIconVariant);
        var baseDirectory = AppContext.BaseDirectory;
        var selectedPath = Path.Combine(baseDirectory, GetIconFileName(normalized));
        if (File.Exists(selectedPath))
            return selectedPath;

        // 古いインストールからの更新直後など、追加バリアントがまだ無い場合は既定アイコンへ戻す。
        return Path.Combine(baseDirectory, "app.ico");
    }

    internal static WindowIcon? CreateWindowIcon(string? variant = null)
    {
        try
        {
            var iconPath = ResolveIconPath(variant);
            if (File.Exists(iconPath))
                return new WindowIcon(iconPath);

            using var stream = AssetLoader.Open(new Uri(GetPreviewResourceUri(variant)));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Logger.LogException("アプリアイコンの読み込みに失敗しました", ex);
            return null;
        }
    }

    internal static void Apply(Window window, string? variant = null)
    {
        var icon = CreateWindowIcon(variant);
        if (icon != null)
            window.Icon = icon;
    }

    internal static void ApplyToOpenWindows(string? variant = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyToOpenWindows(variant));
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
            Apply(window, variant);
    }
}
