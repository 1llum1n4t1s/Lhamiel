using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;

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
    /// 引数を既存のインスタンスに送信する
    /// </summary>
    /// <param name="args">送信するコマンドライン引数</param>
    /// <returns>送信に成功した場合は true</returns>
    public static async Task<bool> SendArgsToExistingInstanceAsync(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            // 既存のインスタンスが接続待ちになるまで少し待機
            await client.ConnectAsync(1000);

            var json = JsonConvert.SerializeObject(args);
            var buffer = Encoding.UTF8.GetBytes(json);

            await client.WriteAsync(buffer, 0, buffer.Length);

            // 書き込み完了を確実にする
            client.WaitForPipeDrain();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"IPC引数送信エラー: {ex.Message}");
            return false;
        }
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
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // 接続を待機
                await server.WaitForConnectionAsync(cancellationToken);

                using (var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true))
                {
                    var json = await reader.ReadToEndAsync(cancellationToken);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var args = JsonConvert.DeserializeObject<string[]>(json);
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
