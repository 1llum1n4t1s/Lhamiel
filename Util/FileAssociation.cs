using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GGEZArchiver.Util;

/// <summary>
/// ファイル関連付け機能を提供するクラス
/// Windows 11対応：ユーザーレベルの設定を使用してファイル拡張子とアプリケーションの関連付けを管理
/// </summary>
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
    private static readonly string AppPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

    /// <summary>
    /// アプリケーションのアイコンファイルパス
    /// アプリケーションと同じディレクトリに配置されたICOファイル
    /// </summary>
    private static readonly string IconPath = Path.Combine(
        System.AppDomain.CurrentDomain.BaseDirectory, "app.ico");

    /// <summary>
    /// アプリケーション名
    /// レジストリに登録されるアプリケーションの表示名
    /// </summary>
    private static readonly string AppName = "GGEZArchiver";

    /// <summary>
    /// ファイル関連付けを設定する
    /// Windows 11対応：ユーザーレベルの設定を使用
    /// </summary>
    /// <param name="extension">関連付けるファイル拡張子（例: .zip）</param>
    /// <returns>設定が成功した場合はtrue、そうでなければfalse</returns>
    public static bool AssociateFileType(string extension)
    {
        try
        {
            // 拡張子を正規化（先頭にドットがない場合は追加）
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            // Windows 11対応：ユーザーレベルの設定を使用
            var userKeyPath = $"Software\\Classes\\{extension}";
            using var userKey = Registry.CurrentUser.CreateSubKey(userKeyPath);
            
            // アプリケーション識別子を設定
            var appId = $"GGEZArchiver{extension}";
            userKey?.SetValue("", appId);

            // アプリケーション識別子の詳細設定
            var appKeyPath = $"Software\\Classes\\{appId}";
            using var appKey = Registry.CurrentUser.CreateSubKey(appKeyPath);
            appKey?.SetValue("FriendlyTypeName", $"{AppName} {extension.ToUpper()} ファイル");

            // シェルコマンドの設定
            var shellKeyPath = $"Software\\Classes\\{appId}\\shell\\open\\command";
            using var shellKey = Registry.CurrentUser.CreateSubKey(shellKeyPath);
            shellKey?.SetValue("", $"\"{AppPath}\" \"%1\"");

            // アイコンの設定（ICOファイルが存在する場合）
            if (File.Exists(IconPath))
            {
                var iconKeyPath = $"Software\\Classes\\{appId}\\DefaultIcon";
                using var iconKey = Registry.CurrentUser.CreateSubKey(iconKeyPath);
                iconKey?.SetValue("", IconPath);
            }

            // ファイルの種類の説明を設定
            var typeKeyPath = $"Software\\Classes\\{appId}";
            using var typeKey = Registry.CurrentUser.CreateSubKey(typeKeyPath);
            typeKey?.SetValue("", $"{AppName} {extension.ToUpper()} ファイル");

            // エクスプローラーに通知
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// ファイル関連付けを解除する
    /// Windows 11対応：ユーザーレベルの設定を削除
    /// </summary>
    /// <param name="extension">解除するファイル拡張子（例: .zip）</param>
    /// <returns>解除が成功した場合はtrue、そうでなければfalse</returns>
    public static bool DisassociateFileType(string extension)
    {
        try
        {
            // 拡張子を正規化（先頭にドットがない場合は追加）
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            // アプリケーション識別子
            var appId = $"GGEZArchiver{extension}";

            // ユーザーレベルの設定を削除
            var userKeyPath = $"Software\\Classes\\{extension}";
            Registry.CurrentUser.DeleteSubKeyTree(userKeyPath, false);

            // アプリケーション識別子の詳細設定も削除
            var appKeyPath = $"Software\\Classes\\{appId}";
            Registry.CurrentUser.DeleteSubKeyTree(appKeyPath, false);

            // エクスプローラーに通知
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// ファイル関連付けの状態を確認する
    /// Windows 11対応：ユーザーレベルの設定をチェック
    /// </summary>
    /// <param name="extension">チェックするファイル拡張子（例: .zip）</param>
    /// <returns>関連付けられている場合はtrue、そうでなければfalse</returns>
    public static bool IsFileTypeAssociated(string extension)
    {
        try
        {
            // 拡張子を正規化（先頭にドットがない場合は追加）
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            // ユーザーレベルの設定をチェック
            var userKeyPath = $"Software\\Classes\\{extension}";
            using var userKey = Registry.CurrentUser.OpenSubKey(userKeyPath);
            
            if (userKey != null)
            {
                var appId = userKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(appId) && appId.StartsWith("GGEZArchiver"))
                {
                    // アプリケーション識別子のコマンドをチェック
                    var shellKeyPath = $"Software\\Classes\\{appId}\\shell\\open\\command";
                    using var shellKey = Registry.CurrentUser.OpenSubKey(shellKeyPath);
                    
                    if (shellKey != null)
                    {
                        var command = shellKey.GetValue("") as string;
                        return command?.Contains(AppPath) == true;
                    }
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// すべてのサポートされているファイル形式に関連付けを設定する
    /// Settingsクラスで定義された展開形式すべてに関連付けを作成
    /// </summary>
    /// <returns>設定が成功した場合はtrue、そうでなければfalse</returns>
    public static bool AssociateAllSupportedTypes()
    {
        var success = true;
        
        foreach (var format in Settings.SupportedExtractionFormats)
        {
            if (!AssociateFileType(format))
            {
                success = false;
            }
        }

        return success;
    }

    /// <summary>
    /// すべてのサポートされているファイル形式の関連付けを解除する
    /// Settingsクラスで定義された展開形式すべての関連付けを削除
    /// </summary>
    /// <returns>解除が成功した場合はtrue、そうでなければfalse</returns>
    public static bool DisassociateAllSupportedTypes()
    {
        var success = true;
        
        foreach (var format in Settings.SupportedExtractionFormats)
        {
            if (!DisassociateFileType(format))
            {
                success = false;
            }
        }

        return success;
    }

    /// <summary>
    /// レジストリの権限をチェックする
    /// ファイル関連付けの設定に必要な権限があるかを確認
    /// </summary>
    /// <returns>権限がある場合はtrue、そうでなければfalse</returns>
    public static bool HasRegistryPermission()
    {
        try
        {
            // テスト用のキーを作成して権限をチェック
            var testKeyName = "Software\\GGEZArchiver\\Test";
            using var testKey = Registry.CurrentUser.CreateSubKey(testKeyName);
            if (testKey != null)
            {
                Registry.CurrentUser.DeleteSubKey(testKeyName);
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 現在のWindowsのファイル関連付け状態を取得する
    /// 各拡張子について実際の関連付け状態をチェックして返す
    /// </summary>
    /// <returns>拡張子と関連付け状態の辞書</returns>
    public static Dictionary<string, bool> GetCurrentAssociationStatus()
    {
        var status = new Dictionary<string, bool>();
        
        foreach (var format in Settings.SupportedExtractionFormats)
        {
            status[format] = IsFileTypeAssociated(format);
        }
        
        return status;
    }

    /// <summary>
    /// 指定された拡張子の現在の関連付け状態を取得する
    /// </summary>
    /// <param name="extension">チェックするファイル拡張子（例: .zip）</param>
    /// <returns>関連付けられている場合はtrue、そうでなければfalse</returns>
    public static bool GetCurrentAssociationStatus(string extension)
    {
        return IsFileTypeAssociated(extension);
    }

    /// <summary>
    /// ファイル関連付けの設定が正常に完了したかどうかを確認する
    /// Windows 11では設定後にエクスプローラーの再起動が必要な場合がある
    /// </summary>
    /// <param name="extension">チェックするファイル拡張子</param>
    /// <returns>設定が完了している場合はtrue、そうでなければfalse</returns>
    public static bool IsAssociationFullyConfigured(string extension)
    {
        try
        {
            // 拡張子を正規化
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            // ユーザーレベルの設定をチェック
            var userKeyPath = $"Software\\Classes\\{extension}";
            using var userKey = Registry.CurrentUser.OpenSubKey(userKeyPath);
            
            if (userKey != null)
            {
                var appId = userKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(appId) && appId.StartsWith("GGEZArchiver"))
                {
                    // アプリケーション識別子のコマンドをチェック
                    var shellKeyPath = $"Software\\Classes\\{appId}\\shell\\open\\command";
                    using var shellKey = Registry.CurrentUser.OpenSubKey(shellKeyPath);
                    
                    if (shellKey != null)
                    {
                        var command = shellKey.GetValue("") as string;
                        if (command?.Contains(AppPath) == true)
                        {
                            // エクスプローラーに通知して設定を反映
                            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}