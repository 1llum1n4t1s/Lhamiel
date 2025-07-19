using System.IO;
using System.Windows;

namespace Lhamiel.Util;

/// <summary>
/// Windows標準のファイル上書き確認ダイアログを表示するユーティリティクラス
/// </summary>
public static class FileOverwriteDialog
{
    /// <summary>
    /// ファイル上書き確認ダイアログを表示する
    /// </summary>
    /// <param name="sourceFilePath">コピー元ファイルパス</param>
    /// <param name="destinationFilePath">コピー先ファイルパス</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合はデスクトップ）</param>
    /// <returns>ユーザーの選択結果</returns>
    public static OverwriteResult ShowOverwriteDialog(string sourceFilePath, string destinationFilePath, Window? parentWindow = null)
    {
        Logger.Log($"ShowOverwriteDialog開始: sourceFilePath={sourceFilePath}, destinationFilePath={destinationFilePath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        try
        {
            Logger.Log($"ShowOverwriteDialog: ファイル存在チェック開始");
            if (!File.Exists(sourceFilePath))
            {
                Logger.Log($"コピー元ファイルが存在しません: {sourceFilePath}");
                return OverwriteResult.Cancel;
            }

            if (!File.Exists(destinationFilePath))
            {
                Logger.Log($"コピー先ファイルが存在しません: {destinationFilePath}");
                return OverwriteResult.Yes;
            }

            Logger.Log($"ShowOverwriteDialog: 両方のファイルが存在します");

            var fileName = Path.GetFileName(destinationFilePath);
            var message = $"ファイル '{fileName}' は既に存在します。\n\n上書きしますか？";
            var title = "ファイルの上書き確認";

            Logger.Log($"ShowOverwriteDialog: MessageBox表示開始");
            var result = MessageBox.Show(
                parentWindow,
                message,
                title,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            Logger.Log($"ShowOverwriteDialog: MessageBox結果 = {result}");
            
            return result switch
            {
                MessageBoxResult.Yes => OverwriteResult.Yes,
                MessageBoxResult.No => OverwriteResult.No,
                MessageBoxResult.Cancel => OverwriteResult.Cancel,
                _ => OverwriteResult.Cancel
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"ファイル上書き確認ダイアログの表示に失敗しました: {ex.Message}");
            Logger.Log($"例外の詳細: {ex}");
            return OverwriteResult.No;
        }
    }

    /// <summary>
    /// 複数ファイルの上書き確認ダイアログを表示する
    /// </summary>
    /// <param name="sourceFilePaths">コピー元ファイルパスの配列</param>
    /// <param name="destinationFolder">コピー先フォルダ</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合はデスクトップ）</param>
    /// <returns>ユーザーの選択結果</returns>
    public static OverwriteResult ShowMultipleFilesOverwriteDialog(string[] sourceFilePaths, string destinationFolder, Window? parentWindow = null)
    {
        try
        {
            Logger.Log($"ShowMultipleFilesOverwriteDialog開始: ファイル数={sourceFilePaths.Length}, destinationFolder={destinationFolder}");
            
            if (sourceFilePaths.Length == 0)
            {
                Logger.Log("競合ファイルがありません");
                return OverwriteResult.Yes;
            }

            // 複数ファイルの場合は、最初のファイルで確認ダイアログを表示
            var firstSourcePath = sourceFilePaths[0];
            var firstDestPath = firstSourcePath;

            Logger.Log($"複数ファイル上書き確認ダイアログを表示: {firstSourcePath} -> {firstDestPath}");
            var result = ShowOverwriteDialog(firstSourcePath, firstDestPath, parentWindow);
            Logger.Log($"複数ファイル上書き確認ダイアログ結果: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Log($"複数ファイル上書き確認ダイアログの表示に失敗しました: {ex.Message}");
            Logger.Log($"例外の詳細: {ex}");
            return OverwriteResult.No;
        }
    }

    /// <summary>
    /// ファイル上書き確認ダイアログを表示する（簡易版）
    /// </summary>
    /// <param name="sourceFilePath">コピー元ファイルパス</param>
    /// <param name="destinationFilePath">コピー先ファイルパス</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合はデスクトップ）</param>
    /// <returns>上書き可能な場合はtrue、そうでなければfalse</returns>
    public static bool CanOverwriteFile(string sourceFilePath, string destinationFilePath, Window? parentWindow = null)
    {
        Logger.Log($"CanOverwriteFile開始: sourceFilePath={sourceFilePath}, destinationFilePath={destinationFilePath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");
        
        // コピー先がディレクトリの場合は、ディレクトリ内のファイルとの競合を確認
        if (Directory.Exists(destinationFilePath))
        {
            Logger.Log($"コピー先がディレクトリです: {destinationFilePath}");
            // ディレクトリの場合は、常に上書きを許可（実際の競合チェックは別途行う）
            Logger.Log($"ディレクトリの場合は上書きを許可");
            return true;
        }
        
        var result = ShowOverwriteDialog(sourceFilePath, destinationFilePath, parentWindow);
        var canOverwrite = result == OverwriteResult.Yes;
        Logger.Log($"CanOverwriteFile結果: result={result}, canOverwrite={canOverwrite}");
        return canOverwrite;
    }
}

/// <summary>
/// 上書き確認ダイアログの結果
/// </summary>
public enum OverwriteResult
{
    /// <summary>
    /// はい（上書きする）
    /// </summary>
    Yes,

    /// <summary>
    /// いいえ（上書きしない）
    /// </summary>
    No,

    /// <summary>
    /// キャンセル（処理を中止）
    /// </summary>
    Cancel
}
