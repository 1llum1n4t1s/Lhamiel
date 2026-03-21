using System.Diagnostics;
namespace Lhamiel.Util;

/// <summary>
/// アプリケーション実行ファイルのパス解決を一元管理するクラス。
/// FileAssociation と ShortcutCreator で共通利用する。
/// </summary>
public static class AppPathResolver
{
    private static readonly Lazy<string> _executablePath = new(Resolve);

    /// <summary>
    /// アプリケーション実行ファイルのパスを取得する（キャッシュ済み）
    /// </summary>
    public static string ExecutablePath => _executablePath.Value;

    private static string Resolve()
    {
        try
        {
            // Environment.ProcessPath（単一ファイル公開・Native AOT対応）
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                return processPath;

            // フォールバック: Process.GetCurrentProcess().MainModule.FileName
            using var process = Process.GetCurrentProcess();
            processPath = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                return processPath;

            // ベースディレクトリからexeファイルを探す
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var exeFiles = Directory.GetFiles(baseDirectory, "*.exe");
            if (exeFiles.Length > 0)
            {
                // Lhamiel.exe を優先
                var mainExe = exeFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals("Lhamiel.exe", StringComparison.OrdinalIgnoreCase));
                return mainExe ?? exeFiles[0];
            }

            // 最終フォールバック: AppContext.BaseDirectory + Lhamiel.exe
            var assemblyPath = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                var exePath = Path.Combine(assemblyPath.TrimEnd(Path.DirectorySeparatorChar), "Lhamiel.exe");
                if (File.Exists(exePath))
                    return exePath;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogException("実行ファイルパスの取得に失敗しました", ex);
            return string.Empty;
        }
    }
}
