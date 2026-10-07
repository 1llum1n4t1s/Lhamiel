using System.Diagnostics;
using System.Text.Json;

namespace Lhamiel.Util;

/// <summary>圧縮・展開の全受付から、入力と受付時の設定を独立プロセスへ渡す。</summary>
internal static class CompressionWorkerLauncher
{
    internal const string RequestArgument = "--compression-request";
    private const int MaxRequestBytes = ShellSelectionFile.MaxBytes * 4;
    private static readonly Lazy<CompressionWorkerRequest?> StartupRequest = new(ReadStartupRequest);
    private static readonly Lazy<ExternalCancellation> Cancellation = new(() => new(CurrentRequest?.CancellationEventName));

    internal static CompressionWorkerRequest? CurrentRequest => StartupRequest.Value;
    internal static CancellationToken WorkerCancellationToken => Cancellation.Value.Token;
    internal static void ReleaseWorkerCancellation()
    {
        if (Cancellation.IsValueCreated) Cancellation.Value.Dispose();
    }

    internal static async Task<ArchiveWorkerResult> RunAsync(string[] sourcePaths, string format, Settings settings, bool processAsBatch = false)
    {
        if (sourcePaths.Length == 0)
            return default;

        var request = new CompressionWorkerRequest
        {
            SourcePaths = sourcePaths.Select(Path.GetFullPath).ToArray(),
            CompressionFormat = format,
            Settings = settings.Snapshot(),
            // 永続設定の JsonIgnore フィールドも操作単位では保持する。
            EncryptFileNames = settings.EncryptFileNames,
            ProcessAsBatch = processAsBatch,
        };
        return ArchiveWorkerResult.FromOutcome(await RunRequestAsync(request));
    }

    internal static async Task<ArchiveWorkerResult> RunExtractionAsync(
        string[] sourcePaths, Settings settings, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        if (sourcePaths.Length == 0) return default;
        var snapshot = settings.Snapshot();
        var eventName = $"Local\\Lhamiel.ExtractionCancel-{Guid.NewGuid():N}";
        using var cancellationEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var registration = cancellationToken.Register(() => cancellationEvent.Set());
        using var slots = new SemaphoreSlim(ArchiveProgressHelper.IoBoundParallelism);
        var succeeded = 0;
        var tasks = sourcePaths.Select(async path =>
        {
            var acquired = false;
            try
            {
                await slots.WaitAsync(cancellationToken);
                acquired = true;
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await RunRequestAsync(new CompressionWorkerRequest
                {
                    SourcePaths = [Path.GetFullPath(path)],
                    CompressionFormat = "default",
                    Settings = snapshot.Snapshot(),
                    EncryptFileNames = snapshot.EncryptFileNames,
                    ProcessAsBatch = false,
                    IsExtraction = true,
                    CancellationEventName = eventName,
                });
                if (outcome == ArchiveWorkerOutcome.Succeeded)
                    progress?.Report(Interlocked.Increment(ref succeeded));
                return outcome;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ArchiveWorkerOutcome.Cancelled;
            }
            finally { if (acquired) slots.Release(); }
        }).ToArray();
        // キャンセル時も起動済みの全 worker が終了するまでイベントを保持する。
        var outcomes = await Task.WhenAll(tasks);
        var result = new ArchiveWorkerResult(
            outcomes.Count(outcome => outcome == ArchiveWorkerOutcome.Succeeded),
            outcomes.Count(outcome => outcome == ArchiveWorkerOutcome.Failed),
            outcomes.Count(outcome => outcome == ArchiveWorkerOutcome.Cancelled));
        Logger.Log($"展開 worker 終了: 成功={result.SucceededCount}, 失敗={result.FailedCount}, 取消={result.CancelledCount}, 全体取消={cancellationToken.IsCancellationRequested}");
        // 全体取消だけは既存の OCE 契約を保ち、個別取消は集計結果として返す。
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<ArchiveWorkerOutcome> RunRequestAsync(CompressionWorkerRequest request)
    {
        Validate(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, AppJsonContext.Default.CompressionWorkerRequest);
        if (bytes.Length > MaxRequestBytes)
            throw new InvalidDataException("Compression request is too large.");

        var token = Guid.NewGuid().ToString("N");
        var path = GetRequestPath(token);
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            var startInfo = new ProcessStartInfo(
                Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable."))
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(request.IsExtraction ? Program.ExtractionWorkerArgument : Program.CompressionWorkerArgument);
            startInfo.ArgumentList.Add(RequestArgument);
            startInfo.ArgumentList.Add(token);
            using var worker = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Compression worker could not be started.");
            Logger.Log($"別プロセスで{(request.IsExtraction ? "展開" : "圧縮")}処理を開始しました。PID: {worker.Id}, sourcePath={request.SourcePaths[0]}");
            // 起動元の CLI は全要求の完了まで生存する。UI は await 中も次の受付が可能。
            await worker.WaitForExitAsync();
            return ArchiveWorkerResult.FromExitCode(worker.ExitCode);
        }
        finally
        {
            // 通常は子が DeleteOnClose で消費する。起動失敗・初期化失敗時だけ回収する。
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (IOException ex) { Logger.LogException("圧縮要求の一時ファイルを回収できませんでした", ex); }
                catch (UnauthorizedAccessException ex) { Logger.LogException("圧縮要求の一時ファイルを回収できませんでした", ex); }
            }
        }
    }

