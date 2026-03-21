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
    public static bool CreateDesktopShortcut()
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
            return CreateShortcut(exePath, shortcutPath, description);
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
    /// <returns>作成に成功した場合はtrue、失敗した場合はfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool CreateShortcut(string targetPath, string shortcutPath, string description)
    {
        try
        {
            if (!File.Exists(targetPath))
            {
                Logger.Log($"ターゲットファイルが存在しません: {targetPath}");
                return false;
            }

            var ok = ShellLinkNative.CreateShortcut(targetPath, shortcutPath, description ?? "");
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

}
