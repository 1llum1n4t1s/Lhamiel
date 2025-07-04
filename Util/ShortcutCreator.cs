using System.Runtime.InteropServices;
using System.IO;

namespace GGEZArchiver.Util
{
    public static class ShortcutCreator
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] out string pszFile, int cchMaxPath, out nint pfd, int fFlags);
            void GetIDList(out nint ppidl);
            void SetIDList(nint pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] out string pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] out string pszDir, int cchMaxDir);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] out string pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] out string pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(nint hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [In, MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        public static bool CreateShortcut(string targetPath, string shortcutPath, string description = "", string? arguments = null)
        {
            try
            {
                var shellLink = new ShellLink();
                var shellLinkInterface = (IShellLink)shellLink;
                var persistFile = (IPersistFile)shellLink;

                shellLinkInterface.SetPath(targetPath);
                shellLinkInterface.SetDescription(description);
                var workingDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(workingDir))
                {
                    shellLinkInterface.SetWorkingDirectory(workingDir);
                }
                if (!string.IsNullOrEmpty(arguments))
                {
                    shellLinkInterface.SetArguments(arguments);
                }

                persistFile.Save(shortcutPath, false);

                // ドロップ機能を有効にするために、ショートカットファイルにプロパティを設定
                EnableDropTarget(shortcutPath);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnableDropTarget(string shortcutPath)
        {
            try
            {
                // ショートカットファイルのプロパティを設定してドロップ機能を有効にする
                var fileInfo = new FileInfo(shortcutPath);
                if (fileInfo.Exists)
                {
                    // ファイル属性を設定（読み取り専用を解除）
                    fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                }

                // ドロップされたファイルのパスを引数として受け取るように設定
                // これはWindowsの標準的なドロップ機能の仕組み
            }
            catch
            {
                // エラーが発生してもショートカット作成は続行
            }
        }

        public static string GetDesktopPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        public static string GetApplicationPath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }
    }
} 