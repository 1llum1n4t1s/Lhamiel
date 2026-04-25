using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace Lhamiel.Util;

/// <summary>
/// アプリケーション間通信（IPC）を制御するサービス
/// </summary>
public static class IpcService
{
    /// <summary>
    /// パイプ名（アプリケーションごとに一意）
    /// </summary>
    private const string PipeName = "Lhamiel_IpcPipe_SingleInstance";

    /// <summary>
    /// クライアント接続の総タイムアウト（ミリ秒）。
    /// サーバー側がパイプ再生成中の瞬間に接続が拒否されるケースに備えてリトライする。
    /// </summary>
    private const int ConnectTotalTimeoutMs = 2000;

    /// <summary>
    /// 単一接続試行のタイムアウト（ミリ秒）
    /// </summary>
    private const int ConnectAttemptTimeoutMs = 500;

    /// <summary>
    /// パイプ排出待ち（WaitForPipeDrain）のタイムアウト（ミリ秒）。
    /// 受信側（既存インスタンス）がハングしている場合に第 2 インスタンスの起動 UI が
    /// 無期限ブロックされるのを防ぐ。ローカル Named Pipe の drain は通常即時完了する。
    /// </summary>
    private const int PipeDrainTimeoutMs = 1000;

    /// <summary>
    /// サーバー側リクエスト読み取りタイムアウト（ミリ秒）。
    /// クライアントが接続だけして送信せず居座るケースで、IPC サーバーが他の起動を
    /// 受け付けられなくなるのを防ぐ。
    /// </summary>
    private const int RequestReadTimeoutMs = 3000;

