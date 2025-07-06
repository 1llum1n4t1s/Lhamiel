using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GGEZArchiver.Util
{
    /// <summary>
    /// ショートカット作成機能を提供するクラス
    /// dynamicを使用してWshShell経由でショートカットファイル（.lnk）を作成
    /// </summary>
    public class ShortcutCreator
    {
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
        /// デスクトップにショートカットを作成する
        /// アプリケーションのショートカットをデスクトップに配置
        /// </summary>
        /// <param name="shortcutName">ショートカットの表示名（拡張子なし）</param>
        /// <returns>作成が成功した場合はtrue、そうでなければfalse</returns>
        public static bool CreateDesktopShortcut(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                
                return CreateShortcut(shortcutPath, AppPath, "GGEZアーカイバー - 圧縮・展開ツール");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// スタートメニューにショートカットを作成する
        /// アプリケーションのショートカットをスタートメニューに配置
        /// </summary>
        /// <param name="shortcutName">ショートカットの表示名（拡張子なし）</param>
        /// <returns>作成が成功した場合はtrue、そうでなければfalse</returns>
        public static bool CreateStartMenuShortcut(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var programPath = Path.Combine(startMenuPath, "Programs");
                
                // Programsフォルダが存在しない場合は作成
                if (!Directory.Exists(programPath))
                {
                    Directory.CreateDirectory(programPath);
                }
                
                var shortcutPath = Path.Combine(programPath, $"{shortcutName}.lnk");
                
                return CreateShortcut(shortcutPath, AppPath, "GGEZアーカイバー - 圧縮・展開ツール");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指定されたパスにショートカットを作成する
        /// dynamicを使用してWshShell経由でショートカットファイルを生成
        /// </summary>
        /// <param name="shortcutPath">作成するショートカットファイルのパス</param>
        /// <param name="targetPath">ショートカットが指すターゲットファイルのパス</param>
        /// <param name="description">ショートカットの説明</param>
        /// <returns>作成が成功した場合はtrue、そうでなければfalse</returns>
        public static bool CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            try
            {
                // COMのWshShellをdynamicで生成
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.Description = description;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                if (File.Exists(IconPath))
                    shortcut.IconLocation = IconPath;
                shortcut.Save();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 既存のショートカットを削除する
        /// 指定されたパスのショートカットファイルを削除
        /// </summary>
        /// <param name="shortcutPath">削除するショートカットファイルのパス</param>
        /// <returns>削除が成功した場合はtrue、そうでなければfalse</returns>
        public static bool DeleteShortcut(string shortcutPath)
        {
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
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
        /// デスクトップのショートカットを削除する
        /// デスクトップに配置されたアプリケーションのショートカットを削除
        /// </summary>
        /// <param name="shortcutName">削除するショートカットの名前（拡張子なし）</param>
        /// <returns>削除が成功した場合はtrue、そうでなければfalse</returns>
        public static bool DeleteDesktopShortcut(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                
                return DeleteShortcut(shortcutPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// スタートメニューのショートカットを削除する
        /// スタートメニューに配置されたアプリケーションのショートカットを削除
        /// </summary>
        /// <param name="shortcutName">削除するショートカットの名前（拡張子なし）</param>
        /// <returns>削除が成功した場合はtrue、そうでなければfalse</returns>
        public static bool DeleteStartMenuShortcut(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var programPath = Path.Combine(startMenuPath, "Programs");
                var shortcutPath = Path.Combine(programPath, $"{shortcutName}.lnk");
                
                return DeleteShortcut(shortcutPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ショートカットが存在するかどうかを確認する
        /// 指定されたパスのショートカットファイルの存在をチェック
        /// </summary>
        /// <param name="shortcutPath">チェックするショートカットファイルのパス</param>
        /// <returns>ショートカットが存在する場合はtrue、そうでなければfalse</returns>
        public static bool ShortcutExists(string shortcutPath)
        {
            return File.Exists(shortcutPath);
        }

        /// <summary>
        /// デスクトップのショートカットが存在するかどうかを確認する
        /// </summary>
        /// <param name="shortcutName">チェックするショートカットの名前（拡張子なし）</param>
        /// <returns>ショートカットが存在する場合はtrue、そうでなければfalse</returns>
        public static bool DesktopShortcutExists(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
                
                return ShortcutExists(shortcutPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// スタートメニューのショートカットが存在するかどうかを確認する
        /// </summary>
        /// <param name="shortcutName">チェックするショートカットの名前（拡張子なし）</param>
        /// <returns>ショートカットが存在する場合はtrue、そうでなければfalse</returns>
        public static bool StartMenuShortcutExists(string shortcutName = "GGEZArchiver")
        {
            try
            {
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var programPath = Path.Combine(startMenuPath, "Programs");
                var shortcutPath = Path.Combine(programPath, $"{shortcutName}.lnk");
                
                return ShortcutExists(shortcutPath);
            }
            catch
            {
                return false;
            }
        }
    }
}