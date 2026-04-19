using System.Diagnostics;
namespace Lhamiel.Util;

/// <summary>
/// フォルダをWindowsエクスプローラーで開く機能を提供するクラス
/// </summary>
public static class FolderOpener
{
    /// <summary>
    /// テスト時に Process.Start をスキップするためのフラグ（InternalsVisibleTo 経由で設定）
    /// </summary>
    internal static bool DryRun { get; set; }
    /// <summary>
    /// 展開結果のフォルダを開く。
    /// CreateArchiveNameFolder=ON + 二重ネスト防止スキップ時は、アーカイブのルートフォルダを開く。
    /// </summary>
    /// <param name="outputPath">展開先パス</param>
    /// <param name="structureInfo">アーカイブ構造情報</param>
    /// <param name="createArchiveNameFolder">展開時に使用されたフォルダ作成設定の値</param>
    public static void OpenExtractionResult(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo = null,
        bool? createArchiveNameFolder = null)
    {
        var folderToOpen = GetExtractionFolderToOpen(outputPath, structureInfo, createArchiveNameFolder);
        if (Directory.Exists(folderToOpen))
            OpenFolder(folderToOpen);
    }

    /// <summary>
    /// 展開結果として開くべきフォルダのパスを決定する。
    /// CreateArchiveNameFolder=ON + 二重ネスト防止でフォルダ作成がスキップされた場合、
    /// アーカイブのルートフォルダ（outputPath/SingleRootItemName）を返す。
    /// </summary>
    /// <remarks>
    /// 展開時に使われた設定値を最も優先する：
    /// <list type="number">
    ///   <item><description><see cref="ArchiveExtractor.ArchiveStructureInfo.CapturedCreateArchiveNameFolder"/>（展開時スナップショット）</description></item>
    ///   <item><description><paramref name="createArchiveNameFolder"/>（呼び出し側が明示）</description></item>
    ///   <item><description>現在の設定値（フォールバック）</description></item>
    /// </list>
    /// この順で参照することで、展開中のユーザー設定変更に対しても
    /// 「作成したフォルダ」と「開くフォルダ」の整合性を保つ。
    /// </remarks>
    /// <param name="createArchiveNameFolder">展開時に使用された設定値。nullの場合は structureInfo か現在の設定を参照する。</param>
    internal static string GetExtractionFolderToOpen(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo,
        bool? createArchiveNameFolder = null)
    {
        var createFolder = structureInfo?.CapturedCreateArchiveNameFolder
                           ?? createArchiveNameFolder
                           ?? SettingsManager.Instance.Current.CreateArchiveNameFolder;

        if (createFolder && structureInfo is { ShouldSkipFolderCreation: true }
            && !string.IsNullOrEmpty(structureInfo.SingleRootItemName))
        {
            var archiveFolder = Path.Combine(outputPath, structureInfo.SingleRootItemName);
            if (Directory.Exists(archiveFolder))
                return archiveFolder;
        }

        return outputPath;
    }

    /// <summary>
    /// 指定したフォルダをWindowsエクスプローラーで開く
    /// </summary>
    /// <param name="folderPath">開くフォルダのパス</param>
    public static void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Logger.Log("フォルダパスが指定されていません", LogLevel.Warning);
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            Logger.Log($"指定されたフォルダが見つかりません: {folderPath}", LogLevel.Warning);
            return;
        }

        try
        {
            if (DryRun)
            {
                Logger.Log($"フォルダを開く処理をスキップしました（DryRun）: {folderPath}", LogLevel.Debug);
                return;
            }

            // ArgumentList を使うことで explorer.exe の引数が CommandLineToArgvW の規則通り
            // 安全にエスケープされ、スイッチ注入（/select,... 等）を防ぐ。
            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            processInfo.ArgumentList.Add(folderPath);

            using var process = Process.Start(processInfo);

            Logger.Log($"フォルダをエクスプローラーで開きました: {folderPath}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.LogException($"フォルダを開く処理でエラーが発生しました: {folderPath}", ex);
        }
    }
}
