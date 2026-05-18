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
    /// パイプ名（アプリ × セッションごとに一意）。
    /// SessionId を含めることで、同一ユーザーが console + RDP の複数セッションで同時起動した際に
    /// 各セッションが独自の IPC エンドポイントを持ち、引数 handoff が別セッションへ流れる事故を防ぐ。
    /// Mutex 側も <c>Local\</c> プレフィックスでセッションスコープ化されているので、Mutex と
    /// IPC のスコープを揃える狙いもある。
    /// </summary>
    private static readonly string PipeName =
        $"Lhamiel_IpcPipe_S{System.Diagnostics.Process.GetCurrentProcess().SessionId}";

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
    ///
    /// 制約: 必ず ConnectTotalTimeoutMs より小さい値にすること。逆転すると、
    /// 同一ユーザー権限のバグったプロセスが「接続だけして送らない」を高速ループした際、
    /// 正規の 2 番目インスタンスが ConnectTotalTimeoutMs 内にサーバーに到達できず
    /// 起動失敗する経路が成立する。
    /// </summary>
    private const int RequestReadTimeoutMs = 1500;

    /// <summary>
    /// IPC リクエストの JSON 読み取り上限（バイト）。
    ///
    /// 1MB に設定する根拠:
    ///   - ユーザーが日本語等の非 ASCII パス（UTF-8 で 1 文字 3 バイト）を持つファイルを多数選んだ場合、
    ///     JSON エスケープと UTF-8 expansion で数十〜数百 KB は容易に超える。例: 100 ファイル × 平均
    ///     パス長 100 文字（日本語）→ 30KB 以上。エクスプローラから一括ドロップで 200 ファイル超える
    ///     ケースもあり、64KB は実用上きつい。
    ///   - LOH 境界（85KB）を超えるが、IPC 送信は「2 番目のインスタンス起動時のみ」=
    ///     ユーザー操作起動の数百 ms に 1 回。ArrayPool が長期生存させて再利用するため、
    ///     LOH コストは IPC 頻度を考えれば無視できる。
    ///   - 一方、悪意ある同一ユーザープロセスからのメモリ枯渇 DoS には依然として上限としての意味があり、
    ///     1MB を超える「巨大 JSON」は確実に弾けるので攻撃面の防御は維持される。
    /// </summary>
    private const int MaxRequestJsonBytes = 1024 * 1024;

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
        // 指数バックオフ: サーバー再生成中の競合や AV/EDR の一時干渉が連続するケースで
        // 固定 50ms のスピンを避け、CPU と Named Pipe の競合を緩和する。
        // 初回 50ms → 100ms → 200ms → 400ms (上限) で、合計 750ms 程度の窓内に収まる。
        const int InitialRetryDelayMs = 50;
        const int MaxRetryDelayMs = 400;
        var retryDelayMs = InitialRetryDelayMs;
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

                // 事前 size チェック: サーバー側 MaxRequestJsonBytes を超えると
                // truncate された JSON でサーバーが Deserialize 失敗 → リクエスト破棄となる。
                // クライアント側で先に拒否することで、送信成功を返した後に handoff が消える事故を防ぐ。
                if (buffer.Length > MaxRequestJsonBytes)
                {
                    Logger.Log(
                        $"IPC 送信ペイロードが上限 {MaxRequestJsonBytes} バイトを超過 ({buffer.Length} bytes)。" +
                        "サーバー側でも破棄されるため送信せずに失敗扱いとします。",
                        LogLevel.Warning);
                    return false;
                }

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
                // サーバー再生成の瞬間に落ちた可能性があるのでリトライ（指数バックオフ）
                try { await Task.Delay(retryDelayMs, cancellationToken); }
                catch (OperationCanceledException) { return false; }
                retryDelayMs = Math.Min(retryDelayMs * 2, MaxRetryDelayMs);
            }
            catch (IOException ex)
            {
                // broken pipe / 接続拒否 / サーバー再生成の隙間などは
                // ConnectTotalTimeoutMs の窓が残っている限りリトライし続ける。
                // 旧実装は attempt < 5 の when ガードで 5 回目以降を generic catch に
                // 落として即 false を返しており、実質 ~200ms で打ち切られていた。
                Logger.Log($"IPC送信リトライ({attempt}回目): {ex.Message}");
                try { await Task.Delay(retryDelayMs, cancellationToken); }
                catch (OperationCanceledException) { return false; }
                retryDelayMs = Math.Min(retryDelayMs * 2, MaxRetryDelayMs);
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
                // クラスレベル定数 MaxRequestJsonBytes (1MB) で読み取りサイズに上限を設ける。
                // ArrayPool 経由で長期生存・再利用される。
                var buffer = ArrayPool<byte>.Shared.Rent(MaxRequestJsonBytes);
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
                        while (totalRead < MaxRequestJsonBytes)
                        {
                            var n = await server.ReadAsync(buffer.AsMemory(totalRead, MaxRequestJsonBytes - totalRead), readCts.Token);
                            if (n == 0) break; // EOF
                            totalRead += n;
                        }

                        // オーバーフロー検知:
                        // 上限に到達してループを抜けた場合、まだ送信側にデータが残っている可能性がある。
                        // そのまま Deserialize すると JSON 末尾が切れて JsonException → 接続切り直し
                        // となるが、送信側は WriteAsync 成功で true を返しているため引数 handoff が
                        // サイレントに失われる。プローブ読み取り 1 バイトで残データの有無を判定し、
                        // オーバーフロー時は明示的に警告ログを出してリクエストを破棄する。
                        var overflowed = false;
                        if (totalRead == MaxRequestJsonBytes)
                        {
                            var probeBuffer = new byte[1];
                            var probeRead = await server.ReadAsync(probeBuffer, readCts.Token);
                            if (probeRead > 0)
                            {
                                overflowed = true;
                                Logger.Log(
                                    $"IPC リクエストが上限 {MaxRequestJsonBytes} バイトを超過したためリクエストを破棄します。" +
                                    "クライアント側でも事前 size チェックを行い handoff を諦める設計になっています。",
                                    LogLevel.Warning);
                            }
                        }

                        // ReadOnlySpan<byte> オーバーロードに直接渡すことで、中間 string アロケーションを回避する。
                        // System.Text.Json は UTF-8 バイト列を直接デシリアライズ可能。
                        if (!overflowed && totalRead > 0)
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
