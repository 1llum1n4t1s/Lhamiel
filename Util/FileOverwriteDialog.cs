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
    /// <param name="sourceFilePath">コピー元ファイルパス（存在確認用）</param>
    /// <param name="destinationPath">コピー先パス（存在確認用）</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合はデスクトップ）</param>
    /// <returns>ユーザーの選択結果</returns>
    public static OverwriteResult ShowOverwriteDialog(string sourceFilePath, string destinationPath, Window? parentWindow = null)
    {
        Logger.Log($"ShowOverwriteDialog開始: sourceFilePath={sourceFilePath}, destinationPath={destinationPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        try
        {
            var isDirectory = Directory.Exists(destinationPath);
            var isFile = File.Exists(destinationPath);

            if (!isDirectory && !isFile)
            {
                Logger.Log($"コピー先が存在しません: {destinationPath}");
                return OverwriteResult.Yes;
            }

            Logger.Log($"ShowOverwriteDialog: 出力先が既に存在します (isDirectory={isDirectory})");

            var name = Path.GetFileName(destinationPath);
            if (string.IsNullOrEmpty(name)) name = destinationPath;

            var message = isDirectory 
                ? $"フォルダ '{name}' は既に存在します。\n\n既存のフォルダを削除して上書きしますか？"
                : $"ファイル '{name}' は既に存在します。\n\n置き換えますか？";
            
            var title = isDirectory ? "フォルダの上書き確認" : "ファイルの置き換え";

            Logger.Log($"ShowOverwriteDialog: MessageBox表示開始");
            
            // 親ウィンドウがnullの場合の対策
            var result = parentWindow != null
                ? MessageBox.Show(parentWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            Logger.Log($"ShowOverwriteDialog: MessageBox結果 = {result}");

            return result switch
            {
                MessageBoxResult.Yes => OverwriteResult.Yes,
                MessageBoxResult.No => OverwriteResult.No,
                _ => OverwriteResult.Cancel
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"上書き確認ダイアログの表示に失敗しました: {ex.Message}");
            return OverwriteResult.No;
        }
    }

    /// <summary>
    /// ファイルまたはフォルダの上書き確認ダイアログを表示する（簡易版）
    /// </summary>
    /// <param name="sourceFilePath">コピー元パス</param>
    /// <param name="destinationPath">コピー先パス</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合はデスクトップ）</param>
    /// <returns>上書き可能な場合はtrue、そうでなければfalse</returns>
    public static bool CanOverwriteFile(string sourceFilePath, string destinationPath, Window? parentWindow = null)
    {
        Logger.Log($"CanOverwriteFile開始: sourceFilePath={sourceFilePath}, destinationPath={destinationPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        var result = ShowOverwriteDialog(sourceFilePath, destinationPath, parentWindow);
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
