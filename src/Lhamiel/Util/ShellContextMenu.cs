using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lhamiel.Util;

/// <summary>
/// エクスプローラーのファイル／フォルダ右クリックメニューに Lhamiel を登録する。
/// Windows 11 では sparse MSIX + IExplorerCommand、それ以前では静的 verb を使う。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellContextMenu
{
    internal const string MenuText = "Lhamielへ";
    internal const string VerbName = "Lhamiel.SendTo";
    internal const string ModernPackageName = "Nephilim.Lhamiel.ContextMenu";
    internal const string ModernPackageFileName = "Lhamiel.ContextMenu.msix";
    internal const string ShellExtensionFileName = "Lhamiel.ShellExtension.dll";
    private const string ClassesRootPath = @"Software\Classes";

    private static readonly string[] TargetClasses = ["*", "Directory"];

    /// <summary>
    /// 右クリックメニューの登録状態を切り替える。
    /// </summary>
    internal static bool SetEnabled(bool enabled)
    {
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
                enabled,
                OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000),
                RegisterModernPackageNative,
                UnregisterModernPackageNative);

            FileAssociation.NotifyExplorer();
            Logger.Log(enabled
                ? "ファイルとフォルダの右クリックメニューに「Lhamielへ」を登録しました。Windows 11 では新しいメニューを使用します。"
                : "ファイルとフォルダの右クリックメニューから「Lhamielへ」を解除しました。",
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

    /// <summary>
    /// OS と配布物に応じてモダン／従来方式を切り替える。
    /// モダン登録が使える場合は従来 verb を消し、クラシックメニューでの重複を避ける。
    /// </summary>
    internal static void ApplyRegistration(
        RegistryKey root,
        string classesRootPath,
        string appPath,
        bool enabled,
        bool preferModernMenu,
        Func<string, string, string, int> registerModernPackage,
        Func<string, string, int> unregisterModernPackage)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registerModernPackage);
        ArgumentNullException.ThrowIfNull(unregisterModernPackage);

        if (!enabled)
        {
            // パッケージが残っている間はモダンメニューが有効なので、先に解除してから静的 verb を消す。
            _ = TryUnregisterModernPackage(appPath, unregisterModernPackage);
            Unregister(root, classesRootPath);
            return;
        }

        if (preferModernMenu && TryRegisterModernPackage(appPath, registerModernPackage))
        {
            Unregister(root, classesRootPath);
            return;
        }

        // Windows 10、開発ビルド、portable の不完全コピーでは従来方式を維持する。
        Register(root, classesRootPath, appPath);
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
        Marshal.ThrowExceptionForHR(registerModernPackage(
            artifacts.ExtensionPath,
            packageUri,
            externalLocationUri));
        return true;
    }

    internal static bool TryUnregisterModernPackage(
        string appPath,
        Func<string, string, int> unregisterModernPackage)
    {
        // PackageManager の外部配置 API は Windows 10 2004 以降。
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || string.IsNullOrWhiteSpace(appPath))
            return false;

        var artifacts = GetModernArtifacts(appPath);
        if (!File.Exists(artifacts.ExtensionPath))
            return false;

        Marshal.ThrowExceptionForHR(unregisterModernPackage(
            artifacts.ExtensionPath,
            ModernPackageName));
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

    private static unsafe int UnregisterModernPackageNative(string extensionPath, string packageName)
    {
        var module = NativeLibrary.Load(extensionPath);
        try
        {
            var unregister = (delegate* unmanaged[Stdcall]<char*, int>)NativeLibrary.GetExport(
                module,
                "LhamielUnregisterSparsePackage");
            fixed (char* packageNamePointer = packageName)
                return unregister(packageNamePointer);
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    /// <summary>
    /// 指定したレジストリ配下へファイル／フォルダ共通の verb を登録する。
    /// テストでは分離したキーを渡して実レジストリ設定を汚さない。
    /// </summary>
    internal static void Register(RegistryKey root, string classesRootPath, string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("実行ファイルパスが空です。", nameof(appPath));

        var command = BuildCommand(appPath);
        var icon = $"\"{appPath}\",0";

        foreach (var targetClass in TargetClasses)
        {
            var verbPath = BuildVerbPath(classesRootPath, targetClass);
            using var verbKey = root.CreateSubKey(verbPath)
                ?? throw new InvalidOperationException($"レジストリキーを作成できませんでした: {verbPath}");
            verbKey.SetValue("", MenuText, RegistryValueKind.String);
            verbKey.SetValue("Icon", icon, RegistryValueKind.String);
            // 複数選択時も選択項目を同じ Lhamiel プロセスへ渡せるようにする。
            verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);

            using var commandKey = verbKey.CreateSubKey("command")
                ?? throw new InvalidOperationException($"command キーを作成できませんでした: {verbPath}");
            commandKey.SetValue("", command, RegistryValueKind.String);
        }
    }

    /// <summary>
    /// 指定したレジストリ配下から Lhamiel が所有する verb だけを削除する。
    /// </summary>
    internal static void Unregister(RegistryKey root, string classesRootPath)
    {
        foreach (var targetClass in TargetClasses)
            root.DeleteSubKeyTree(BuildVerbPath(classesRootPath, targetClass), throwOnMissingSubKey: false);
    }

    internal static string BuildCommand(string appPath) => $"\"{appPath}\" \"%1\"";

    internal static string BuildVerbPath(string classesRootPath, string targetClass) =>
        $@"{classesRootPath}\{targetClass}\shell\{VerbName}";
}
