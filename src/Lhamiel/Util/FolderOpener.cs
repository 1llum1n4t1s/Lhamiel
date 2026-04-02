using System.Diagnostics;
namespace Lhamiel.Util;

/// <summary>
/// フォルダをWindowsエクスプローラーで開く機能を提供するクラス
/// </summary>
public static class FolderOpener
{
    /// <summary>
    /// 展開結果のフォルダを開く。常にoutputPathを開く。
    /// </summary>
    public static void OpenExtractionResult(string outputPath)
    {
        if (Directory.Exists(outputPath))
            OpenFolder(outputPath);
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
