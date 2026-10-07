namespace Lhamiel.Util;

/// <summary>表示済みの操作失敗と取消を、プロセス起動自体の異常終了から区別する。</summary>
internal enum ArchiveWorkerOutcome
{
    Succeeded = 0,
    Failed = 10,
    Cancelled = 11,
}

internal readonly record struct ArchiveWorkerResult(int SucceededCount, int FailedCount, int CancelledCount)
{
    internal ArchiveWorkerOutcome Outcome => FailedCount > 0 ? ArchiveWorkerOutcome.Failed
        : CancelledCount > 0 ? ArchiveWorkerOutcome.Cancelled : ArchiveWorkerOutcome.Succeeded;

    internal static ArchiveWorkerResult FromOutcome(ArchiveWorkerOutcome outcome) => outcome switch
    {
        ArchiveWorkerOutcome.Succeeded => new(1, 0, 0),
        ArchiveWorkerOutcome.Failed => new(0, 1, 0),
        ArchiveWorkerOutcome.Cancelled => new(0, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    internal static ArchiveWorkerOutcome FromExitCode(int exitCode) => exitCode switch
    {
        0 => ArchiveWorkerOutcome.Succeeded,
        10 => ArchiveWorkerOutcome.Failed,
        11 => ArchiveWorkerOutcome.Cancelled,
        _ => throw new InvalidOperationException($"Archive worker exited with code {exitCode}."),
    };
}
