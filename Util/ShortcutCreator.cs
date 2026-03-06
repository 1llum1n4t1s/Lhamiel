using System.Diagnostics;
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

            var exePath = GetExecutablePath();
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

    /// <summary>
    /// 現在の実行ファイルのパスを取得する
    /// </summary>
    /// <returns>実行ファイルのパス</returns>
    private static string GetExecutablePath()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var processPath = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                Logger.Log($"Process.GetCurrentProcess().MainModule.FileName: {processPath}");
                return processPath;
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Logger.Log($"AppDomain.CurrentDomain.BaseDirectory: {baseDirectory}");

            var exeFiles = Directory.GetFiles(baseDirectory, "*.exe");
            if (exeFiles.Length > 0)
            {
                var mainExe = exeFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Lhamiel.exe", StringComparison.OrdinalIgnoreCase));
                if (mainExe != null)
                {
                    Logger.Log($"メイン実行ファイルを発見: {mainExe}");
                    return mainExe;
                }
                Logger.Log($"実行ファイルを発見: {exeFiles[0]}");
                return exeFiles[0];
            }

            var assemblyPath = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                var exePath = Path.Combine(assemblyPath.TrimEnd(Path.DirectorySeparatorChar), "Lhamiel.exe");
                if (File.Exists(exePath))
                {
                    Logger.Log($"ベースディレクトリから実行ファイル: {exePath}");
                    return exePath;
                }
            }

            Logger.Log($"最終的なパス: {assemblyPath}");
            return assemblyPath ?? "";
        }
        catch (Exception ex)
        {
            Logger.LogException("実行ファイルパスの取得に失敗しました", ex);
            return "";
        }
    }
}
