using SuperLightLogger;

namespace Lhamiel.Util;

/// <summary>
/// ログレベルを表す列挙型
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// ログ初期化設定
/// </summary>
public sealed class LoggerConfig
{
    /// <summary>ログ出力ディレクトリ</summary>
    public required string LogDirectory { get; init; }

    /// <summary>ログファイル名のプレフィックス（例: "MyApp"）</summary>
    public required string FilePrefix { get; init; }

    /// <summary>ローリングサイズ上限（MB）</summary>
    public int MaxSizeMB { get; init; } = 10;

    /// <summary>アーカイブファイルの最大保持数</summary>
    public int MaxArchiveFiles { get; init; } = 10;

    /// <summary>ログファイルの保持日数（0以下の場合は削除しない）</summary>
    public int RetentionDays { get; init; } = 7;
}

/// <summary>
/// SuperLightLoggerを使用した汎用ログ出力クラス
/// </summary>
public static class Logger
{
    private static ILog? _logger;
    private static bool _isConfigured;
    private static readonly object _initLock = new();
    private static string _appName = "App";

    // 一時的なログマスク対象トークンの集合 (defense-in-depth、v1.0.181+)。
    // 圧縮パスワードなど機密性の高い文字列を <see cref="RegisterRedactionToken"/> で登録すると、
    // <see cref="Log"/> / <see cref="LogException"/> の出力時に "***" に置換される。
    // 通常 Lhamiel のコードはパスワードを直接ログに流さない設計だが、ライブラリ例外の
    // <c>ex.Message</c> 等で偶発的に混入するリスクへの保険として用意する。
    // value は refcount: バッチで同じパスワードを複数回登録するケースに対応し、
    // 各 RedactionScope.Dispose が refcount を 1 ずつ減らして 0 で remove する
    // (codex P2 #3381085196 対応、同一 token を共有する別 scope を巻き添えにしない)。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _redactionTokens = new();

    /// <summary>
    /// 現在の処理ジョブを識別する相関 ID。<see cref="BeginScope(string)"/> で
    /// 並列圧縮/展開ジョブのログを後追跡可能にするための AsyncLocal スコープ。
    /// RTK レビュー #F-001 対応。
    /// </summary>
    private static readonly System.Threading.AsyncLocal<string?> _correlationId = new();

    /// <summary>
    /// 最小ログレベル（これ以上のレベルのログのみ出力）。
    /// RTK レビュー #F-002 対応: Release ビルドでも Info 以上を出力して、
    /// ユーザー環境で「ExtractArchiveAsync 開始」「圧縮完了」等のフロー追跡ログが
    /// 完全に消失する事態を防ぐ。Warning にしたい場合は設定 UI で切替を検討。
    /// </summary>
    private static readonly LogLevel MinLogLevel =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    /// <summary>
    /// 現在のスレッドの相関 ID を設定し、Dispose 時に元の値に戻す scope ハンドル。
    /// 並列ジョブごとに <c>using (Logger.BeginScope("Extract-" + Guid.NewGuid().ToString("N").Substring(0, 8))) {...}</c>
    /// で囲むことで、ログ末尾に <c>[id:XXXX]</c> が付き、grep で 1 ジョブのログだけを抽出できる。
    /// </summary>
    public static IDisposable BeginScope(string correlationId)
    {
        var previous = _correlationId.Value;
        _correlationId.Value = correlationId;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(string? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _correlationId.Value = previous;
        }
    }

    /// <summary>
    /// 現在の相関 ID を <c>[id:XXXX]</c> 形式で取得する（無ければ空文字）。
    /// 各 <c>WriteToLogger</c> 呼び出しでメッセージ末尾に付加する。
    /// </summary>
    private static string GetCorrelationSuffix()
    {
        var id = _correlationId.Value;
        return string.IsNullOrEmpty(id) ? string.Empty : $" [id:{id}]";
    }

    /// <summary>
    /// ユーザー名マスク用の事前コンパイル済みパターン。プロセス起動後の初回参照で 1 回だけ構築する。
    /// <para>
    /// <see cref="Environment.UserName"/> は使わない: 内部で GetUserNameExW (secur32 → LSA への
    /// RPC) を呼び、RDP セッションやドメイン環境で LSA が応答しないと**無期限ブロック**する
    /// (実機で再現・dump で確認済み: ログ 1 行ごとに呼んでいた旧実装では全ログ呼び出しスレッドが
    /// ここに吸い込まれてプロセス全体が凍結し、テストの断続的ハングの原因だった)。
    /// 代わりに環境変数 USERNAME (プロセス環境ブロックの読み取りのみ、ブロック不能) と
    /// プロファイルフォルダ名 (SHGetKnownFolderPath ベース、LSA 非経由) の両方を候補にする。
    /// アカウント名とプロファイルフォルダ名が異なるケース (アカウントリネーム等) も
    /// 両方マスクできるため、旧実装より PII カバレッジも広い。
    /// </para>
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex[] _userNameMaskPatterns = BuildUserNameMaskPatterns();

