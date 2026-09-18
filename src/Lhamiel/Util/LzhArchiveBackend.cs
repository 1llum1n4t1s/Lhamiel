using Kagayoi.UnLhaRe;

namespace Lhamiel.Util;

/// <summary>
/// LHA/LZH 操作を UnLhaRe の同期 API へ接続する境界。
/// テストではこの境界だけを差し替え、Lhamiel 側の一時展開・選択・進捗契約を検証する。
/// </summary>
internal interface ILzhArchiveBackend
{
    IReadOnlyList<ArchiveEntry> List(
        string archive,
        CancellationToken cancellationToken = default,
        IProgress<ArchiveProgress>? progress = null);

    void VisitEntries(
        string archive,
        Action<ArchiveEntry> visitor,
        CancellationToken cancellationToken = default,
        IProgress<ArchiveProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var entry in List(archive, cancellationToken, progress))
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(entry);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    void Extract(
        string archive,
        string destination,
        IReadOnlyList<string>? selectedNames,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);

    ArchiveCreateReport Create(
        string output,
        IReadOnlyList<ArchiveSourceEntry> entries,
        CompressionMethod method,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>アプリ全体で共有する LHA/LZH バックエンド。</summary>
internal static class LzhArchiveBackendProvider
{
    /// <summary>
    /// 大容量の通常利用を妨げず、異常なエントリ数・展開量を有限値で拒否するアプリ上限。
    /// UnLhaRe の保守的な既定値（1件256MiB・合計2GiB）はデスクトップ用途には小さいため、
    /// 呼び出し側の責任で明示的に引き上げる。
    /// </summary>
    internal static ArchiveLimits ReadLimits { get; } = new(
        MaxEntries: 1_000_000,
        MaxEntryBytes: uint.MaxValue,
        MaxTotalBytes: 256UL * 1024 * 1024 * 1024);

    /// <summary>
    /// 現行 encoder は1エントリをメモリ上で処理するため、作成時は1件256MiBに制限する。
    /// 合計上限は多数の通常ファイルを妨げない有限値として維持する。
    /// </summary>
    internal static ArchiveLimits CreateLimits { get; } = new(
        MaxEntries: 1_000_000,
        MaxEntryBytes: 256UL * 1024 * 1024,
        MaxTotalBytes: 256UL * 1024 * 1024 * 1024);

    internal static ILzhArchiveBackend Current { get; set; } = new UnLhaReArchiveBackend();
}

internal sealed class UnLhaReArchiveBackend : ILzhArchiveBackend
{
    public IReadOnlyList<ArchiveEntry> List(
        string archive,
        CancellationToken cancellationToken = default,
        IProgress<ArchiveProgress>? progress = null) =>
        ArchiveClient.List(
            archive,
            cancellationToken,
            LzhArchiveBackendProvider.ReadLimits,
            progress);

    public void VisitEntries(
        string archive,
        Action<ArchiveEntry> visitor,
        CancellationToken cancellationToken = default,
        IProgress<ArchiveProgress>? progress = null) =>
        ArchiveClient.VisitEntries(
            archive,
            visitor,
            LzhArchiveBackendProvider.ReadLimits,
            progress,
            cancellationToken);

    public void Extract(
        string archive,
        string destination,
        IReadOnlyList<string>? selectedNames,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArchiveClient.ExtractWithOptions(
            archive,
            destination,
            new ArchiveExtractOptions(PreserveTimestamps: true),
            selectedNames,
            LzhArchiveBackendProvider.ReadLimits,
            progress,
            cancellationToken);
    }

    public ArchiveCreateReport Create(
        string output,
        IReadOnlyList<ArchiveSourceEntry> entries,
        CompressionMethod method,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken) =>
        ArchiveClient.CreateWithResults(
            output,
            entries,
            method,
            new ArchiveCreateReportOptions(FailIfAllSkipped: true),
            LzhArchiveBackendProvider.CreateLimits,
            progress,
            cancellationToken);
}

/// <summary>IProgress の同期通知をそのまま呼び出し側へ渡す軽量アダプター。</summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>
/// UnLhaRe の細粒度コールバックを UI 通知向けに間引く。
/// 各フェーズの初回だけ即時に伝え、以降の反復通知をフェーズ遷移に関わらず時間制限する。
/// </summary>
internal sealed class LzhProgressThrottler(int reportIntervalMs = 100)
{
    private readonly object _lock = new();
    private readonly HashSet<ArchiveProgressPhase> _seenPhases = [];
    private long _lastReportTime;

    internal bool ShouldReport(ArchiveProgress value)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            var firstForPhase = _seenPhases.Add(value.Phase);
            if (!firstForPhase && now - _lastReportTime < reportIntervalMs)
                return false;

            _lastReportTime = now;
            return true;
        }
    }
}
