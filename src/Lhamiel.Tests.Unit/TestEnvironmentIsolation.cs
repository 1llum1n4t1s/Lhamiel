using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lhamiel.Util;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// テストアセンブリの読み込み直後に、設定・.lhaignore・ログ等の保存先を使い捨てフォルダーへ差し替える。
/// 差し替えないと、<c>Settings.ResetToDefaults()</c> や <c>SettingsManager.MutateAndSave</c> が
/// 実ユーザーの <c>%LocalAppData%\Lhamiel</c> を書き換える。
/// </summary>
/// <remarks>
/// <c>LhaignoreFile.FilePath</c> など AppDataDirectory から派生する値は各型の初期化時に確定するため、
/// 差し替えは他の製品型に触れる前（ModuleInitializer）に行う。製品コードは変更しない。
/// </remarks>
internal static class TestEnvironmentIsolation
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    private static extern ref string AppDataDirectoryField(Settings? owner);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    private static extern ref string SettingsFilePathField(Settings? owner);

    internal static string IsolatedDirectory { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        SweepStaleDirectories();
        // 名前に PID を含め、後続の実行が「所有プロセスが生きていない」フォルダーだけを判定して片付けられるようにする。
        var root = Path.Combine(
            Path.GetTempPath(), $"lhamiel-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        IsolatedDirectory = root;

        // 参照が型初期化を起こすため、元の値の初期化が先に終わってから上書きする。
        AppDataDirectoryField(null) = root;
        SettingsFilePathField(null) = Path.Combine(root, "settings.json");

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // ログのファイルハンドルを先に閉じてから削除する（開いたままだと削除に失敗して残る）。
            try { Logger.Dispose(); }
            catch (Exception) { /* 閉じられなくても削除は試みる */ }
            TryDelete(root);
        };
    }

    /// <summary>
    /// 終了時の削除に失敗して残った前回分を、次の実行の開始時に片付ける。
    /// PID 付きの名前は所有プロセスが生きていないものだけ、PID の無い旧形式は 1 日以上前のものだけを消す
    /// （並行実行中の他プロセスのフォルダーには触れない）。
    /// </summary>
    private static void SweepStaleDirectories()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), "lhamiel-tests-*"))
            {
                if (IsStale(dir))
                    TryDelete(dir);
            }
        }
        catch (Exception) { /* 片付けは best-effort */ }
    }

    private static bool IsStale(string dir)
    {
        var parts = Path.GetFileName(dir).Split('-');
        // lhamiel-tests-<pid>-<guid> → ["lhamiel", "tests", "<pid>", "<guid>"]
        if (parts.Length != 4 || !int.TryParse(parts[2], out var pid))
            return DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > TimeSpan.FromDays(1);

        try
        {
            using var owner = Process.GetProcessById(pid);
            // PID が再利用されていれば、所有プロセスの開始はフォルダー作成より後になる。
            return owner.StartTime.ToUniversalTime() > Directory.GetCreationTimeUtc(dir).AddSeconds(1);
        }
        catch (ArgumentException)
        {
            return true; // プロセスが存在しない
        }
        catch (Exception)
        {
            return false; // 判定できないものは消さない
        }
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception) { /* ログ等が保持中なら一時フォルダーに残す */ }
    }
}