    private static System.Text.RegularExpressions.Regex[] BuildUserNameMaskPatterns()
    {
        var candidates = new List<string>(2);
        try
        {
            // ユーザー名候補は最小長ガード (MinRedactionTokenLength=4) を満たすものだけ採用する。
            // 置換は裸のユーザー名を IgnoreCase で全行から探す部分一致なので、短い語
            // ("PC" / "Dev" / "D" 等の短いユーザー名) を候補にすると、無関係な単語の一部
            // (例: "development" → "<USER>elopment") まで <USER> に潰れ、ログ・サポート ZIP の
            // 診断性が壊滅する。redaction token 側の CanRedactToken と同じ 4 文字フロアで
            // 対称化し、過剰マスクを防ぐ (4 文字未満のユーザー名はマスクせず字面のまま残すが、
            // 短語の過剰マスクの害の方が大きいため許容)。
            var envUser = Environment.GetEnvironmentVariable("USERNAME");
            if (!string.IsNullOrEmpty(envUser) && envUser.Length >= MinRedactionTokenLength)
                candidates.Add(envUser);

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var profileLeaf = Path.GetFileName(
                profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(profileLeaf) && profileLeaf.Length >= MinRedactionTokenLength &&
                !candidates.Contains(profileLeaf, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(profileLeaf);
            }
        }
        catch
        {
            // 候補が取得できなくてもログ機能自体は止めない (マスクなしで続行)
        }

        var patterns = new System.Text.RegularExpressions.Regex[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            // case-insensitive 置換（Windows のパスは大小区別なし）。毎行呼ばれるので事前コンパイル
            patterns[i] = new System.Text.RegularExpressions.Regex(
                System.Text.RegularExpressions.Regex.Escape(candidates[i]),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        return patterns;
    }

    /// <summary>
    /// ユーザー名を含むパスを <c>&lt;USER&gt;</c> プレースホルダにマスクする。
    /// <c>C:\Users\田中太郎\...</c> のような PII 露出を Logger 経由のサポート ZIP で防ぐ。
    /// RTK レビュー #F-014 対応。多言語ユーザー名にも対応するためユーザー名ベースの単純置換。
    /// </summary>
    private static string MaskUserPath(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        foreach (var pattern in _userNameMaskPatterns)
        {
            input = pattern.Replace(input, "<USER>");
        }
        return input;
    }

    /// <summary>
    /// ロガーを初期化する
    /// </summary>
    /// <param name="config">ログ設定</param>
    public static void Initialize(LoggerConfig config)
    {
        if (_isConfigured) return;

        lock (_initLock)
        {
            // ダブルチェックロッキング: 別スレッドが先に初期化を完了した場合を防止
            if (_isConfigured) return;

            _appName = config.FilePrefix;

            Directory.CreateDirectory(config.LogDirectory);

            // 文字列ベース API を使用（Lhamiel.Util.LogLevel と MEL の LogLevel の名前衝突を回避）
            LogManager.Configure(builder =>
            {
                builder.AddSuperLightFile(opt =>
                {
                    opt.FileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}.log");
                    opt.Layout = "${longdate} [${level:uppercase=true}] ${message}${onexception:inner=${newline}${exception:format=tostring}}";
                    opt.ArchiveAboveSize = (long)config.MaxSizeMB * 1024 * 1024;
                    opt.ArchiveFileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}_{{#}}.log");
                    opt.ArchiveNumbering = ArchiveNumbering.Sequence;
                    opt.MaxArchiveFiles = config.MaxArchiveFiles;
                    opt.Encoding = System.Text.Encoding.UTF8;
                    opt.MinLevelName = "Trace";
                });

                builder.SetMinimumLevel(ToLevelName(MinLogLevel));
            });

            _logger = LogManager.GetLogger(config.FilePrefix);
            _isConfigured = true;
        }

        Log("Logger initialized with SuperLightLogger (File Target)", LogLevel.Debug);

        // ファイル I/O を含むクリーンアップ処理は起動クリティカルパスから外す。
        // Task.Run で非同期化して、%LocalAppData% が OneDrive 同期や
        // Defender スキャンで遅い環境でも起動を止めない。
        var logDirectory = config.LogDirectory;
        var filePrefix = config.FilePrefix;
        var retentionDays = config.RetentionDays;
        _ = Task.Run(() =>
        {
            try
            {
                // 過去のバグで作成された不要な "0" ファイルを削除
                CleanupStaleFile(Path.Combine(logDirectory, "0"));

                // 保持期間を超えた古いログファイルを削除
                CleanupOldLogFiles(logDirectory, filePrefix, retentionDays);
            }
            catch
            {
                // ベストエフォート: 起動パスを絶対に止めない
            }
        });
    }

    /// <summary>
    /// 過去のバグで作成された不要ファイルを削除する
    /// </summary>
    /// <param name="filePath">削除対象のファイルパス</param>
    private static void CleanupStaleFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            Log($"不要なファイルを削除しました: {Path.GetFileName(filePath)}", LogLevel.Debug);
        }
        catch
        {
            // ファイルが存在しない or 削除できない場合は無視
        }
    }

    /// <summary>
    /// 保持期間を超えた古いログファイルを削除する
    /// </summary>
    /// <param name="logDirectory">ログディレクトリ</param>
    /// <param name="filePrefix">ログファイル名のプレフィックス</param>
    /// <param name="retentionDays">保持日数（0以下の場合は削除しない）</param>
    private static void CleanupOldLogFiles(string logDirectory, string filePrefix, int retentionDays)
    {
        if (retentionDays <= 0) return;

        try
        {
            var cutoffDate = DateTime.Now.Date.AddDays(-retentionDays);
            var logFiles = Directory.EnumerateFiles(logDirectory, $"{filePrefix}_*.log");

            foreach (var file in logFiles)
            {
                try
                {
                    // ファイル名から日付部分を抽出（例: Lhamiel_20260206.log or Lhamiel_20260206_000.log）
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2 && parts[1].Length == 8 &&
                        DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                    {
                        if (fileDate < cutoffDate)
                        {
                            File.Delete(file);
                            Log($"古いログファイルを削除しました: {Path.GetFileName(file)}", LogLevel.Debug);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 個別ファイルの削除失敗はログに記録して続行
                    Log($"ログファイルの削除に失敗しました: {Path.GetFileName(file)} - {ex.Message}", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"ログファイルのクリーンアップ中にエラーが発生しました: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// ログを出力する
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel)
            return;

        WriteToLogger(ApplyRedaction(message), level);
    }

    /// <summary>
    /// 一時的にログ出力からマスクすべき文字列を登録する。返り値の <see cref="IDisposable.Dispose"/> で登録解除される
    /// （<c>using</c> スコープで利用する）。
    /// </summary>
    /// <param name="token">マスク対象文字列（通常はパスワード平文）。4 文字未満・null・空文字列は no-op。</param>
    /// <returns>Dispose で登録解除される <see cref="IDisposable"/>。</returns>
    /// <remarks>
    /// <para>
    /// defense-in-depth: Lhamiel コード自体はパスワードを直接ログに流さない設計だが、ライブラリ例外メッセージや
    /// 将来の改修ミスで混入した場合の保険。性能影響は登録 token が無いとき <see cref="ConcurrentDictionary{TKey,TValue}.IsEmpty"/>
    /// による即座 return で最小化される。
    /// </para>
    /// <para>
    /// 4 文字以上の token のみ登録する (CLAUDE.md 契約 / CodeRabbit #3382682610)。
    /// 1〜3 文字を登録すると、その文字を含む通常ログやスタックトレース全体が `***` に潰れ、
    /// 障害時の診断性が大きく落ちる。3 文字以下のパスワードは暗号学的にほぼ無価値で
    /// redaction の defense-in-depth 効果も乏しいため、ログ可読性を優先して no-op にする
    /// (「短いパスワードだけ redaction されない」損失より「全ログが *** で潰れる」被害の方が遥かに大きい)。
    /// なお過去の codex #3381905948 で一旦この下限を撤去したが、ログ破壊の副作用が大きく
    /// CLAUDE.md の明文契約とも矛盾するため復元した。
    /// </para>
    /// </remarks>
    public static IDisposable RegisterRedactionToken(string? token)
    {
        if (!CanRedactToken(token))
            return NoopDisposable.Instance;
        // Refcount を 1 増やす (同一 token を別 scope が共有しても安全)。
        _redactionTokens.AddOrUpdate(token!, 1, (_, current) => current + 1);
        return new RedactionScope(token!);
    }

    /// <summary>
    /// redaction の最小トークン長。これ未満の token は over-masking でログ全体を
    /// 破壊するため <see cref="RegisterRedactionToken"/> が no-op になる (上記 remarks 参照)。
    /// </summary>
    internal const int MinRedactionTokenLength = 4;

    /// <summary>
    /// token が <see cref="RegisterRedactionToken"/> でマスク可能な長さかどうかを返す。
    /// false の場合、呼び出し側はその token を含みうるライブラリ例外メッセージ等を
    /// 生ログしないこと (型名 + HResult 等の安全な要約に置き換える契約、codex P2 #3386732834)。
    /// </summary>
    internal static bool CanRedactToken(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length >= MinRedactionTokenLength;

    /// <summary>
    /// 内部 redaction ロジック。テストから直接呼べるよう internal で公開する。
    /// 通常ロガー経路 (<see cref="Log"/> / <see cref="LogException"/>) はここを通る。
    /// </summary>
    internal static string ApplyRedaction(string message)
    {
        if (_redactionTokens.IsEmpty || string.IsNullOrEmpty(message)) return message;

        // Adversarial review (round 6): 単純な順次 Replace は、たとえ降順 length でソートしても
        // 「同長の overlapping token」(e.g. `abcd` + `cdef` が同時アクティブで message=`abcdef`) で
        // tie-breaking が不定。`abcd` を先に Replace すると "***ef" となり ef が平文で残る。
        // 解決策: 全 token の出現位置を非破壊的に bool 配列にマークし、最後にまとめて連続区間を "***" に圧縮する。
        // これにより token 順序・長さ・重複に関わらず「マッチした 1 文字でも残らない」ことを保証する。
        var tokens = new List<string>();
        foreach (var t in _redactionTokens.Keys)
        {
            if (!string.IsNullOrEmpty(t)) tokens.Add(t);
        }
        if (tokens.Count == 0) return message;

        var maskBits = new bool[message.Length];
        var anyHit = false;
        foreach (var t in tokens)
        {
            var idx = 0;
            while (idx <= message.Length - t.Length)
            {
                var hit = message.IndexOf(t, idx, StringComparison.Ordinal);
                if (hit < 0) break;
                anyHit = true;
                for (var i = hit; i < hit + t.Length; i++) maskBits[i] = true;
                // codex #3382276697: 自己 overlap する token (e.g. `aaa` in `aaaa`) を取りこぼさないよう
                // `hit + t.Length` ではなく `hit + 1` で次の検索位置を 1 文字だけ進める。
                // 既に hit 範囲は maskBits=true なので、その内側で再 hit しても結果は冪等。
                idx = hit + 1;
            }
        }
        if (!anyHit) return message;

        var sb = new System.Text.StringBuilder(message.Length);
        var pos = 0;
        while (pos < message.Length)
        {
            if (maskBits[pos])
            {
                sb.Append("***");
                while (pos < message.Length && maskBits[pos]) pos++;
            }
            else
            {
                sb.Append(message[pos]);
                pos++;
            }
        }
        return sb.ToString();
    }

    private sealed class RedactionScope(string token) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // Refcount を 1 減らす。0 になったら entry を削除する。
            // ConcurrentDictionary の TryUpdate(new, current) と
            // TryRemove(KeyValuePair) を CAS ループで使うことで refcount 更新 + remove を
            // atomic に行い、別 thread の RegisterRedactionToken と race しても安全
            // (codex P2 #3381085196)。
            while (true)
            {
                if (!_redactionTokens.TryGetValue(token, out var current)) return;
                var next = current - 1;
                if (next <= 0)
                {
                    if (_redactionTokens.TryRemove(
                            new KeyValuePair<string, int>(token, current))) return;
                }
                else
                {
                    if (_redactionTokens.TryUpdate(token, next, current)) return;
                }
                // CAS 失敗 → retry
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        internal static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// 複数行のログを出力する
    /// </summary>
    /// <param name="messages">ログメッセージの配列</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void LogLines(string[] messages, LogLevel level = LogLevel.Info)
    {
        if (messages == null || messages.Length == 0) return;

        foreach (var message in messages)
        {
            Log(message, level);
        }
    }

    /// <summary>
    /// 例外情報を含むログを出力する（常にErrorレベル）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
        // 平文 redaction → ユーザーパス mask → 相関 ID suffix の順で常に適用する。
        // CodeRabbit #3381597792: 通常経路でも redaction 経路でも MaskUserPath / GetCorrelationSuffix を
        // 統一的に通す。WriteToLogger は exception 引数を取らないので LogException 専用に同じ前処理を直接呼ぶ。
        var maskedMessage = MaskUserPath(ApplyRedaction(message)) + GetCorrelationSuffix();
        if (_logger != null)
        {
            // 例外オブジェクトをそのまま構造化ログ (_logger.Error(msg, exception)) に渡すと、
            // SuperLightLogger のレイアウト (${exception:format=tostring}) が内部で exception.ToString() を
            // MaskUserPath を通さずにレンダリングし、Message / StackTrace 経由でユーザー名 (PII) や
            // 圧縮パスワード平文が生ログ・サポート ZIP に残る (#7 / codex P2 #3381085189)。
            // redaction token の有無にかかわらず、平文化してから ApplyRedaction → MaskUserPath を通して
            // 1 行 Error で出す (構造化ログとのトレードオフだが安全側に倒す)。ToString は Message と
            // StackTrace を両方含むため、構造化経路と比べて診断情報は失われない。
            var maskedException = MaskUserPath(ApplyRedaction(exception.ToString()));
            _logger.Error($"{maskedMessage}{Environment.NewLine}{maskedException}");
            return;
        }

        // Logger 初期化前 / 初期化失敗時の緊急フォールバック。
        // Avalonia 起動失敗・LogManager.Configure 例外などでロガーが
        // 立ち上がらないケースでも例外情報を失わないよう、直接ファイルに追記する。
        // 緊急ログ側でも MaskUserPath / Redaction を再度適用するので二重マスクになるが
        // 冪等 (regex の no-op マッチ) なので問題ない。
        WriteEmergencyLog(message, exception);
    }

    /// <summary>
    /// _logger 未初期化時の緊急ログ書き込み（外部から呼ぶ用に internal で公開）。
    /// <see cref="Settings.Load"/> の 3 段フォールバックが全失敗したケース等、
    /// Logger.Log すら使えない経路でも最低限の診断情報を残すために使う。
    /// %LocalAppData%\Lhamiel\Lhamiel_emergency.log に追記する。RTK レビュー #F-012 対応。
    /// </summary>
    internal static void WriteEmergencyLog(string message, Exception exception)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _appName);
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"{_appName}_emergency.log");
            // CodeRabbit 指摘対応 (Outside diff): 緊急ログ経路でも MaskUserPath を適用する。
            // 設定破損・起動失敗時のログには StackTrace 経由でユーザー実名フォルダパスが含まれやすく、
            // 通常ロガー経路と同じ <USER> マスクを通すことでサポート ZIP の PII 露出を防ぐ。
            // ApplyRedaction を MaskUserPath より先に通す: Logger 経路と同じく
            // 圧縮パスワード等の token を "***" に置換してから PII マスクをかける
            // (codex P2 #3381085189 の緊急ログ経路も同様に保護)。
            var maskedMessage = MaskUserPath(ApplyRedaction(message)) + GetCorrelationSuffix();
            var maskedException = MaskUserPath(ApplyRedaction(exception.ToString()));
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {maskedMessage}{Environment.NewLine}{maskedException}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            // 最終フォールバック失敗時はもう諦める（ディスク満杯・権限不足など）。
        }
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する（Debugレベル）
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
        if (LogLevel.Debug < MinLogLevel) return;

        _logger?.Debug(
            $"""
            === {_appName} 起動ログ ===
            起動時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
            実行ファイルパス: {Environment.ProcessPath}
            コマンドライン引数の数: {args.Length}
            コマンドライン引数:
            {string.Join(Environment.NewLine, args.Select((a, i) => $"  [{i}]: {a}"))}
            """);
    }

    /// <summary>
    /// ロガーを明示的に終了する（バッファのフラッシュなど）
    /// </summary>
    public static void Dispose()
    {
        LogManager.Shutdown();
        _isConfigured = false;
    }

    /// <summary>
    /// SuperLightLogger の ILog にレベル別メソッドでメッセージを書き出す。
    /// 相関 ID とユーザー名マスクを自動付加する。
    /// </summary>
    private static void WriteToLogger(string message, LogLevel level)
    {
        if (_logger == null) return;

        // ユーザー名マスク + 相関 ID 付加
        var augmented = MaskUserPath(message) + GetCorrelationSuffix();

        switch (level)
        {
            case LogLevel.Debug:
                _logger.Debug(augmented);
                break;
            case LogLevel.Info:
                _logger.Info(augmented);
                break;
            case LogLevel.Warning:
                _logger.Warn(augmented);
                break;
            case LogLevel.Error:
                _logger.Error(augmented);
                break;
        }
    }

    /// <summary>
    /// 独自LogLevelをSuperLightLoggerが受け付けるレベル名に変換
    /// </summary>
    private static string ToLevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warning => "Warn",
        LogLevel.Error => "Error",
        _ => "Info"
    };
}
