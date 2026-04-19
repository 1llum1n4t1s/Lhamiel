using Avalonia.Controls;
using Avalonia.Threading;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// ディスク容量チェックユーティリティ。
/// 展開前/圧縮前の事前チェックと、処理中の定期チェックを提供する。
/// </summary>
public static class DiskSpaceChecker
{
    /// <summary>
    /// 定期チェックの間隔（秒）
    /// </summary>
    private const int CheckIntervalSeconds = 10;

    /// <summary>
    /// 定期チェック時の最低空き容量閾値（100MB）
    /// </summary>
    private const long MinFreeSpaceThresholdBytes = 100 * 1024 * 1024;

    /// <summary>
    /// 指定されたパスのドライブの空き容量を取得する。
    /// </summary>
    public static long GetAvailableSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return long.MaxValue;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch (Exception ex)
        {
            Logger.Log($"空き容量取得失敗: {path}, {ex.Message}");
            return long.MaxValue; // 取得失敗時はチェックをスキップ
        }
    }

    /// <summary>
    /// アーカイブ内の非圧縮サイズ合計を取得する。
    /// </summary>
    public static long GetArchiveUncompressedSize(string archivePath)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);
            return reader.Items
                .Where(item => !item.IsDirectory)
                .Sum(item => item.Length);
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブサイズ取得失敗: {archivePath}, {ex.Message}");
            return 0; // 取得失敗時はチェックをスキップ
        }
    }

    /// <summary>
    /// ソースファイル群の合計サイズを取得する（圧縮時用）。
    /// </summary>
    public static long GetTotalFileSize(IEnumerable<string> filePaths)
    {
        long total = 0;
        foreach (var path in filePaths)
        {
            try
            {
                if (File.Exists(path))
                    total += new FileInfo(path).Length;
                else if (Directory.Exists(path))
                    total += new DirectoryInfo(path)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length);
            }
            catch (Exception ex)
            {
                Logger.Log($"ファイルサイズ取得失敗: {path}, {ex.Message}");
            }
        }
        return total;
    }

    /// <summary>
    /// 容量が足りるかチェックし、不足の場合は一時停止ダイアログを表示する。
    /// ユーザーが「再開」を選び、かつ容量が確保されるまでループする。
    /// </summary>
    /// <param name="outputPath">出力先パス</param>
    /// <param name="requiredBytes">必要なバイト数</param>
    /// <param name="parentWindow">親ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>true=続行可能、false=ユーザーがキャンセル</returns>
    public static async Task<bool> EnsureDiskSpaceAsync(
        string outputPath, long requiredBytes, Window? parentWindow, CancellationToken cancellationToken)
    {
        if (requiredBytes <= 0 || parentWindow is null) return true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var available = GetAvailableSpace(outputPath);
            if (available >= requiredBytes) return true;

            var shortage = requiredBytes - available;
            Logger.Log($"容量不足: 必要={FormatSize(requiredBytes)}, 空き={FormatSize(available)}, 不足={FormatSize(shortage)}");

            // UIスレッドでダイアログ表示
            var userChoice = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new View.DiskSpaceDialog(
                    requiredBytes, available, shortage, outputPath);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                return await dialog.ShowDialog<bool>(parentWindow);
            });

            if (!userChoice)
            {
                Logger.Log("ユーザーが容量不足でキャンセル");
                return false;
            }

            // 「再開」が押された → ループ先頭で再チェック
            Logger.Log("ユーザーが再開を選択、容量を再チェック");
        }
    }

    /// <summary>
    /// 処理中の定期ディスク容量チェックを開始する。
    /// 容量不足を検出した場合、<paramref name="operationCts"/> をキャンセルし、
    /// <paramref name="parentWindow"/> が指定されていれば通知ダイアログを表示する。
    /// </summary>
    /// <remarks>
    /// 旧実装は「通知用コールバック」が渡された時のみダイアログを出す設計だったため、
    /// 呼び出し側（<see cref="ArchiveExtractor"/> 等）がコールバックを渡し忘れると
    /// 容量不足のキャンセルが UI 無しでサイレントに起きてユーザーが原因を特定できない。
    /// そこで通知の有無は <paramref name="parentWindow"/> 1 つで判定する設計に改める。
    /// </remarks>
    /// <returns>定期チェックを停止するための IDisposable</returns>
    public static IDisposable StartPeriodicCheck(
        string outputPath, long requiredBytes,
        Window? parentWindow, CancellationTokenSource operationCts)
    {
        var checkCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(checkCts.Token, operationCts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), linkedCts.Token);

                    var available = GetAvailableSpace(outputPath);
                    // 空き容量が MinFreeSpaceThreshold 未満、または処理中に残りが必要量の 10% を切った場合に警告。
                    // requiredBytes <= 0（メタデータ不明 / 空アーカイブ）の場合、相対判定は無意味で常に
                    // 条件を満たしてしまうため、絶対閾値チェックのみに限定する。
                    var relativeShortage = requiredBytes > 0 && available < requiredBytes / 10;
                    if (available < MinFreeSpaceThresholdBytes || relativeShortage)
                    {
                        Logger.Log($"定期チェックで容量不足を検出: 空き={FormatSize(available)}");

                        // 現在の 7z.dll ベースの処理では「再開」がサポートされていないため、
                        // 容量不足を検出した時点で即座にキャンセルを通知し、その後に通知ダイアログを出す。
                        // （旧実装はダイアログ表示まで Cancel を待ってしまい、ネイティブ処理が進み続けていた）
                        operationCts.Cancel();

                        if (parentWindow is not null)
                        {
                            // キャンセル後の通知としてダイアログ表示（結果は無視。ユーザーには操作中断を認知させる目的）。
                            // requiredBytes<=0（メタデータ不明アーカイブ）時は `requiredBytes - available` が
                            // 負数になってしまうので、shortage は 0 以上にクランプして UI 表示を守る。
                            // また、ダイアログの ShowDialog を await すると、この Task.Run 側が break で
                            // 抜けて periodicCheck が Dispose されたとき InvokeAsync の継続がキャンセルされて
                            // ダイアログが消えかねない。そのため通知は fire-and-forget で UI スレッドに
                            // 投げてから即 break する（ダイアログのライフサイクルは Avalonia に委ねる）。
                            var shortage = Math.Max(0, requiredBytes - available);
                            var capturedRequired = requiredBytes;
                            var capturedAvailable = available;
                            var capturedOutput = outputPath;
                            _ = Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                try
                                {
                                    var dialog = new View.DiskSpaceDialog(
                                        capturedRequired, capturedAvailable, shortage, capturedOutput);
                                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                    await dialog.ShowDialog<bool>(parentWindow);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Log($"ディスク容量通知ダイアログの表示に失敗: {ex.Message}", LogLevel.Warning);
                                }
                            });
                        }
                        Logger.Log("定期チェック: 7z.dll処理中の再開は不可のため操作をキャンセル");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Log($"定期容量チェックエラー: {ex.Message}");
            }
        }, linkedCts.Token);

        return new PeriodicCheckDisposable(checkCts, linkedCts);
    }

    /// <summary>
    /// ファイルサイズの表示用文字列
    /// </summary>
    /// <summary>
    /// ファイルサイズの表示用文字列（最小単位: KB）
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private sealed class PeriodicCheckDisposable(
        CancellationTokenSource checkCts,
        CancellationTokenSource linkedCts) : IDisposable
    {
        public void Dispose()
        {
            checkCts.Cancel();
            checkCts.Dispose();
            linkedCts.Dispose();
        }
    }
}
