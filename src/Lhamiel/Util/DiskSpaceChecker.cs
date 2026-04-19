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
    /// 容量不足を検出した場合、CancellationTokenSource をキャンセルし、
    /// コールバックで通知する。
    /// </summary>
    /// <returns>定期チェックを停止するための IDisposable</returns>
    public static IDisposable StartPeriodicCheck(
        string outputPath, long requiredBytes,
        Window? parentWindow, CancellationTokenSource operationCts,
        Func<long, long, Task<bool>>? onInsufficientSpace = null)
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
                    // 空き容量がMinFreeSpaceThreshold未満、または処理中に残りが必要量の10%を切った場合に警告
                    if (available < MinFreeSpaceThresholdBytes || available < requiredBytes / 10)
                    {
                        Logger.Log($"定期チェックで容量不足を検出: 空き={FormatSize(available)}");

                        // 現在の 7z.dll ベースの処理では「再開」がサポートされていないため、
                        // 容量不足を検出した時点で即座にキャンセルを通知し、その後に通知ダイアログを出す。
                        // （旧実装はダイアログ表示まで Cancel を待ってしまい、ネイティブ処理が進み続けていた）
                        operationCts.Cancel();

                        if (onInsufficientSpace != null)
                        {
                            // キャンセル後の通知としてダイアログ表示（結果は無視。ユーザーには操作中断を認知させる目的）
                            _ = await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                if (parentWindow is null) return false;
                                var dialog = new View.DiskSpaceDialog(
                                    requiredBytes, available, requiredBytes - available, outputPath);
                                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                return await dialog.ShowDialog<bool>(parentWindow);
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
