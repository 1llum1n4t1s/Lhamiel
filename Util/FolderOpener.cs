using System.Diagnostics;
using System.IO;

namespace Lhamiel.Util;

/// <summary>
/// フォルダをWindowsエクスプローラーで開く機能を提供するクラス
/// </summary>
public static class FolderOpener
{
    /// <summary>
    /// 指定したフォルダをWindowsエクスプローラーで開く
    /// </summary>
    /// <param name="folderPath">開くフォルダのパス</param>
    public static void OpenFolder(string folderPath)
    {
        try
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

            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folderPath
            };

            using var process = Process.Start(processInfo);
            Logger.Log($"フォルダをエクスプローラーで開きました: {folderPath}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.LogException($"フォルダを開く処理でエラーが発生しました: {folderPath}", ex);
        }
    }

    /// <summary>
    /// 指定したファイルが属するフォルダをWindowsエクスプローラーで開いて、ファイルを選択する
    /// </summary>
    /// <param name="filePath">選択するファイルのパス</param>
    public static void OpenFolderAndSelectFile(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.Log("ファイルパスが指定されていません", LogLevel.Warning);
                return;
            }

            if (!File.Exists(filePath))
            {
                Logger.Log($"指定されたファイルが見つかりません: {filePath}", LogLevel.Warning);
                return;
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select, \"{filePath}\""
            };

            using var process = Process.Start(processInfo);
            Logger.Log($"ファイルを選択した状態でエクスプローラーを開きました: {filePath}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.LogException($"ファイルを選択した状態でエクスプローラーを開く処理でエラーが発生しました: {filePath}", ex);
        }
    }
}
