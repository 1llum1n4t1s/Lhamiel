namespace Lhamiel.Util;

/// <summary>
/// 進捗報告のスロットリング（UIスレッド負荷軽減用）
/// 単調増加を保証し、指定間隔以内の重複報告を抑制する。
/// 0% と 100% は常に報告される。
/// </summary>
internal sealed class ProgressThrottler
{
    private int _lastPercentage = -1;
    private long _lastReportTime;
    private readonly object _lock = new();
    private readonly int _reportIntervalMs;

    /// <param name="reportIntervalMs">報告間隔（ミリ秒）</param>
    public ProgressThrottler(int reportIntervalMs = 100)
    {
        _reportIntervalMs = reportIntervalMs;
    }

    /// <summary>
    /// 指定した進捗率を報告すべきかどうかを判定する
    /// </summary>
    /// <param name="percentage">0-100 の進捗率</param>
    /// <returns>報告すべき場合は true</returns>
    public bool ShouldReport(int percentage)
    {
        lock (_lock)
        {
            var isBoundary = percentage <= 0 || percentage >= 100;

            // 中間値は単調増加を保証し、スロットリングを適用
            if (!isBoundary)
            {
                if (percentage <= _lastPercentage) return false;
                var currentTime = Environment.TickCount64;
                if (currentTime - _lastReportTime < _reportIntervalMs) return false;
                _lastReportTime = currentTime;
            }
            else
            {
                _lastReportTime = Environment.TickCount64;
            }

            _lastPercentage = percentage;
            return true;
        }
    }
}
