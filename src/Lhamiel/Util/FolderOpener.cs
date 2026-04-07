using System.Diagnostics;
namespace Lhamiel.Util;

/// <summary>
/// フォルダをWindowsエクスプローラーで開く機能を提供するクラス
/// </summary>
public static class FolderOpener
{
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
    /// <param name="createArchiveNameFolder">展開時に使用された設定値。nullの場合は現在の設定を参照する。</param>
    internal static string GetExtractionFolderToOpen(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo,
        bool? createArchiveNameFolder = null)
    {
        var createFolder = createArchiveNameFolder ?? SettingsManager.Instance.Current.CreateArchiveNameFolder;

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
            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folderPath,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(processInfo);
            if (process != null)
            {
                process.Dispose();
            }

            Logger.Log($"フォルダをエクスプローラーで開きました: {folderPath}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.LogException($"フォルダを開く処理でエラーが発生しました: {folderPath}", ex);
        }
    }
}
