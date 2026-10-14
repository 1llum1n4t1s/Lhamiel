using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Lhamiel.Util;

internal enum ContextMenuOperation
{
    Extract,
    Compress,
}

/// <summary>
/// エクスプローラーのファイル／フォルダ右クリックメニューへ用途別の Lhamiel コマンドを登録する。
/// Windows 10 2004 以降では sparse MSIX の COM コマンドで選択集合を処理し、
/// Windows 11 の IExplorerCommand と従来型 verb のコマンド GUID を揃えて登録する。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellContextMenu
{
    internal const string ExtractMenuText = "Lhamielで展開";
    internal const string CompressMenuText = "Lhamielで圧縮";
    internal const string LegacyVerbName = "Lhamiel.SendTo";
    internal const string ExtractCommandClsid = "{ABB8423C-A40B-4259-9F8A-6C62435C29CA}";
    internal const string CompressCommandClsid = "{E1856DF5-177C-4A12-A9B5-F3D1C63C9D1B}";
    internal const string ExtractVerbName = ExtractCommandClsid;
    internal const string CompressVerbName = CompressCommandClsid;
    private const string PreviousExtractVerbName = "Lhamiel.Extract";
    private const string PreviousCompressVerbName = "Lhamiel.Compress";
    internal const string ModernPackageFamilyName = "Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4";
    internal const string ModernPackageFileName = "Lhamiel.ContextMenu.msix";
    internal const string ShellExtensionFileName = "Lhamiel.ShellExtension.dll";
    internal const string StateKeyName = "Lhamiel.ContextMenu";
    internal const string ExtractEnabledValueName = "ExtractEnabled";
    internal const string CompressEnabledValueName = "CompressEnabled";
    internal const int PackagePendingRemovalHResult = unchecked((int)0x80073D3C);
    private const int NoInterfaceHResult = unchecked((int)0x80004002);
    private const string ClassesRootPath = @"Software\Classes";

    private static readonly string[] FileTargetClasses = ["*"];
    private static readonly string[] CompressTargetClasses = ["*", "Directory"];
    private static readonly string[] LegacyTargetClasses = ["*", "Directory"];

    /// <summary>右クリックメニューの展開／圧縮コマンドを独立して切り替える。</summary>
    internal static bool SetEnabled(bool extractEnabled, bool compressEnabled)
    {
        var enabled = extractEnabled || compressEnabled;
        try
        {
            var appPath = AppPathResolver.ExecutablePath;
            if (enabled && string.IsNullOrWhiteSpace(appPath))
            {
                Logger.Log("右クリックメニューの登録に必要な実行ファイルパスを取得できませんでした。", LogLevel.Warning);
                return false;
            }

            ApplyRegistration(
                Registry.CurrentUser,
                ClassesRootPath,
                appPath,
                extractEnabled,
                compressEnabled,
                OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
                RegisterModernPackageNative,
                IsModernPackageCurrentNative,
                CheckSelectionCommandActivationNative);

            FileAssociation.NotifyExplorer();
            Logger.Log(
                $"右クリックメニュー設定を更新しました: 展開={extractEnabled}, 圧縮={compressEnabled}",
                LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException(enabled
                ? "右クリックメニューの登録に失敗しました"
                : "右クリックメニューの解除に失敗しました", ex);
            return false;
        }
    }

    /// <summary>製品アンインストール時に、モダン／従来方式の登録と表示状態をすべて削除する。</summary>
    internal static bool RemoveAll()
    {
        try
        {
            RemoveRegistration(
                Registry.CurrentUser,
                ClassesRootPath,
                AppPathResolver.ExecutablePath,
                UnregisterModernPackageNative);

            FileAssociation.NotifyExplorer();
            Logger.Log("右クリックメニューの登録を解除しました。", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("右クリックメニューの解除に失敗しました", ex);
            return false;
        }
    }

    /// <summary>対応環境では同じ GUID の COM コマンドで従来／モダンメニューの選択集合を処理する。</summary>
    internal static void ApplyRegistration(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        bool extractEnabled,
        bool compressEnabled,
        bool preferModernMenu,
        Func<string, string, string, int> registerModernPackage,
        Func<string, string, ulong, bool>? isModernPackageCurrent = null,
        Func<string, string, int>? checkSelectionCommandActivation = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registerModernPackage);

        if (!extractEnabled && !compressEnabled)
        {
            // sparse パッケージを削除すると、Explorer が DLL を使用中の間は削除保留になり、
            // 直後の再有効化やアプリ更新による再登録が ERROR_BLOCKED_BY_PENDING_PACKAGE_REMOVAL で失敗する。
            // 通常の設定変更ではパッケージを維持し、ネイティブ拡張の表示フラグだけを無効にする。
            Unregister(root, classesRootPath);
            WriteState(root, classesRootPath, extractEnabled: false, compressEnabled: false);
            return;
        }

        // モダン登録の失敗時は、従来型と公開済みの表示状態を変更しない。
        var useSelectionHandler = preferModernMenu
            && TryRegisterModernPackage(appPath, registerModernPackage, isModernPackageCurrent);

        if (useSelectionHandler && checkSelectionCommandActivation != null)
        {
            // 配置成功だけでは旧 COM サーバーの終了を保証しない。未対応時は従来コマンドを使う。
            var extensionPath = GetModernArtifacts(appPath).ExtensionPath;
            if (extractEnabled)
                useSelectionHandler &= CanUseSelectionHandler(extensionPath, ExtractCommandClsid, checkSelectionCommandActivation);
            if (compressEnabled)
                useSelectionHandler &= CanUseSelectionHandler(extensionPath, CompressCommandClsid, checkSelectionCommandActivation);
        }

        // 従来型の verb 名もネイティブ拡張の canonical GUID に揃え、旧名から移行する。
        // 生の command は COM 非対応のファイルマネージャーや未登録環境向けに維持する。
        Register(root, classesRootPath, appPath, extractEnabled, compressEnabled, useSelectionHandler);
        WriteState(root, classesRootPath, extractEnabled, compressEnabled);
    }

    private static bool CanUseSelectionHandler(
        string extensionPath, string classId, Func<string, string, int> checkSelectionCommandActivation)
    {
        var result = checkSelectionCommandActivation(extensionPath, classId);
        if (result == NoInterfaceHResult)
        {
            Logger.Log(
                $"右クリックの複数選択用 COM インターフェースを利用できません: CLSID={classId}, HRESULT=0x{unchecked((uint)result):X8}。従来のコマンドでメニュー設定を反映します。",
                LogLevel.Warning);
            return false;
        }

        if (result < 0)
        {
            Logger.Log(
                $"右クリックの複数選択用 COM の確認に失敗しました: CLSID={classId}, HRESULT=0x{unchecked((uint)result):X8}。既存のメニュー設定を維持します。",
                LogLevel.Warning);
        }
        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    /// <summary>製品アンインストール専用。通常の設定OFFでは呼び出さない。</summary>
    internal static void RemoveRegistration(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        Func<string, string, int> unregisterModernPackage)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(unregisterModernPackage);

        // DLL が Explorer に残っていても即座に項目を隠せるよう、状態を先に削除する。
        Unregister(root, classesRootPath);
        DeleteState(root, classesRootPath);
        _ = TryUnregisterModernPackage(appPath, unregisterModernPackage);
    }

    internal static bool TryRegisterModernPackage(
        string appPath,
        Func<string, string, string, int> registerModernPackage,
        Func<string, string, ulong, bool>? isModernPackageCurrent = null)
    {
        var artifacts = GetModernArtifacts(appPath);
        if (!File.Exists(artifacts.ExtensionPath) || !File.Exists(artifacts.PackagePath))
            return false;

        if (isModernPackageCurrent?.Invoke(artifacts.ExtensionPath, artifacts.ApplicationDirectory,
                ReadPackageVersion(artifacts.PackagePath)) == true)
            return true;

        var packageUri = new Uri(artifacts.PackagePath).AbsoluteUri;
        var externalLocationUri = new Uri(
            Path.EndsInDirectorySeparator(artifacts.ApplicationDirectory)
                ? artifacts.ApplicationDirectory
                : artifacts.ApplicationDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
        var hResult = registerModernPackage(
            artifacts.ExtensionPath,
            packageUri,
            externalLocationUri);
        if (hResult == PackagePendingRemovalHResult)
        {
            // 削除保留による配置拒否は登録成功ではない。既存の verb と表示状態を保持する。
            Logger.Log(
                "右クリックメニューのパッケージは削除保留中のため登録できません。ログオフ・再ログオン後に再試行してください。",
                LogLevel.Warning);
        }

        Marshal.ThrowExceptionForHR(hResult);
        return true;
    }

    internal static ulong ReadPackageVersion(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = archive.GetEntry("AppxManifest.xml")
            ?? throw new InvalidDataException("MSIX manifest is missing.");
        using var stream = manifest.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
        });
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var value = XDocument.Load(reader).Root?.Element(ns + "Identity")?.Attribute("Version")?.Value;
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision < 0
            || version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue
            || version.Build > ushort.MaxValue || version.Revision > ushort.MaxValue)
            throw new InvalidDataException("Invalid MSIX version.");
        return ((ulong)version.Major << 48) | ((ulong)version.Minor << 32)
            | ((ulong)version.Build << 16) | (uint)version.Revision;
    }

    private static unsafe bool IsModernPackageCurrentNative(string extensionPath, string directory, ulong version)
    {
        // EffectiveExternalPath は末尾区切り付き。配布 DLL の完全比較にも同じ表記を渡す。
        if (!Path.EndsInDirectorySeparator(directory))
            directory += Path.DirectorySeparatorChar;
        var module = NativeLibrary.Load(extensionPath);
        try
        {
            // 古い配布 DLL には照合 API が無いため、従来の登録経路で更新する。
            if (!NativeLibrary.TryGetExport(module, "LhamielIsSparsePackageCurrent", out var address))
                return false;
            var query = (delegate* unmanaged[Stdcall]<char*, char*, ulong, int>)address;
            fixed (char* familyPointer = ModernPackageFamilyName)
            fixed (char* directoryPointer = directory)
            {
                var result = query(familyPointer, directoryPointer, version);
                Marshal.ThrowExceptionForHR(result);
                return result == 0;
            }
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    private static unsafe int CheckSelectionCommandActivationNative(string extensionPath, string classId)
    {
        var module = NativeLibrary.Load(extensionPath);
        try
        {
            var check = (delegate* unmanaged[Stdcall]<char*, int>)NativeLibrary.GetExport(
                module,
                "LhamielCheckSelectionCommandActivation");
            fixed (char* classIdPointer = classId)
                return check(classIdPointer);
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    internal static bool TryUnregisterModernPackage(
        string appPath,
        Func<string, string, int> unregisterModernPackage)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || string.IsNullOrWhiteSpace(appPath))
            return false;

        var artifacts = GetModernArtifacts(appPath);
        if (!File.Exists(artifacts.ExtensionPath))
            return false;

        Marshal.ThrowExceptionForHR(unregisterModernPackage(
            artifacts.ExtensionPath,
            ModernPackageFamilyName));
        return true;
    }

    internal static (string ApplicationDirectory, string ExtensionPath, string PackagePath) GetModernArtifacts(
        string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("実行ファイルパスが空です。", nameof(appPath));

        var applicationDirectory = Path.GetDirectoryName(appPath)
            ?? throw new ArgumentException("実行ファイルの親ディレクトリを取得できません。", nameof(appPath));
        return (
            applicationDirectory,
            Path.Combine(applicationDirectory, ShellExtensionFileName),
            Path.Combine(applicationDirectory, ModernPackageFileName));
    }

    private static unsafe int RegisterModernPackageNative(
        string extensionPath,
        string packageUri,
        string externalLocationUri)
    {
        var module = NativeLibrary.Load(extensionPath);
        try
        {
            var register = (delegate* unmanaged[Stdcall]<char*, char*, int>)NativeLibrary.GetExport(
                module,
                "LhamielRegisterSparsePackage");
            fixed (char* packageUriPointer = packageUri)
            fixed (char* externalLocationPointer = externalLocationUri)
                return register(packageUriPointer, externalLocationPointer);
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    private static unsafe int UnregisterModernPackageNative(string extensionPath, string packageFamilyName)
    {
        var module = NativeLibrary.Load(extensionPath);
        try
        {
            var unregister = (delegate* unmanaged[Stdcall]<char*, int>)NativeLibrary.GetExport(
                module,
                "LhamielUnregisterSparsePackage");
            fixed (char* packageFamilyNamePointer = packageFamilyName)
                return unregister(packageFamilyNamePointer);
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    /// <summary>指定したレジストリ配下へ有効な静的 verb だけを登録する。</summary>
    internal static void Register(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        bool extractEnabled,
        bool compressEnabled,
        bool useSelectionHandler = false)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("実行ファイルパスが空です。", nameof(appPath));

        Unregister(root, classesRootPath);
        if (extractEnabled)
            RegisterOperation(root, classesRootPath, appPath, ContextMenuOperation.Extract, FileTargetClasses, useSelectionHandler);
        if (compressEnabled)
            RegisterOperation(root, classesRootPath, appPath, ContextMenuOperation.Compress, CompressTargetClasses, useSelectionHandler);
    }

    private static void RegisterOperation(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        ContextMenuOperation operation,
        IEnumerable<string> targetClasses,
        bool useSelectionHandler)
    {
        var command = BuildCommand(appPath, operation);
        var icon = $"\"{appPath}\",0";
        var menuText = operation == ContextMenuOperation.Extract ? ExtractMenuText : CompressMenuText;

        foreach (var targetClass in targetClasses)
        {
            var verbPath = BuildVerbPath(classesRootPath, targetClass, operation);
            using var verbKey = root.CreateSubKey(verbPath)
                ?? throw new InvalidOperationException($"レジストリキーを作成できませんでした: {verbPath}");
            verbKey.SetValue("", menuText, RegistryValueKind.String);
            verbKey.SetValue("Icon", icon, RegistryValueKind.String);
            verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);

            using var commandKey = verbKey.CreateSubKey("command")
                ?? throw new InvalidOperationException($"command キーを作成できませんでした: {verbPath}");
            commandKey.SetValue("", command, RegistryValueKind.String);
            if (useSelectionHandler)
            {
                var clsid = operation == ContextMenuOperation.Extract ? ExtractCommandClsid : CompressCommandClsid;
                commandKey.SetValue("DelegateExecute", clsid, RegistryValueKind.String);
            }
        }
    }

    /// <summary>GUID への移行前を含め、新旧すべての Lhamiel verb を削除する。</summary>
    internal static void Unregister(RegistryKey root, string classesRootPath)
    {
        foreach (var targetClass in LegacyTargetClasses)
        {
            root.DeleteSubKeyTree(BuildLegacyVerbPath(classesRootPath, targetClass), throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(BuildVerbPath(classesRootPath, targetClass, ContextMenuOperation.Extract), throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(BuildVerbPath(classesRootPath, targetClass, ContextMenuOperation.Compress), throwOnMissingSubKey: false);
            root.DeleteSubKeyTree($@"{classesRootPath}\{targetClass}\shell\{PreviousExtractVerbName}", throwOnMissingSubKey: false);
            root.DeleteSubKeyTree($@"{classesRootPath}\{targetClass}\shell\{PreviousCompressVerbName}", throwOnMissingSubKey: false);
        }
    }

    internal static void WriteState(
        RegistryKey root,
        string classesRootPath,
        bool extractEnabled,
        bool compressEnabled)
    {
        using var stateKey = root.CreateSubKey(BuildStatePath(classesRootPath))
            ?? throw new InvalidOperationException("右クリックメニュー状態キーを作成できませんでした。");
        stateKey.SetValue(ExtractEnabledValueName, extractEnabled ? 1 : 0, RegistryValueKind.DWord);
        stateKey.SetValue(CompressEnabledValueName, compressEnabled ? 1 : 0, RegistryValueKind.DWord);
    }

    internal static void DeleteState(RegistryKey root, string classesRootPath) =>
        root.DeleteSubKeyTree(BuildStatePath(classesRootPath), throwOnMissingSubKey: false);

    internal static string BuildCommand(string appPath, ContextMenuOperation operation)
    {
        var operationArgument = operation == ContextMenuOperation.Extract ? "--extract" : "--compress";
        return $"\"{appPath}\" {operationArgument} \"%1\"";
    }

    internal static string BuildVerbPath(
        string classesRootPath,
        string targetClass,
        ContextMenuOperation operation)
    {
        var verbName = operation == ContextMenuOperation.Extract ? ExtractVerbName : CompressVerbName;
        return $@"{classesRootPath}\{targetClass}\shell\{verbName}";
    }

    internal static string BuildLegacyVerbPath(string classesRootPath, string targetClass) =>
        $@"{classesRootPath}\{targetClass}\shell\{LegacyVerbName}";

    internal static string BuildStatePath(string classesRootPath) => $@"{classesRootPath}\{StateKeyName}";
}
