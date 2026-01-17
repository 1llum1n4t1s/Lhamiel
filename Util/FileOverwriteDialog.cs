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
    /// <param name="parentWindow">親ウィンドウ（nullの場合は自動検索）</param>
    /// <returns>ユーザーの選択結果</returns>
    public static OverwriteResult ShowOverwriteDialog(string sourceFilePath, string destinationPath, Window? parentWindow = null)
    {
        // 親ウィンドウが未指定の場合は、現在のアクティブなウィンドウまたは最前面のウィンドウを探す
        parentWindow ??= GetBestParentWindow();

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
            
            // 親ウィンドウを指定してダイアログを表示（親がTopmostならダイアログもTopmostになる）
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
    /// <param name="parentWindow">親ウィンドウ（nullの場合は自動検索）</param>
    /// <returns>上書き可能な場合はtrue、そうでなければfalse</returns>
    public static bool CanOverwriteFile(string sourceFilePath, string destinationPath, Window? parentWindow = null)
    {
        Logger.Log($"CanOverwriteFile開始: sourceFilePath={sourceFilePath}, destinationPath={destinationPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        var result = ShowOverwriteDialog(sourceFilePath, destinationPath, parentWindow);
        var canOverwrite = result == OverwriteResult.Yes;
        Logger.Log($"CanOverwriteFile結果: result={result}, canOverwrite={canOverwrite}");
        return canOverwrite;
    }

    /// <summary>
    /// ダイアログを表示するための最適な親ウィンドウを取得する
    /// </summary>
    private static Window? GetBestParentWindow()
    {
        if (Application.Current == null) return null;

        // すでにUIスレッドにいる場合は直接実行、そうでなければInvokeしてデッドロックを防ぐ
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return GetBestParentWindowInternal();
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(GetBestParentWindowInternal);
        }
    }

    /// <summary>
    /// UIスレッド上で親ウィンドウを探索する実体
    /// </summary>
    private static Window? GetBestParentWindowInternal()
    {
        // 1. アクティブなウィンドウがあればそれを優先（ProgressWindowなど）
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (activeWindow != null) return activeWindow;

        // 2. アクティブなウィンドウがない場合は、最後に表示された表示中のウィンドウを探す
        var lastVisibleWindow = Application.Current.Windows.OfType<Window>().LastOrDefault(w => w.IsVisible);
        if (lastVisibleWindow != null) return lastVisibleWindow;

        // 3. 最後にMainWindow
        return Application.Current.MainWindow;
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
