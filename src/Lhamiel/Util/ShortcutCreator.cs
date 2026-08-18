using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// デスクトップにショートカットを作成するユーティリティクラス。
/// IShellLinkW を P/Invoke で呼び出すため、Native AOT でも動作する。
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShortcutCreator
{
    /// <summary>
    /// デスクトップにショートカットを作成する
    /// </summary>
    /// <returns>作成に成功した場合はtrue、失敗した場合はfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool CreateDesktopShortcut(string? appIconVariant = null)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktopPath))
            {
                Logger.Log("デスクトップパスの取得に失敗しました");
                return false;
            }

            var exePath = AppPathResolver.ExecutablePath;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Log("実行ファイルパスの取得に失敗しました");
                return false;
            }

            var shortcutName = "Lhamiel.lnk";
            var description = "Lhamiel - 圧縮・展開ツール";
            var shortcutPath = Path.Combine(desktopPath, shortcutName);
            return CreateShortcut(exePath, shortcutPath, description, AppIconManager.ResolveIconPath(appIconVariant));
        }
        catch (Exception ex)
        {
            Logger.LogException("デスクトップショートカットの作成に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// 指定されたパスにショートカットを作成する
    /// </summary>
    /// <param name="targetPath">ショートカットのターゲットパス</param>
    /// <param name="shortcutPath">ショートカットファイルの保存パス</param>
    /// <param name="description">ショートカットの説明</param>
    /// <param name="iconPath">ショートカットに表示するアイコンファイル。null の場合は現在の設定から解決する。</param>
    /// <returns>作成に成功した場合はtrue、失敗した場合はfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool CreateShortcut(string targetPath, string shortcutPath, string description, string? iconPath = null)
    {
        try
        {
            if (!File.Exists(targetPath))
            {
                Logger.Log($"ターゲットファイルが存在しません: {targetPath}");
                return false;
            }

            var resolvedIconPath = iconPath ?? AppIconManager.ResolveIconPath();
            var ok = ShellLinkNative.CreateShortcut(
                targetPath,
                shortcutPath,
                description ?? "",
                resolvedIconPath,
                global::Lhamiel.Program.AppUserModelId);
            if (ok)
            {
                Logger.Log($"ショートカットを作成しました: {shortcutPath}");
            }
            else
            {
                Logger.Log("ショートカットファイルの作成に失敗しました");
            }
            return ok;
        }
        catch (Exception ex)
        {
            Logger.LogException($"ショートカットの作成に失敗しました: {shortcutPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Lhamiel の既存ショートカットが見つかった場所だけ、選択中のアイコンへ更新する。
    /// インストールされていない場所へ新しいショートカットは作成しない。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static void RefreshKnownApplicationShortcutIcons(string? variant = null)
    {
        try
        {
            var iconPath = AppIconManager.ResolveIconPath(variant);
            if (!File.Exists(iconPath))
                return;

            var shortcutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKnownShortcut(shortcutPaths, Environment.SpecialFolder.Desktop);
            AddKnownShortcut(shortcutPaths, Environment.SpecialFolder.CommonDesktopDirectory);
            AddKnownShortcut(shortcutPaths, Environment.SpecialFolder.Programs);
            AddKnownShortcut(shortcutPaths, Environment.SpecialFolder.CommonPrograms);

            var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(applicationData))
            {
                shortcutPaths.Add(Path.Combine(
                    applicationData,
                    "Microsoft",
                    "Internet Explorer",
                    "Quick Launch",
                    "User Pinned",
                    "TaskBar",
                    "Lhamiel.lnk"));
            }

            var updated = false;
            foreach (var shortcutPath in shortcutPaths.Where(File.Exists))
            {
                if (ShellLinkNative.UpdateIconLocation(
                    shortcutPath,
                    iconPath,
                    global::Lhamiel.Program.AppUserModelId))
                {
                    Logger.Log($"ショートカットのアイコンを更新しました: {shortcutPath}");
                    updated = true;
                }
                else
                {
                    Logger.Log($"ショートカットのアイコン更新に失敗しました: {shortcutPath}", LogLevel.Warning);
                }
            }

            if (updated)
                FileAssociation.NotifyExplorer();
        }
        catch (Exception ex)
        {
            // 設定変更自体は維持し、権限不足などで更新できなかったショートカットだけを残す。
            Logger.LogException("既存ショートカットのアイコン更新に失敗しました", ex);
        }
    }

    private static void AddKnownShortcut(HashSet<string> shortcutPaths, Environment.SpecialFolder folder)
    {
        var directory = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(directory))
            shortcutPaths.Add(Path.Combine(directory, "Lhamiel.lnk"));
    }

}
