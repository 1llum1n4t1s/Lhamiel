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

                // 書き込み完了を確実にする
                client.WaitForPipeDrain();
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

                using (var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true))
                {
                    var json = await reader.ReadToEndAsync(cancellationToken);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var args = JsonSerializer.Deserialize(json, AppJsonContext.Default.StringArray);
                        if (args != null)
                        {
                            onArgsReceived(args);
                        }
                    }
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
