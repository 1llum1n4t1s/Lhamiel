using Avalonia.Threading;
using Lhamiel.View;
namespace Lhamiel.Util;

/// <summary>
/// 展開・圧縮処理から ProgressWindow への進捗ディスパッチを共通化するヘルパ。
/// </summary>
/// <remarks>
/// /rere レビュー指摘 #24「ArchiveProcessor 責務過多」の入口対応として、
/// 進捗マッピング系の static 関数を <see cref="ArchiveProcessor"/> から分離した。
/// 将来的に ArchiveProcessor を ExtractionOrchestrator / CompressionOrchestrator に
/// 分割する際、このヘルパは両方から共有して使う。
/// </remarks>
internal static class ArchiveProgressHelper
{
    /// <summary>
    /// ProgressInfo を ProgressWindow にディスパッチする共通ヘルパー。
    /// 不確定進捗はマーキー表示、確定進捗はパーセンテージ更新。
    /// </summary>
    public static void DispatchProgress(ProgressWindow? progressWindow, ProgressInfo info)
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
    public static IProgress<ProgressInfo> CreateMappedProgress(
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
