using System.Runtime.Versioning;
namespace Lhamiel.Util;

internal enum DesktopShortcutKind
{
    Integrated,
    Extract,
    Compress,
}

/// <summary>
/// デスクトップにショートカットを作成するユーティリティクラス。
/// IShellLinkW を P/Invoke で呼び出すため、Native AOT でも動作する。
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShortcutCreator
{
    /// <summary>従来どおり自動判定する統合ショートカットをデスクトップに作成する。</summary>
    /// <param name="appIconVariant">ショートカットに使うアプリアイコンのバリアント。</param>
    /// <returns>作成に成功した場合は true。</returns>
    [SupportedOSPlatform("windows")]
    public static bool CreateDesktopShortcut(string? appIconVariant = null) =>
        CreateDesktopShortcut(DesktopShortcutKind.Integrated, appIconVariant);

    /// <summary>
    /// デスクトップに用途別ショートカットを作成する。
    /// </summary>
    /// <param name="kind">統合／展開／圧縮のショートカット種別。</param>
    /// <param name="appIconVariant">ショートカットに使うアプリアイコンのバリアント。</param>
    /// <returns>作成に成功した場合はtrue、失敗した場合はfalse</returns>
    [SupportedOSPlatform("windows")]
    internal static bool CreateDesktopShortcut(DesktopShortcutKind kind, string? appIconVariant = null)
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

            var definition = GetDesktopShortcutDefinition(kind);
            var shortcutPath = Path.Combine(desktopPath, definition.FileName);
            return CreateShortcut(
                exePath,
                shortcutPath,
                definition.Description,
                AppIconManager.ResolveIconPath(appIconVariant),
                definition.Arguments);
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
    /// <param name="arguments">リンク先へ固定で渡すコマンドライン引数。</param>
    /// <returns>作成に成功した場合はtrue、失敗した場合はfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool CreateShortcut(
        string targetPath,
        string shortcutPath,
        string description,
        string? iconPath = null,
        string? arguments = null)
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
                global::Lhamiel.Program.AppUserModelId,
                arguments);
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
        => RefreshKnownApplicationShortcuts(variant, identityOnly: false);

    internal static void RefreshKnownApplicationShortcutIdentity()
        => RefreshKnownApplicationShortcuts(null, identityOnly: true);

    private static void RefreshKnownApplicationShortcuts(string? variant, bool identityOnly)
    {
        try
        {
            var iconPath = AppIconManager.ResolveIconPath(variant);
            if (!identityOnly && !File.Exists(iconPath))
                return;

            var shortcutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKnownShortcuts(shortcutPaths, Environment.SpecialFolder.Desktop);
            AddKnownShortcuts(shortcutPaths, Environment.SpecialFolder.CommonDesktopDirectory);
            AddKnownShortcuts(shortcutPaths, Environment.SpecialFolder.Programs);
            AddKnownShortcuts(shortcutPaths, Environment.SpecialFolder.CommonPrograms);
            // Velopack が作成する製品名サブフォルダーのリンクも移行対象。
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
            {
                var directory = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(directory))
                    shortcutPaths.Add(Path.Combine(directory, "Lhamiel", "Lhamiel.lnk"));
            }

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
                if (identityOnly)
                {
                    updated |= MigrateShortcutIdentity(shortcutPath, AppPathResolver.ExecutablePath, Program.AppUserModelId);
                    continue;
                }
                if (!ShortcutTargetsExecutable(shortcutPath, AppPathResolver.ExecutablePath))
                    continue;
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

    internal static (string FileName, string Description, string? Arguments) GetDesktopShortcutDefinition(
        DesktopShortcutKind kind)
    {
        return kind switch
        {
            DesktopShortcutKind.Integrated => ("Lhamiel.lnk", "Lhamiel - 圧縮・展開ツール", null),
            DesktopShortcutKind.Extract => ("Lhamiel展開.lnk", "Lhamiel - 展開", "--extract"),
            DesktopShortcutKind.Compress => ("Lhamiel圧縮.lnk", "Lhamiel - 圧縮", "--compress"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>現在の実行ファイルを指すリンクだけを移行し、既に同じ ID なら書き込まない。</summary>
    internal static bool MigrateShortcutIdentity(string shortcutPath, string executablePath, string appId)
    {
        if (!ShortcutTargetsExecutable(shortcutPath, executablePath)
            || ShellLinkNative.GetAppUserModelId(shortcutPath) == appId)
            return false;
        return ShellLinkNative.UpdateExistingShortcutAppUserModelId(shortcutPath, appId);
    }

    private static bool ShortcutTargetsExecutable(string shortcutPath, string executablePath)
    {
        var target = ShellLinkNative.GetTargetPath(shortcutPath);
        return !string.IsNullOrEmpty(target)
            && Path.GetFullPath(Environment.ExpandEnvironmentVariables(target)).Equals(
                Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static void AddKnownShortcuts(HashSet<string> shortcutPaths, Environment.SpecialFolder folder)
    {
        var directory = Environment.GetFolderPath(folder);
        if (string.IsNullOrEmpty(directory))
            return;

        foreach (var kind in Enum.GetValues<DesktopShortcutKind>())
            shortcutPaths.Add(Path.Combine(directory, GetDesktopShortcutDefinition(kind).FileName));
    }

}
