using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// IShellLinkW / IPersistFile を P/Invoke で呼び出し、ショートカットを作成する。
/// Native AOT（BuiltInComInteropSupport=false）でも動作する。
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ShellLinkNative
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int CLSCTX_INPROC_SERVER = 1;
    private const int COINIT_APARTMENTTHREADED = 2;

    // IShellLinkW vtable offsets (COM spec)
    private const int VTable_SetPath = 20;
    private const int VTable_SetDescription = 7;
    private const int VTable_SetWorkingDirectory = 9;
    // IUnknown vtable offsets (COM spec)
    private const int VTable_QueryInterface = 0;
    private const int VTable_Release = 2;
    // IPersistFile vtable offsets (COM spec)
    private const int VTable_IPersistFile_Save = 6;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, int dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        int dwClsContext,
        in Guid riid,
        out nint ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(nint thisPtr, in Guid riid, out nint ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPathDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszFile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDescriptionDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetWorkingDirectoryDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SaveDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int fRemember);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint thisPtr);

    /// <summary>
    /// 指定パスにショートカット（.lnk）を作成する。
    /// </summary>
    /// <param name="targetPath">ターゲットのパス</param>
    /// <param name="shortcutPath">ショートカットの保存パス（.lnk）</param>
    /// <param name="description">説明文字列</param>
    /// <returns>成功した場合 true</returns>
    public static bool CreateShortcut(string targetPath, string shortcutPath, string description)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        var hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        try
        {
            var createHr = CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out var pShellLink);
            if (createHr != S_OK || pShellLink == 0)
            {
                return false;
            }

            try
            {
                var vtable = Marshal.ReadIntPtr(pShellLink);
                var setPathPtr = Marshal.ReadIntPtr(vtable, VTable_SetPath * IntPtr.Size);
                var setDescriptionPtr = Marshal.ReadIntPtr(vtable, VTable_SetDescription * IntPtr.Size);
                var setWorkingDirPtr = Marshal.ReadIntPtr(vtable, VTable_SetWorkingDirectory * IntPtr.Size);
                var releasePtr = Marshal.ReadIntPtr(vtable, VTable_Release * IntPtr.Size);

                var setPath = Marshal.GetDelegateForFunctionPointer<SetPathDelegate>(setPathPtr);
                var setDescription = Marshal.GetDelegateForFunctionPointer<SetDescriptionDelegate>(setDescriptionPtr);
                var setWorkingDir = Marshal.GetDelegateForFunctionPointer<SetWorkingDirectoryDelegate>(setWorkingDirPtr);
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);

                if (setPath(pShellLink, targetPath) != S_OK)
                {
                    return false;
                }
                if (setDescription(pShellLink, description) != S_OK)
                {
                    return false;
                }
                var workDir = Path.GetDirectoryName(targetPath) ?? "";
                if (!string.IsNullOrEmpty(workDir) && setWorkingDir(pShellLink, workDir) != S_OK)
                {
                    return false;
                }

                var queryInterfacePtr = Marshal.ReadIntPtr(vtable, VTable_QueryInterface * IntPtr.Size);
                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfacePtr);
                if (queryInterface(pShellLink, IID_IPersistFile, out var pPersistFile) != S_OK || pPersistFile == 0)
                {
                    return false;
                }

                try
                {
                    var persistVtable = Marshal.ReadIntPtr(pPersistFile);
                    // IPersistFile: IUnknown(0,1,2), GetClassID(3), IsDirty(4), Load(5), Save(6)
                    var savePtr = Marshal.ReadIntPtr(persistVtable, 6 * IntPtr.Size);
                    var save = Marshal.GetDelegateForFunctionPointer<SaveDelegate>(savePtr);
                    if (save(pPersistFile, shortcutPath, 1) != S_OK)
                    {
                        return false;
                    }
                }
                finally
                {
                    var persistReleasePtr = Marshal.ReadIntPtr(Marshal.ReadIntPtr(pPersistFile), VTable_Release * IntPtr.Size);
                    var persistRelease = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(persistReleasePtr);
                    _ = persistRelease(pPersistFile);
                }
            }
            finally
            {
                var vtable = Marshal.ReadIntPtr(pShellLink);
                var releasePtr = Marshal.ReadIntPtr(vtable, VTable_Release * IntPtr.Size);
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                _ = release(pShellLink);
            }

            return File.Exists(shortcutPath);
        }
        finally
        {
            if (hr == S_OK || hr == S_FALSE)
            {
                CoUninitialize();
            }
        }
    }
}
