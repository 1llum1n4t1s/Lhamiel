using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lhamiel.Util;

/// <summary>
/// デスクトップにショートカットを作成するユーティリティクラス
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

            var shortcutPath = Path.Combine(desktopPath, "Lhamiel.lnk");
            return CreateShortcut(exePath, shortcutPath, "Lhamiel - 圧縮・展開ツール");
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

            // COMオブジェクトを直接使用してショートカットを作成
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                Logger.Log("WScript.Shell COMオブジェクトの作成に失敗しました");
                return false;
            }
            
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            
            shortcut.TargetPath = targetPath;
            shortcut.Description = description;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            shortcut.Save();

            // COMオブジェクトを解放
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);

            if (File.Exists(shortcutPath))
            {
                Logger.Log($"ショートカットを作成しました: {shortcutPath}");
                return true;
            }
            else
            {
                Logger.Log("ショートカットファイルの作成に失敗しました");
                return false;
            }
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
            // 一般的な方法：Process.GetCurrentProcess().MainModule.FileNameを使用
            var processPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                Logger.Log($"Process.GetCurrentProcess().MainModule.FileName: {processPath}");
                return processPath;
            }
            
            // .NET 9の新しい実行モデルに対応
            // アプリケーションのベースディレクトリからexeファイルを探す
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Logger.Log($"AppDomain.CurrentDomain.BaseDirectory: {baseDirectory}");
            
            // ベースディレクトリ内のexeファイルを探す
            var exeFiles = Directory.GetFiles(baseDirectory, "*.exe");
            if (exeFiles.Length > 0)
            {
                // メインのアプリケーションexeファイルを特定
                // Lhamiel.exeを優先し、見つからない場合は最初のexeファイルを使用
                var mainExe = exeFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Lhamiel.exe", StringComparison.OrdinalIgnoreCase));
                if (mainExe != null)
                {
                    Logger.Log($"メイン実行ファイルを発見: {mainExe}");
                    return mainExe;
                }
                
                // Lhamiel.exeが見つからない場合は、最初のexeファイルを使用
                Logger.Log($"実行ファイルを発見: {exeFiles[0]}");
                return exeFiles[0];
            }
            
            // フォールバック：Assembly.GetExecutingAssembly().Location
            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Logger.Log($"Assembly.GetExecutingAssembly().Location: {assemblyPath}");
            
            // DLLファイルの場合は、同じディレクトリのexeファイルを探す
            if (Path.GetExtension(assemblyPath).ToLowerInvariant() == ".dll")
            {
                var assemblyDir = Path.GetDirectoryName(assemblyPath);
                if (assemblyDir != null)
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                    var exePath = Path.Combine(assemblyDir, assemblyName + ".exe");
                    
                    if (File.Exists(exePath))
                    {
                        Logger.Log($"DLLから派生した実行ファイル: {exePath}");
                        return exePath;
                    }
                }
            }
            
            Logger.Log($"最終的なパス: {assemblyPath}");
            return assemblyPath;
        }
        catch (Exception ex)
        {
            Logger.LogException("実行ファイルパスの取得に失敗しました", ex);
            return "";
        }
    }
}