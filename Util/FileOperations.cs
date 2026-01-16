using System.IO;

namespace Lhamiel.Util;

/// <summary>
/// ファイル操作を集約するヘルパー
/// </summary>
internal static class FileOperations
{
    /// <summary>
    /// 一時展開した内容からファイルをコピーする
    /// </summary>
    public static void CopyExtractedItem(string tempPath, string outputPath, string fullName, bool isDirectory)
    {
        var sourcePath = Path.Combine(tempPath, fullName);
        var targetPath = Path.Combine(outputPath, fullName);

        if (isDirectory)
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("展開されたファイルが見つかりません。", sourcePath);
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.Copy(sourcePath, targetPath, true);
    }

    /// <summary>
    /// 一時ディレクトリを削除する
    /// </summary>
    public static void CleanupTemporaryPath(string tempPath, Action<string>? logWarning = null)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
        catch (Exception ex)
        {
            logWarning?.Invoke($"一時ディレクトリ削除に失敗しました: {tempPath}, {ex.Message}");
        }
    }
}