    /// <summary>
    /// 引数を既存のインスタンスに送信する。
    /// 接続失敗時は少し待ってリトライする（サーバー側のパイプ再生成の隙間に落ちるのを避けるため）。
    /// </summary>
    /// <param name="args">送信するコマンドライン引数</param>
    /// <param name="cancellationToken">
    /// 呼び出し側の Cancellation Token。アプリ終了時等の早期打ち切りに使う。
    /// 省略時は <see cref="CancellationToken.None"/> で、<see cref="ConnectTotalTimeoutMs"/> の
    /// タイムアウトのみで制御される。
    /// </param>
    /// <returns>送信に成功した場合は true</returns>
    public static async Task<bool> SendArgsToExistingInstanceAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var startedAt = Environment.TickCount64;
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested &&
               Environment.TickCount64 - startedAt < ConnectTotalTimeoutMs)
        {
            attempt++;
            try
            {
                // PipeOptions.CurrentUserOnly: 同名のパイプを別ユーザーが先回りして
                // 作成していた場合の接続・書き込みを拒否する（悪意ある待ち伏せへの保険）。
                using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out,
                    PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(ConnectAttemptTimeoutMs, cancellationToken);

                var json = JsonSerializer.Serialize(args, AppJsonContext.Default.StringArray);
                var buffer = Encoding.UTF8.GetBytes(json);

                await client.WriteAsync(buffer, cancellationToken);

                // FlushAsync でキャンセルトークンを伝搬させたうえで、
                // WaitForPipeDrain でパイプ他端が受信完了するまで同期待機。
                // 通常はローカル Named Pipe なので即時返るが、対向側（既存インスタンス）が
                // ハングしているケースに備えて Task.Run でバックグラウンドへオフロード + WaitAsync で
                // PipeDrainTimeoutMs の上限を設ける。タイムアウトしても送信自体は完了済みなので
                // 起動を継続して問題ない（既存インスタンスのフォアグラウンド化は失敗扱い）。
                await client.FlushAsync(cancellationToken);
                try
                {
                    await Task.Run(client.WaitForPipeDrain, cancellationToken)
                        .WaitAsync(TimeSpan.FromMilliseconds(PipeDrainTimeoutMs), cancellationToken);
                }
                catch (TimeoutException)
                {
                    Logger.Log(
                        $"IPC WaitForPipeDrain が {PipeDrainTimeoutMs}ms でタイムアウト（受信側ハング？）。送信は完了済みのため起動継続。",
                        LogLevel.Warning);
                }
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 呼び出し側からの明示キャンセル。リトライせず即終了。
                Logger.Log("IPC引数送信がキャンセルされました", LogLevel.Debug);
                return false;
            }
            catch (TimeoutException)
            {
                // サーバー再生成の瞬間に落ちた可能性があるのでリトライ
                try { await Task.Delay(50, cancellationToken); }
                catch (OperationCanceledException) { return false; }
            }
            catch (IOException ex)
            {
                // broken pipe / 接続拒否 / サーバー再生成の隙間などは
                // ConnectTotalTimeoutMs の窓が残っている限りリトライし続ける。
                // 旧実装は attempt < 5 の when ガードで 5 回目以降を generic catch に
                // 落として即 false を返しており、実質 ~200ms で打ち切られていた。
                Logger.Log($"IPC送信リトライ({attempt}回目): {ex.Message}");
                try { await Task.Delay(50, cancellationToken); }
                catch (OperationCanceledException) { return false; }
            }
            catch (Exception ex)
            {
                // IOException 以外の例外（シリアライズ失敗等）はリトライしても解決しないため即失敗
                Logger.Log($"IPC引数送信エラー: {ex.Message}");
                return false;
            }
        }

        if (cancellationToken.IsCancellationRequested)
            Logger.Log("IPC引数送信がキャンセルされました（ループ離脱時）", LogLevel.Debug);
        else
            Logger.Log("IPC引数送信に失敗しました（タイムアウト到達）", LogLevel.Warning);
        return false;
    }

    /// <summary>
    /// IPCサーバーを開始し、引数の受信を待機する
    /// </summary>
    /// <param name="onArgsReceived">引数を受信したときに呼び出されるアクション</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public static async Task StartServerAsync(Action<string[]> onArgsReceived, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // PipeOptions.CurrentUserOnly: 同一ログオンユーザーのプロセスからの接続のみ許可
                // （悪意ある別ユーザープロセスや低権限アカウントからのコマンド注入を防ぐ）
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                // 接続を待機
                await server.WaitForConnectionAsync(cancellationToken);

                // PipeOptions.CurrentUserOnly で外部ユーザーの攻撃は防げるが、同一ユーザーの
                // バグったプロセスや誤動作による大量送信でメモリ枯渇するのを防ぐため、
                // 読み取りサイズに上限を設ける。コマンドライン引数の JSON は通常数 KB で収まる。
                // 1MB は LOH（>= 85,000 bytes）に直接配置されるため、`new byte[]` ではなく
                // ArrayPool 経由でレンタルし、断片化と GC 負荷を回避する。
                const int MaxJsonBytes = 1 * 1024 * 1024;
                var buffer = ArrayPool<byte>.Shared.Rent(MaxJsonBytes);
                try
                {
                    // 個別リクエスト処理は専用 try/catch で囲む。
                    // 不正な JSON / Deserialize 失敗 / onArgsReceived ハンドラ内例外などが
                    // 外側 catch まで伝搬すると、構造的エラー（パイプ破損等）と同じく 100ms 待機経路に
                    // 落ちて応答性が悪化するため、リクエスト単位で握って次接続待ちに進む。
                    //
                    // 読み取りタイムアウト: クライアントが接続だけして送信しない（ハング / 悪意）
                    // ケースに備え、外側 cancellationToken に RequestReadTimeoutMs を上乗せした
                    // linked CTS を ReadAsync に渡す。タイムアウトしたリクエストは握って次接続へ進む。
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(RequestReadTimeoutMs);
                    try
                    {
                        var totalRead = 0;
                        while (totalRead < MaxJsonBytes)
                        {
                            var n = await server.ReadAsync(buffer.AsMemory(totalRead, MaxJsonBytes - totalRead), readCts.Token);
                            if (n == 0) break; // EOF
                            totalRead += n;
                        }

                        // ReadOnlySpan<byte> オーバーロードに直接渡すことで、
                        // 最大 1MB に達しうる中間 string アロケーションを回避する。
                        // System.Text.Json は UTF-8 バイト列を直接デシリアライズ可能。
                        if (totalRead > 0)
                        {
                            var args = JsonSerializer.Deserialize(buffer.AsSpan(0, totalRead), AppJsonContext.Default.StringArray);
                            if (args != null)
                            {
                                onArgsReceived(args);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // readCts のタイムアウト発火（外側 cancellationToken は未キャンセル）。
                        // 「クライアントが接続だけして送信しないハング」シナリオなので、
                        // 握って次接続待ちに進む。外側 cancellationToken のキャンセルは握らずに
                        // 外側 catch (OperationCanceledException) → break 経路に通す。
                        Logger.Log(
                            $"IPC リクエスト読み取りが {RequestReadTimeoutMs}ms でタイムアウト（送信なし）。次接続待ちに進む。",
                            LogLevel.Warning);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Logger.LogException("IPC リクエスト処理に失敗（次接続待ちに進む）", ex);
                    }
                }
                finally
                {
                    // clearArray:true → JSON にコマンドライン引数（パス等）が含まれるため
                    // 次の Rent ユーザーが残骸を読まないよう確実にゼロクリア
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }

                // クライアントが切断されるのを待つか、サーバーを再作成するためにループを回す
                if (server.IsConnected)
                {
                    server.Disconnect();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // OperationCanceledException 以外のエラーのみログ出力
                if (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"IPCサーバーエラー: {ex.Message}");
                    // エラー時は少し待機してリトライ（頻繁なリトライを防ぐ）
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
    }
}
