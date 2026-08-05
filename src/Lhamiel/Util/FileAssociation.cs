using Microsoft.Win32;
using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// ファイル関連付け機能を提供するクラス
/// Windows 11対応：ユーザーレベルの設定を使用してファイル拡張子とアプリケーションの関連付けを管理
/// </summary>
[SupportedOSPlatform("windows")]
public class FileAssociation
{
    private const string ClassesRootPath = @"Software\Classes";
    private static readonly string[] SupportedTypes =
    [
        "zip", "7z", "tar", "gz", "bz2", "lzma", "xz", "rar", "lzh",
        "cab", "arj", "z", "tgz", "tbz2", "tbz", "tlz", "txz", "tz"
    ];

    private static string AppPath => AppPathResolver.ExecutablePath;

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
    private static readonly string FileFolderIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "file_folder.ico");

    /// <summary>
    /// アプリケーション名
    /// レジストリに登録されるアプリケーションの表示名
    /// </summary>
    private static readonly string AppName = "Lhamiel";

    internal static string ResolveFileIconPath(string? variant = null)
    {
        variant ??= SettingsManager.Instance.Current.FileIconVariant;
        var normalized = Settings.NormalizeFileIconVariant(variant);
        var selectedPath = string.Equals(normalized, Settings.FileIconVariantFolder, StringComparison.Ordinal)
            ? FileFolderIconPath
            : FileIconPath;

        if (File.Exists(selectedPath))
            return selectedPath;
        if (File.Exists(FileIconPath))
            return FileIconPath;
        if (File.Exists(FileFolderIconPath))
            return FileFolderIconPath;

        return IconPath;
    }

    /// <summary>
    /// エクスプローラーに変更を通知する（安全な方法）
    /// </summary>
    /// <summary>
    /// エクスプローラーに関連付け変更を一括通知する。
    /// 複数の拡張子を変更した後に一度だけ呼び出すこと。
    /// </summary>
    public static void NotifyExplorer()
    {
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
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
    /// <param name="fileIconVariant">登録するアイコンのバリアント名。null なら現在の設定値を使う</param>
    /// <returns>設定が成功した場合はtrue、そうでなければfalse</returns>
    [SupportedOSPlatform("windows")]
    public static bool AssociateFileType(string extension, string? fileIconVariant = null)
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
            var fileIconToUse = ResolveFileIconPath(fileIconVariant);
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
            DisassociateFileType(Registry.CurrentUser, ClassesRootPath, extension);

            // 共有 OpenWith キー `Software\Classes\Applications\<exe>` はここでは削除しない (#9)。
            // このキーは拡張子に依存せず exe 名だけでキーされ、Windows シェルの「プログラムから開く」が
            // 全ファイル種に対して使う共有登録（ユーザーが手動で『プログラムから開く > Lhamiel』した
            // 履歴）であり、AssociateFileType も作成しない。1 拡張子の解除ごとに無条件削除すると、
            // 他の関連付け済み拡張子やシェルレベルの「Lhamiel で開く」まで巻き添えに消してしまう。
            // 個別拡張子の解除では per-extension キー (上の userKeyPath / appKeyPath) のみを削除する。

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
    /// Lhamiel がサポートする全拡張子の関連付けを解除する。
    /// アンインストール時に使用し、エクスプローラーへの変更通知は最後に一度だけ行う。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool DisassociateAllFileTypes()
    {
        try
        {
            Logger.Log("[関連付け一括解除] 開始", LogLevel.Debug);
            DisassociateFileTypes(Registry.CurrentUser, ClassesRootPath, SupportedTypes);
            NotifyExplorer();
            Logger.Log("[関連付け一括解除] 完了", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("[関連付け一括解除] エラー", ex);
            return false;
        }
    }

    internal static void DisassociateFileTypes(
        RegistryKey root,
        string classesRootPath,
        IEnumerable<string> extensions)
    {
        foreach (var extension in extensions)
            DisassociateFileType(root, classesRootPath, extension);
    }

    internal static void DisassociateFileType(
        RegistryKey root,
        string classesRootPath,
        string extension)
    {
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        var appId = $"Lhamiel{extension}";
        var extensionKeyPath = $@"{classesRootPath}\{extension}";

        // 拡張子キーは他アプリも共有するため、Lhamiel が現在所有する既定値だけを外す。
        // 別アプリへ変更済みなら、その関連付けと OpenWith 情報をそのまま維持する。
        using (var extensionKey = root.OpenSubKey(extensionKeyPath, writable: true))
        {
            if (extensionKey is not null &&
                string.Equals(extensionKey.GetValue("") as string, appId, StringComparison.OrdinalIgnoreCase))
                extensionKey.DeleteValue("", throwOnMissingValue: false);
        }

        // ProgID は Lhamiel 固有なので、子キーを含めて安全に削除できる。
        var appKeyPath = $@"{classesRootPath}\{appId}";
        root.DeleteSubKeyTree(appKeyPath, throwOnMissingSubKey: false);
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
    /// 現在の関連付け状態を取得する
    /// </summary>
    /// <returns>拡張子と関連付け状態の辞書</returns>
    [SupportedOSPlatform("windows")]
    public static Dictionary<string, bool> GetCurrentAssociationStatus()
    {
        var status = new Dictionary<string, bool>();

        foreach (var type in SupportedTypes)
        {
            status[type] = IsFileTypeAssociated(type);
        }

        return status;
    }

}
