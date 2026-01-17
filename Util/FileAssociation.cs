using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lhamiel.Util;

/// <summary>
/// ファイル関連付け機能を提供するクラス
/// Windows 11対応：ユーザーレベルの設定を使用してファイル拡張子とアプリケーションの関連付けを管理
/// </summary>
[SupportedOSPlatform("windows")]
public class FileAssociation
{
    // Windows APIの定数
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    // Windows API関数の宣言
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// アプリケーションの実行ファイルパス
    /// 現在実行中のアプリケーションのパスを取得
    /// </summary>
    private static string AppPath
    {
        get
        {
            try
            {
                // 一般的な方法：Process.GetCurrentProcess().MainModule.FileNameを使用
                var processPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    return processPath;
                }
                
                // .NET 9の新しい実行モデルに対応
                // アプリケーションのベースディレクトリからexeファイルを探す
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                
                // ベースディレクトリ内のexeファイルを探す
                var exeFiles = Directory.GetFiles(baseDirectory, "*.exe");
                if (exeFiles.Length > 0)
                {
                    // メインのアプリケーションexeファイルを特定
                    // Lhamiel.exeを優先し、見つからない場合は最初のexeファイルを使用
                    var mainExe = exeFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Lhamiel.exe", StringComparison.OrdinalIgnoreCase));
                    if (mainExe != null)
                    {
                        return mainExe;
                    }
                    
                    // Lhamiel.exeが見つからない場合は、最初のexeファイルを使用
                    return exeFiles[0];
                }
                
                // フォールバック：Assembly.GetExecutingAssembly().Location
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                
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
                            return exePath;
                        }
                    }
                }
                
                return assemblyPath;
            }
            catch (Exception ex)
            {
                Logger.LogException("実行ファイルパスの取得に失敗しました", ex);
                return Assembly.GetExecutingAssembly().Location;
            }
        }
    }

    /// <summary>
    /// アプリケーションのアイコンファイルパス
    /// アプリケーションと同じディレクトリに配置されたICOファイル
    /// </summary>
    private static readonly string IconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");

    /// <summary>
    /// ファイル関連付けのアイコンファイルパス
    /// ファイルマネージャーで表示されるアイコン
    /// </summary>
    private static readonly string FileIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "file.ico");

    /// <summary>
    /// アプリケーション名
    /// レジストリに登録されるアプリケーションの表示名
    /// </summary>
    private static readonly string AppName = "Lhamiel";

    /// <summary>
    /// エクスプローラーに変更を通知する（安全な方法）
    /// </summary>
    private static void SafeNotifyExplorer()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(200);
        }
        catch (Exception ex)
        {
            Logger.LogException("エクスプローラー通知でエラーが発生しました", ex);
        }
    }

    /// <summary>
    /// ファイル関連付けを設定する
    /// Windows 11対応：ユーザーレベルの設定を使用
    /// </summary>
    /// <param name="extension">関連付けるファイル拡張子（例: .zip）</param>
    /// <returns>設定が成功した場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool AssociateFileType(string extension)
    {
        try
        {
            Logger.Log($"[関連付け設定] 開始: {extension}", LogLevel.Debug);

            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            var userKeyPath = $"Software\\Classes\\{extension}";
            Logger.Log($"[関連付け設定] レジストリキー作成: {userKeyPath}", LogLevel.Debug);
            using var userKey = Registry.CurrentUser.CreateSubKey(userKeyPath);
            var appId = $"Lhamiel{extension}";
            userKey?.SetValue("", appId);
            Logger.Log($"[関連付け設定] アプリケーション識別子: {appId}", LogLevel.Debug);

            var appKeyPath = $"Software\\Classes\\{appId}";
            using var appKey = Registry.CurrentUser.CreateSubKey(appKeyPath);
            appKey?.SetValue("FriendlyTypeName", $"{AppName} {extension.ToUpper()} ファイル");

            var shellKeyPath = $"Software\\Classes\\{appId}\\shell\\open\\command";
            using var shellKey = Registry.CurrentUser.CreateSubKey(shellKeyPath);
            var command = $"\"{AppPath}\" \"%1\"";
            shellKey?.SetValue("", command);
            Logger.Log($"[関連付け設定] シェルコマンド: {command}", LogLevel.Debug);

            // ファイル関連付けアイコンを設定（file.icoがあれば使用、なければapp.icoを使用）
            var fileIconToUse = File.Exists(FileIconPath) ? FileIconPath : IconPath;
            if (File.Exists(fileIconToUse))
            {
                var iconKeyPath = $"Software\\Classes\\{appId}\\DefaultIcon";
                using var iconKey = Registry.CurrentUser.CreateSubKey(iconKeyPath);
                iconKey?.SetValue("", fileIconToUse);
                Logger.Log($"[関連付け設定] アイコン: {fileIconToUse}", LogLevel.Debug);
            }

            var typeKeyPath = $"Software\\Classes\\{appId}";
            using var typeKey = Registry.CurrentUser.CreateSubKey(typeKeyPath);
            typeKey?.SetValue("", $"{AppName} {extension.ToUpper()} ファイル");

            Logger.Log($"[関連付け設定] エクスプローラーに通知", LogLevel.Debug);
            SafeNotifyExplorer();

            Logger.Log($"[関連付け設定] 完了: {extension}", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException($"[関連付け設定] エラー: {extension}", ex);
            return false;
        }
    }

    /// <summary>
    /// ファイル関連付けを解除する
    /// Windows 11対応：ユーザーレベルの設定を削除
    /// </summary>
    /// <param name="extension">解除するファイル拡張子（例: .zip）</param>
    /// <returns>解除が成功した場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool DisassociateFileType(string extension)
    {
        try
        {
            Logger.Log($"[関連付け解除] 開始: {extension}", LogLevel.Debug);
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }
            var appId = $"Lhamiel{extension}";
            var userKeyPath = $"Software\\Classes\\{extension}";
            Logger.Log($"[関連付け解除] レジストリキー削除: {userKeyPath}", LogLevel.Debug);
            Registry.CurrentUser.DeleteSubKeyTree(userKeyPath, false);
            var appKeyPath = $"Software\\Classes\\{appId}";
            Logger.Log($"[関連付け解除] アプリケーション識別子キー削除: {appKeyPath}", LogLevel.Debug);
            Registry.CurrentUser.DeleteSubKeyTree(appKeyPath, false);

            // OpenWithでの関連付けも削除
            var openWithKeyPath = $"Software\\Classes\\Applications\\{Path.GetFileName(AppPath)}";
            Logger.Log($"[関連付け解除] OpenWithキー削除: {openWithKeyPath}", LogLevel.Debug);
            Registry.CurrentUser.DeleteSubKeyTree(openWithKeyPath, false);

            Logger.Log($"[関連付け解除] エクスプローラーに通知", LogLevel.Debug);
            SafeNotifyExplorer();
            Logger.Log($"[関連付け解除] 完了: {extension}", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException($"[関連付け解除] エラー: {extension}", ex);
            return false;
        }
    }

    /// <summary>
    /// ファイル関連付けの状態を確認する
    /// Windows 11対応：ユーザーレベルの設定をチェック
    /// </summary>
    /// <param name="extension">チェックするファイル拡張子（例: .zip）</param>
    /// <returns>関連付けられている場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool IsFileTypeAssociated(string extension)
    {
        try
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            var userKeyPath = $"Software\\Classes\\{extension}";
            using var userKey = Registry.CurrentUser.OpenSubKey(userKeyPath);
            if (userKey != null)
            {
                var appId = userKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(appId) && appId.StartsWith("Lhamiel"))
                {
                    var shellKeyPath = $"Software\\Classes\\{appId}\\shell\\open\\command";
                    using var shellKey = Registry.CurrentUser.OpenSubKey(shellKeyPath);
                    if (shellKey != null)
                    {
                        var command = shellKey.GetValue("") as string;
                        var isAssociated = command?.Contains(AppPath) == true;
                        return isAssociated;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException($"ファイル関連付け状態の確認に失敗しました: {extension}", ex);
            return false;
        }
    }

    /// <summary>
    /// すべてのサポートされているファイル形式に関連付けを設定する
    /// </summary>
    /// <returns>すべての設定が成功した場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool AssociateAllSupportedTypes()
    {
        var supportedTypes = new[] { ".zip", ".7z", ".tar", ".gz", ".bz2", ".lzma", ".xz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tgz", ".tbz2", ".tbz", ".tlz", ".txz", ".tz" };
        var success = true;

        foreach (var type in supportedTypes)
        {
            if (!AssociateFileType(type))
            {
                success = false;
            }
        }

        return success;
    }

    /// <summary>
    /// すべてのサポートされているファイル形式の関連付けを解除する
    /// </summary>
    /// <returns>すべての解除が成功した場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool DisassociateAllSupportedTypes()
    {
        var supportedTypes = new[] { ".zip", ".7z", ".tar", ".gz", ".bz2", ".lzma", ".xz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tgz", ".tbz2", ".tbz", ".tlz", ".txz", ".tz" };
        var success = true;

        foreach (var type in supportedTypes)
        {
            if (!DisassociateFileType(type))
            {
                success = false;
            }
        }

        return success;
    }

    /// <summary>
    /// レジストリへの書き込み権限があるかどうかを確認する
    /// </summary>
    /// <returns>権限がある場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool HasRegistryPermission()
    {
        try
        {
            var testKeyPath = "Software\\LhamielTest";
            using var testKey = Registry.CurrentUser.CreateSubKey(testKeyPath);
            if (testKey != null)
            {
                testKey.SetValue("Test", "TestValue");
                Registry.CurrentUser.DeleteSubKey(testKeyPath);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 現在の関連付け状態を取得する
    /// </summary>
    /// <returns>拡張子と関連付け状態の辞書</returns>
    [SupportedOSPlatform("windows")]
    public static Dictionary<string, bool> GetCurrentAssociationStatus()
    {
        var supportedTypes = new[] { "zip", "7z", "tar", "gz", "bz2", "lzma", "xz", "rar", "lzh", "cab", "arj", "z", "tgz", "tbz2", "tbz", "tlz", "txz", "tz" };
        var status = new Dictionary<string, bool>();

        foreach (var type in supportedTypes)
        {
            status[type] = IsFileTypeAssociated(type);
        }

        return status;
    }

    /// <summary>
    /// 指定された拡張子の現在の関連付け状態を取得する
    /// </summary>
    /// <param name="extension">拡張子</param>
    /// <returns>関連付けられている場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool GetCurrentAssociationStatus(string extension)
    {
        return IsFileTypeAssociated(extension);
    }

    /// <summary>
    /// 指定された拡張子の関連付けが完全に設定されているかどうかを確認する
    /// </summary>
    /// <param name="extension">拡張子</param>
    /// <returns>完全に設定されている場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool IsAssociationFullyConfigured(string extension)
    {
        return IsFileTypeAssociated(extension);
    }
}
