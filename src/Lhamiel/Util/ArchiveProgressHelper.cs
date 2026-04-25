using Avalonia.Threading;
using Lhamiel.View;
namespace Lhamiel.Util;

/// <summary>
/// 展開・圧縮処理から ProgressWindow への進捗ディスパッチを共通化するヘルパ。
/// </summary>
internal static class ArchiveProgressHelper
{
    /// <summary>
    /// I/O バウンド処理の推奨並列度。
    /// SHGetFileInfo / 7z.dll のようなディスク I/O 系は ProcessorCount に比例して並列化しても
    /// シェル / NTFS 側でキューイングされるだけなので、おおむね 2〜4 に収める。
    /// </summary>
    public static int IoBoundParallelism =>
        Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>
    /// ProgressInfo を ProgressWindow にディスパッチする。
    /// 不確定進捗はマーキー表示、確定進捗はパーセンテージ更新。
    /// </summary>
    internal static void DispatchProgress(ProgressWindow? progressWindow, ProgressInfo info)
    {
        if (info.IsIndeterminate)
            Dispatcher.UIThread.Post(() => progressWindow?.SetIndeterminate(info.Status));
        else
            Dispatcher.UIThread.Post(() => progressWindow?.UpdateProgress(info.Percentage));
    }

    /// <summary>
    /// 並列処理時の進捗マッピングを作成する。
    /// 完了済み件数をベースラインとし、処理中の個別進捗を加算して全体進捗を計算する。
    /// </summary>
    internal static IProgress<ProgressInfo> CreateMappedProgress(
        int totalCount, object lockObject, Func<int> getCompletedCount,
        ProgressWindow? progressWindow, ProgressThrottler? sharedThrottler = null)
    {
        if (totalCount == 1)
            return new Progress<ProgressInfo>(info => DispatchProgress(progressWindow, info));

        // 並列処理時は共有スロットラーで全タスク横断の UI スレッド負荷を軽減
        var throttler = sharedThrottler ?? new ProgressThrottler();

        return new Progress<ProgressInfo>(info =>
        {
            if (info.IsIndeterminate)
            {
                Dispatcher.UIThread.Post(() => progressWindow?.SetIndeterminate(info.Status));
                return;
            }
            int baseline;
            lock (lockObject)
            {
                baseline = getCompletedCount();
            }
            var overallProgress = (int)((baseline + info.Percentage / 100.0) / totalCount * 100);
            if (throttler.ShouldReport(overallProgress))
                Dispatcher.UIThread.Post(() => progressWindow?.UpdateProgress(overallProgress));
        });
    }
}