    private static CompressionWorkerRequest? ReadStartupRequest()
    {
        if (!Program.IsArchiveWorker)
            return null;

        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, RequestArgument);
        // 旧版が起動する private worker 引数との互換性。通常の起動元は新しい要求だけを使う。
        if (index < 0)
            return null;
        if (index + 1 >= args.Length || Array.LastIndexOf(args, RequestArgument) != index)
            throw new InvalidDataException("Invalid compression request argument.");

        var path = GetRequestPath(args[index + 1]);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Compression request must not be a reparse point.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
            4096, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaxRequestBytes)
            throw new InvalidDataException("Invalid compression request size.");
        var request = JsonSerializer.Deserialize(stream, AppJsonContext.Default.CompressionWorkerRequest)
            ?? throw new InvalidDataException("Compression request is empty.");
        Validate(request);
        if (request.IsExtraction != Program.IsExtractionWorker)
            throw new InvalidDataException("Worker operation does not match its request.");
        request.Settings.EncryptFileNames = request.EncryptFileNames;
        return request;
    }

    private static string GetRequestPath(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _))
            throw new InvalidDataException("Invalid compression request token.");
        return Path.Combine(Path.GetTempPath(), $"Lhamiel-compression-request-{token}.json");
    }

    private static void Validate(CompressionWorkerRequest request)
    {
        if (request.Settings is null || request.SourcePaths is not { Length: > 0 }
            || request.SourcePaths.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            || (request.CompressionFormat != "default"
                && !Settings.SupportedCompressionFormats.Contains(request.CompressionFormat, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid compression request.");
        if (request.CancellationEventName is { } name
            && (!name.StartsWith("Local\\Lhamiel.ExtractionCancel-", StringComparison.Ordinal)
                || !Guid.TryParseExact(name["Local\\Lhamiel.ExtractionCancel-".Length..], "N", out _)))
            throw new InvalidDataException("Invalid extraction cancellation event.");
    }

    private sealed class ExternalCancellation : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private readonly EventWaitHandle? _event;
        private readonly RegisteredWaitHandle? _wait;
        public CancellationToken Token => _source.Token;

        public ExternalCancellation(string? name)
        {
            if (name is null) return;
            // 起動直後に親のメイン画面を閉じても、受付済みの独立展開は続ける。
            // 親が生存して全体取消を待っている間はイベントも保持される。
            if (!EventWaitHandle.TryOpenExisting(name, out _event)) return;
            _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) =>
            {
                try { _source.Cancel(); }
                catch (ObjectDisposedException) { }
            }, null, Timeout.Infinite, executeOnlyOnce: true);
        }

        public void Dispose()
        {
            _wait?.Unregister(null);
            _event?.Dispose();
            _source.Dispose();
        }
    }
}

internal sealed class CompressionWorkerRequest
{
    public required string[] SourcePaths { get; init; }
    public required string CompressionFormat { get; init; }
    public required Settings Settings { get; init; }
    public required bool EncryptFileNames { get; init; }
    public required bool ProcessAsBatch { get; init; }
    public bool IsExtraction { get; init; }
    public string? CancellationEventName { get; init; }
}
