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
                // 並列バッチでは複数アイテムのフェーズ表示が交錯するため、バー
                // (完了件数ベースの確定進捗) は維持し、フェーズテキストは通知行に流す
                // (マーキー⇔確定のバー切替が点滅して見えるのを避ける)。
                Dispatcher.UIThread.Post(() => progressWindow?.SetNotice(info.Status));
                return;
            }
            int baseline;
            lock (lockObject)
            {
                baseline = getCompletedCount();
            }
            // in-flight (処理中アイテム) の全体進捗は [0,99] に抑える (#8)。
            // baseline は完了済み件数で、報告中のアイテムが Progress<T> のスレッドプール配送
            // 遅延で「既に完了計上された後」に届くと baseline + frac が totalCount に達し、
            // overallProgress が 100 (以上) になりうる。共有 ProgressThrottler は単調増加保証のため
            // 一度 100 を報告すると以後の中間値 (1..99) を恒久ドロップする設計なので、in-flight の
            // 早期 100 を渡すとバー更新が止まる。確定 100 / 完了表示は throttler を介さない別経路
            // (完了件数ベースの UpdateProgress / SetCompleted) が駆動するため、ここでは 99 で頭打ちにする。
            var overallProgress = Math.Clamp((int)((baseline + info.Percentage / 100.0) / totalCount * 100), 0, 99);
            if (throttler.ShouldReport(overallProgress))
                Dispatcher.UIThread.Post(() => progressWindow?.UpdateProgress(overallProgress));
        });
    }
}
