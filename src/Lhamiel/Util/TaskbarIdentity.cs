using System.Runtime.InteropServices;

namespace Lhamiel.Util;

/// <summary>起動経路にかかわらず、同じ配置先のアプリとショートカットを同じ AUMID に揃える。</summary>
internal static partial class TaskbarIdentity
{
    internal const string UnpackagedAppId = "velopack.Lhamiel";
    internal const string PackagedAppId = ShellContextMenu.ModernPackageFamilyName + "!Lhamiel.ContextMenu";
    private const int InsufficientBuffer = 122;
    private static readonly Lazy<string> CurrentId = new(() => GetAppId(AppPathResolver.ExecutablePath));

    internal static string Current => CurrentId.Value;

    internal static string Resolve(string? processAppId, string executablePath, IEnumerable<string> externalDirectories)
    {
        if (!string.IsNullOrEmpty(processAppId)) return processAppId;
        if (!Path.GetFileName(executablePath).Equals("Lhamiel.exe", StringComparison.OrdinalIgnoreCase))
            return UnpackagedAppId;

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(executablePath)!));
        foreach (var externalDirectory in externalDirectories)
        {
            if (directory.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(externalDirectory)),
                    StringComparison.OrdinalIgnoreCase))
                return PackagedAppId;
        }
        return UnpackagedAppId;
    }

    internal static string GetAppId(string executablePath)
    {
        if (!OperatingSystem.IsWindows()) return UnpackagedAppId;
        try
        {
            var processAppId = ReadProcessAppId();
            if (!string.IsNullOrEmpty(processAppId)) return processAppId;
            return Resolve(null, executablePath, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                ? ReadExternalDirectories() : []);
        }
        catch (Exception ex)
        {
            Logger.LogException("タスクバーのアプリ識別子を取得できませんでした", ex);
            return UnpackagedAppId;
        }
    }

    internal static void Apply()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var result = NativeMethods.SetCurrentProcessExplicitAppUserModelID(Current);
            if (result < 0)
                Logger.Log($"タスクバーのアプリ識別子を設定できませんでした: 0x{result:X8}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            Logger.LogException("タスクバーのアプリ識別子の設定に失敗しました", ex);
        }
    }

    private static unsafe string? ReadProcessAppId()
    {
        // AUMID の上限は 128 文字。終端を含めたバッファで受け取る。
        uint length = 130;
        var buffer = stackalloc char[(int)length];
        return GetCurrentApplicationUserModelId(ref length, buffer) == 0 ? new string(buffer) : null;
    }

    internal static unsafe IReadOnlyList<string> ReadExternalDirectories()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return [];
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(ShellContextMenu.ModernPackageFamilyName, ref count, null, ref length, null)
            != InsufficientBuffer || count == 0 || count > 1024 || length > 1024 * 1024)
            return [];

        var pointers = new nint[count];
        var buffer = new char[length];
        var directories = new List<string>();
        fixed (nint* names = pointers)
        fixed (char* characters = buffer)
        {
            if (GetPackagesByPackageFamily(ShellContextMenu.ModernPackageFamilyName, ref count, names, ref length, characters) != 0)
                return [];
            for (var i = 0; i < count; i++)
            {
                var fullName = new string((char*)names[i]);
                uint pathLength = 0;
                // PackagePathType_EffectiveExternal = 5。別の開発配置先を登録済みアプリと混同しない。
                if (GetPackagePathByFullName2(fullName, 5, ref pathLength, null) != InsufficientBuffer
                    || pathLength == 0 || pathLength > 32768)
                    continue;
                var path = new char[pathLength];
                fixed (char* pathBuffer = path)
                    if (GetPackagePathByFullName2(fullName, 5, ref pathLength, pathBuffer) == 0)
                        directories.Add(new string(pathBuffer));
            }
        }
        return directories;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentApplicationUserModelId(ref uint length, char* value);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetPackagesByPackageFamily(string family, ref uint count, nint* names, ref uint length, char* buffer);

    [LibraryImport("kernelbase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetPackagePathByFullName2(string fullName, int pathType, ref uint length, char* path);
}
