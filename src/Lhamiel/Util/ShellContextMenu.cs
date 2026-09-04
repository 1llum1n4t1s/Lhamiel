using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lhamiel.Util;

internal enum ContextMenuOperation
{
    Extract,
    Compress,
}

/// <summary>
/// エクスプローラーのファイル／フォルダ右クリックメニューへ用途別の Lhamiel コマンドを登録する。
/// Windows 11 では sparse MSIX + IExplorerCommand、それ以前では静的 verb を使う。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellContextMenu
{
    internal const string ExtractMenuText = "Lhamielで展開";
    internal const string CompressMenuText = "Lhamielで圧縮";
    internal const string ExtractVerbName = "Lhamiel.Extract";
    internal const string CompressVerbName = "Lhamiel.Compress";
    internal const string LegacyVerbName = "Lhamiel.SendTo";
    internal const string ModernPackageFamilyName = "Nephilim.Lhamiel.ContextMenu_n9k69gpd3y5t4";
    internal const string ModernPackageFileName = "Lhamiel.ContextMenu.msix";
    internal const string ShellExtensionFileName = "Lhamiel.ShellExtension.dll";
    internal const string StateKeyName = "Lhamiel.ContextMenu";
    internal const string ExtractEnabledValueName = "ExtractEnabled";
    internal const string CompressEnabledValueName = "CompressEnabled";
    internal const int PackagePendingRemovalHResult = unchecked((int)0x80073D3C);
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
                OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000),
                RegisterModernPackageNative);

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

    /// <summary>OS と配布物に応じてモダン／従来方式を切り替える。</summary>
    internal static void ApplyRegistration(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        bool extractEnabled,
        bool compressEnabled,
        bool preferModernMenu,
        Func<string, string, string, int> registerModernPackage)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registerModernPackage);

        if (!extractEnabled && !compressEnabled)
        {
            // sparse パッケージを削除すると、Explorer が DLL を使用中の間は削除保留になり、
            // 直後の再有効化やアプリ更新による再登録が ERROR_PACKAGE_PENDING_REMOVAL で失敗する。
            // 通常の設定変更ではパッケージを維持し、ネイティブ拡張の表示フラグだけを無効にする。
            Unregister(root, classesRootPath);
            WriteState(root, classesRootPath, extractEnabled: false, compressEnabled: false);
            return;
        }

        if (preferModernMenu && TryRegisterModernPackage(appPath, registerModernPackage))
        {
            Unregister(root, classesRootPath);
            // パッケージ登録と旧 verb の削除が完了してから、ネイティブ拡張へ新状態を公開する。
            // 先に書くと登録失敗時に UI / settings.json だけが旧値へ戻り、Explorer と不一致になる。
            WriteState(root, classesRootPath, extractEnabled, compressEnabled);
            return;
        }

        // Windows 10、開発ビルド、portable の不完全コピーでは従来方式を維持する。
        Register(root, classesRootPath, appPath, extractEnabled, compressEnabled);
        WriteState(root, classesRootPath, extractEnabled, compressEnabled);
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
        Func<string, string, string, int> registerModernPackage)
    {
        var artifacts = GetModernArtifacts(appPath);
        if (!File.Exists(artifacts.ExtensionPath) || !File.Exists(artifacts.PackagePath))
            return false;

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
            // DeferRegistrationWhenPackagesAreInUse による更新保留中は、現在登録済みの
            // パッケージが引き続き有効。再登録せず状態だけ公開すれば設定変更は反映できる。
            Logger.Log(
                "右クリックメニューのパッケージ更新は Explorer の使用終了まで保留中です。現在の登録へ設定を反映します。",
                LogLevel.Warning);
            return true;
        }

        Marshal.ThrowExceptionForHR(hResult);
        return true;
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
        bool compressEnabled)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("実行ファイルパスが空です。", nameof(appPath));

        Unregister(root, classesRootPath);
        if (extractEnabled)
            RegisterOperation(root, classesRootPath, appPath, ContextMenuOperation.Extract, FileTargetClasses);
        if (compressEnabled)
            RegisterOperation(root, classesRootPath, appPath, ContextMenuOperation.Compress, CompressTargetClasses);
    }

    private static void RegisterOperation(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        ContextMenuOperation operation,
        IEnumerable<string> targetClasses)
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
        }
    }

    /// <summary>新旧すべての Lhamiel 静的 verb を削除する。</summary>
    internal static void Unregister(RegistryKey root, string classesRootPath)
    {
        foreach (var targetClass in LegacyTargetClasses)
        {
            root.DeleteSubKeyTree(BuildLegacyVerbPath(classesRootPath, targetClass), throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(BuildVerbPath(classesRootPath, targetClass, ContextMenuOperation.Extract), throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(BuildVerbPath(classesRootPath, targetClass, ContextMenuOperation.Compress), throwOnMissingSubKey: false);
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
