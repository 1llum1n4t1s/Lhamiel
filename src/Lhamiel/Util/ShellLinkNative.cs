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
    private const uint STGM_READWRITE = 2;

    // IShellLinkW vtable offsets (COM spec)
    private const int VTable_SetPath = 20;
    private const int VTable_SetDescription = 7;
    private const int VTable_SetWorkingDirectory = 9;
    private const int VTable_SetIconLocation = 17;
    // IUnknown vtable offsets (COM spec)
    private const int VTable_QueryInterface = 0;
    private const int VTable_Release = 2;
    // IPersistFile vtable offsets (COM spec)
    private const int VTable_IPersistFile_Save = 6;
    private const int VTable_IPersistFile_Load = 5;
    // IPropertyStore vtable offsets: IUnknown(0-2), GetCount(3), GetAt(4), GetValue(5), SetValue(6), Commit(7)
    private const int VTable_IPropertyStore_GetValue = 5;
    private const int VTable_IPropertyStore_SetValue = 6;
    private const int VTable_IPropertyStore_Commit = 7;
    private const ushort VT_LPWSTR = 31;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey PKEY_AppUserModel_ID = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    // Lhamiel は x64 / ARM64 のみ。64-bit PROPVARIANT は 24 bytes、値 union は offset 8。
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public nint PointerValue;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, int dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant propVariant);

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
    private delegate int SetIconLocationDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iconIndex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LoadDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint mode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SaveDelegate(nint thisPtr, [MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int fRemember);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetValueDelegate(nint thisPtr, in PropertyKey key, out PropVariant propVariant);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetValueDelegate(nint thisPtr, in PropertyKey key, in PropVariant propVariant);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CommitDelegate(nint thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint thisPtr);

    /// <summary>
    /// 指定パスにショートカット（.lnk）を作成する。
    /// </summary>
    /// <param name="targetPath">ターゲットのパス</param>
    /// <param name="shortcutPath">ショートカットの保存パス（.lnk）</param>
    /// <param name="description">説明文字列</param>
    /// <param name="iconPath">ショートカットに表示するアイコンファイル。null の場合はリンク先のアイコンを使用する。</param>
    /// <param name="appUserModelId">タスクバーのグループ化に使う AppUserModelID。null の場合は設定しない。</param>
    /// <returns>成功した場合 true</returns>
    public static bool CreateShortcut(
        string targetPath,
        string shortcutPath,
        string description,
        string? iconPath = null,
        string? appUserModelId = null)
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
                var setIconLocationPtr = Marshal.ReadIntPtr(vtable, VTable_SetIconLocation * IntPtr.Size);
                var releasePtr = Marshal.ReadIntPtr(vtable, VTable_Release * IntPtr.Size);

                var setPath = Marshal.GetDelegateForFunctionPointer<SetPathDelegate>(setPathPtr);
                var setDescription = Marshal.GetDelegateForFunctionPointer<SetDescriptionDelegate>(setDescriptionPtr);
                var setWorkingDir = Marshal.GetDelegateForFunctionPointer<SetWorkingDirectoryDelegate>(setWorkingDirPtr);
                var setIconLocation = Marshal.GetDelegateForFunctionPointer<SetIconLocationDelegate>(setIconLocationPtr);
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
                if (!string.IsNullOrEmpty(iconPath) && setIconLocation(pShellLink, iconPath, 0) != S_OK)
                {
                    return false;
                }
                if (!string.IsNullOrEmpty(appUserModelId))
                {
                    var appIdHr = SetAppUserModelId(pShellLink, appUserModelId);
                    if (appIdHr < 0)
                    {
                        return false;
                    }
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

    /// <summary>
    /// 既存ショートカットのリンク先を変えずにアイコンだけを更新する。
    /// </summary>
    public static bool UpdateIconLocation(string shortcutPath, string iconPath, string? appUserModelId = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || !File.Exists(shortcutPath)
            || !File.Exists(iconPath))
        {
            return false;
        }

        var hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        var iconSaved = false;
        try
        {
            var createHr = CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out var pShellLink);
            if (createHr != S_OK || pShellLink == 0)
                return false;

            try
            {
                var shellVtable = Marshal.ReadIntPtr(pShellLink);
                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
                    Marshal.ReadIntPtr(shellVtable, VTable_QueryInterface * IntPtr.Size));
                if (queryInterface(pShellLink, IID_IPersistFile, out var pPersistFile) != S_OK || pPersistFile == 0)
                    return false;

                try
                {
                    var persistVtable = Marshal.ReadIntPtr(pPersistFile);
                    var load = Marshal.GetDelegateForFunctionPointer<LoadDelegate>(
                        Marshal.ReadIntPtr(persistVtable, VTable_IPersistFile_Load * IntPtr.Size));
                    if (load(pPersistFile, shortcutPath, 0) != S_OK)
                        return false;

                    var setIconLocation = Marshal.GetDelegateForFunctionPointer<SetIconLocationDelegate>(
                        Marshal.ReadIntPtr(shellVtable, VTable_SetIconLocation * IntPtr.Size));
                    if (setIconLocation(pShellLink, iconPath, 0) != S_OK)
                        return false;

                    var save = Marshal.GetDelegateForFunctionPointer<SaveDelegate>(
                        Marshal.ReadIntPtr(persistVtable, VTable_IPersistFile_Save * IntPtr.Size));
                    iconSaved = save(pPersistFile, shortcutPath, 1) == S_OK;
                }
                finally
                {
                    Release(pPersistFile);
                }
            }
            finally
            {
                Release(pShellLink);
            }

            if (!iconSaved)
                return false;

            return iconSaved
                && (string.IsNullOrEmpty(appUserModelId)
                    || UpdateExistingShortcutAppUserModelId(shortcutPath, appUserModelId));
        }
        finally
        {
            if (hr == S_OK || hr == S_FALSE)
                CoUninitialize();
        }
    }

    /// <summary>
    /// テストと診断用に、ショートカットへ保存された AppUserModelID を取得する。
    /// </summary>
    internal static string? GetAppUserModelId(string shortcutPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !File.Exists(shortcutPath))
            return null;

        var hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        try
        {
            if (CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out var pShellLink) != S_OK
                || pShellLink == 0)
                return null;

            try
            {
                var shellVtable = Marshal.ReadIntPtr(pShellLink);
                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
                    Marshal.ReadIntPtr(shellVtable, VTable_QueryInterface * IntPtr.Size));
                if (queryInterface(pShellLink, IID_IPersistFile, out var pPersistFile) != S_OK || pPersistFile == 0)
                    return null;

                try
                {
                    var persistVtable = Marshal.ReadIntPtr(pPersistFile);
                    var load = Marshal.GetDelegateForFunctionPointer<LoadDelegate>(
                        Marshal.ReadIntPtr(persistVtable, VTable_IPersistFile_Load * IntPtr.Size));
                    if (load(pPersistFile, shortcutPath, 0) != S_OK)
                        return null;

                    if (queryInterface(pShellLink, IID_IPropertyStore, out var pPropertyStore) != S_OK
                        || pPropertyStore == 0)
                        return null;

                    try
                    {
                        var propertyVtable = Marshal.ReadIntPtr(pPropertyStore);
                        var getValue = Marshal.GetDelegateForFunctionPointer<GetValueDelegate>(
                            Marshal.ReadIntPtr(propertyVtable, VTable_IPropertyStore_GetValue * IntPtr.Size));
                        if (getValue(pPropertyStore, PKEY_AppUserModel_ID, out var value) < 0)
                            return null;

                        try
                        {
                            return value.VariantType == VT_LPWSTR && value.PointerValue != 0
                                ? Marshal.PtrToStringUni(value.PointerValue)
                                : null;
                        }
                        finally
                        {
                            _ = PropVariantClear(ref value);
                        }
                    }
                    finally
                    {
                        Release(pPropertyStore);
                    }
                }
                finally
                {
                    Release(pPersistFile);
                }
            }
            finally
            {
                Release(pShellLink);
            }
        }
        finally
        {
            if (hr == S_OK || hr == S_FALSE)
                CoUninitialize();
        }
    }

    private static int SetAppUserModelId(nint pShellLink, string appUserModelId)
    {
        var shellVtable = Marshal.ReadIntPtr(pShellLink);
        var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
            Marshal.ReadIntPtr(shellVtable, VTable_QueryInterface * IntPtr.Size));
        var queryHr = queryInterface(pShellLink, IID_IPropertyStore, out var pPropertyStore);
        if (queryHr != S_OK || pPropertyStore == 0)
            return queryHr;

        try
        {
            var value = new PropVariant
            {
                VariantType = VT_LPWSTR,
                PointerValue = Marshal.StringToCoTaskMemUni(appUserModelId),
            };

            try
            {
                var propertyVtable = Marshal.ReadIntPtr(pPropertyStore);
                var setValue = Marshal.GetDelegateForFunctionPointer<SetValueDelegate>(
                    Marshal.ReadIntPtr(propertyVtable, VTable_IPropertyStore_SetValue * IntPtr.Size));
                var setHr = setValue(pPropertyStore, PKEY_AppUserModel_ID, value);
                if (setHr < 0)
                    return setHr;

                var commit = Marshal.GetDelegateForFunctionPointer<CommitDelegate>(
                    Marshal.ReadIntPtr(propertyVtable, VTable_IPropertyStore_Commit * IntPtr.Size));
                return commit(pPropertyStore);
            }
            finally
            {
                _ = PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(pPropertyStore);
        }
    }

    private static bool UpdateExistingShortcutAppUserModelId(string shortcutPath, string appUserModelId)
    {
        if (CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out var pShellLink) != S_OK
            || pShellLink == 0)
            return false;

        try
        {
            var shellVtable = Marshal.ReadIntPtr(pShellLink);
            var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
                Marshal.ReadIntPtr(shellVtable, VTable_QueryInterface * IntPtr.Size));
            if (queryInterface(pShellLink, IID_IPersistFile, out var pPersistFile) != S_OK || pPersistFile == 0)
                return false;

            try
            {
                var persistVtable = Marshal.ReadIntPtr(pPersistFile);
                var load = Marshal.GetDelegateForFunctionPointer<LoadDelegate>(
                    Marshal.ReadIntPtr(persistVtable, VTable_IPersistFile_Load * IntPtr.Size));
                if (load(pPersistFile, shortcutPath, STGM_READWRITE) != S_OK
                    || SetAppUserModelId(pShellLink, appUserModelId) < 0)
                    return false;

                var save = Marshal.GetDelegateForFunctionPointer<SaveDelegate>(
                    Marshal.ReadIntPtr(persistVtable, VTable_IPersistFile_Save * IntPtr.Size));
                return save(pPersistFile, shortcutPath, 1) == S_OK;
            }
            finally
            {
                Release(pPersistFile);
            }
        }
        finally
        {
            Release(pShellLink);
        }
    }

    private static void Release(nint comObject)
    {
        var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(comObject), VTable_Release * IntPtr.Size));
        _ = release(comObject);
    }
}
