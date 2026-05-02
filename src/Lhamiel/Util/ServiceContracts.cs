using Avalonia.Controls;
using Avalonia.Threading;
using Lhamiel.Models;
namespace Lhamiel.Util;

/// <summary>
/// エラー表示の抽象。テスト時はスタブに差し替え可能。
/// </summary>
internal interface IMessageService
{
    Task ShowError(string message, string? title = null);
}

/// <summary>
/// UI スレッドへのディスパッチの抽象。テスト時は同期実行するスタブに差し替え可能。
/// </summary>
internal interface IUiDispatcher
{
    void Post(Action action);
    Task<T> InvokeAsync<T>(Func<Task<T>> callback);
}

/// <summary>
/// ファイル衝突ダイアログの抽象。テスト時に結果をスタブで返せる。
/// </summary>
internal interface IConflictDialogService
{
    Task<bool> CanOverwriteFromBackgroundAsync(string sourcePath, string destinationPath, Window? parentWindow);
    Task<(FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
        ShowFromBackgroundAsync(List<FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true);
}

// --- デフォルト実装（既存の静的クラスへの薄いラッパー） ---

internal sealed class DefaultMessageService : IMessageService
{
    public Task ShowError(string message, string? title = null) => MessageService.ShowError(message, title);
}

internal sealed class DefaultUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public Task<T> InvokeAsync<T>(Func<Task<T>> callback) => Dispatcher.UIThread.InvokeAsync(callback);
}

internal sealed class DefaultConflictDialogService : IConflictDialogService
{
    public Task<bool> CanOverwriteFromBackgroundAsync(string sourcePath, string destinationPath, Window? parentWindow)
        => View.FileConflictDialog.CanOverwriteFromBackgroundAsync(sourcePath, destinationPath, parentWindow);

    public Task<(FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
        ShowFromBackgroundAsync(List<FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true)
        => View.FileConflictDialog.ShowFromBackgroundAsync(groups, parentWindow, isTwoPane);
}
