using Microsoft.Win32;

namespace GGEZArchiver.Util
{
    public static class FileAssociation
    {
        private const string AppName = "GGEZArchiver";
        private static string AppPath => "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" \"%1\"";

        public static void AssociateFileType(string extension, string description)
        {
            try
            {
                // アプリケーションの登録
                using var appKey = Registry.ClassesRoot.CreateSubKey($"{AppName}.{extension}");
                appKey.SetValue("", description);

                using var commandKey = appKey.CreateSubKey("shell\\open\\command");
                commandKey.SetValue("", AppPath);

                // 拡張子の関連付け
                using var extKey = Registry.ClassesRoot.CreateSubKey($".{extension}");
                extKey.SetValue("", $"{AppName}.{extension}");
            }
            catch
            {
                // 管理者権限が必要な場合があるため、エラーは無視
            }
        }

        public static void RemoveFileAssociation(string extension)
        {
            try
            {
                // 拡張子の関連付けを削除
                Registry.ClassesRoot.DeleteSubKeyTree($".{extension}", false);
                
                // アプリケーションの登録を削除
                Registry.ClassesRoot.DeleteSubKeyTree($"{AppName}.{extension}", false);
            }
            catch
            {
                // エラーは無視
            }
        }

        public static bool IsAssociated(string extension)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey($".{extension}");
                if (key != null)
                {
                    var defaultValue = key.GetValue("") as string;
                    return defaultValue?.StartsWith($"{AppName}.{extension}") == true;
                }
            }
            catch
            {
                // エラーは無視
            }

            return false;
        }
    }
} 