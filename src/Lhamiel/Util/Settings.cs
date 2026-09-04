using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Lhamiel.Util;

/// <summary>
/// 圧縮時のディレクトリ構造モード
/// </summary>
public enum DirectoryStructureMode
{
    /// <summary>ルートディレクトリを含める（デフォルト）</summary>
    IncludeRoot,
    /// <summary>ルートディレクトリを含めない</summary>
    ExcludeRoot,
    /// <summary>ディレクトリ構造を含めない（全フラット）</summary>
    Flat
}

/// <summary>
/// アプリケーション設定を管理するクラス
/// </summary>
public class Settings
{
    internal const int MaxSourceIgnoreFileNames = 16;
    internal const int MaxSourceIgnoreFileNameLength = 255;

    private static readonly HashSet<string> ReservedSourceIgnoreFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// アプリケーションデータディレクトリ
    /// </summary>
    internal static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lhamiel");

    /// <summary>
    /// 設定ファイルのパス
    /// </summary>
    private static readonly string SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>
    /// 自動更新で許可する R2 配信元の正規 URL（悪意ある誘導を防ぐためハードコード固定）。
    /// Velopack の <see cref="Velopack.Sources.SimpleWebSource"/> がこの base URL + <c>/releases.{channel}.json</c> を
    /// 取得しに行く。末尾の "/" は付けない（Velopack 内部で正規化される）。
    /// 旧 GitHub Releases (https://github.com/1llum1n4t1s/Lhamiel) からは v1.0.168 以降で完全移行。
    /// 配信元は中立ドメイン lhamiel.kagayoi.com（旧 lhamiel.1llum1n4t1.com は配信期間が短くクリーン移行。
    /// クラウド/企業 egress の SNI フィルタで false positive を起こすため中立ドメインへ切替えた）。
    /// 超旧 GithubSource クライアント (≤v1.0.167) 救済のため、GitHub Releases に kagayoi.com 版を踏み台として
    /// publish する（GithubSource は最新版を選ぶので、それ経由で更新 → 再起動後に kagayoi.com を見るようになる）。
    /// </summary>
    internal const string CanonicalUpdateBaseUrl = "https://lhamiel.kagayoi.com";

    /// <summary>
    /// テーマ設定（"System", "Dark", "Light"）
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// ロケール設定（"ja_JP", "en_US" など。空文字列はシステム自動検出）
    /// </summary>
    public string Locale { get; set; } = "";

    /// <summary>
    /// 圧縮形式の設定
    /// </summary>
    public string CompressionFormat { get; set; } = "ZIP";

    /// <summary>
    /// File association icon variant.
    /// </summary>
    public string FileIconVariant { get; set; } = FileIconVariantClassic;

    /// <summary>
    /// ウィンドウおよびショートカットに表示するアプリアイコンのバリアント。
    /// </summary>
    public string AppIconVariant { get; set; } = AppIconVariantClassic;

    /// <summary>
    /// 展開用出力ディレクトリの設定
    /// </summary>
    public string ExtractionOutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// 圧縮用出力ディレクトリの設定
    /// </summary>
    public string CompressionOutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// 展開用出力先パターンの設定
    /// </summary>
    public bool ExtractionOutputToSameDirectory { get; set; }

    /// <summary>
    /// 圧縮用出力先パターンの設定
    /// </summary>
    public bool CompressionOutputToSameDirectory { get; set; }

    /// <summary>
    /// 自動更新の配信元 base URL（Cloudflare R2 でホスティング）。
    /// セキュリティ上の理由でハードコード固定（<see cref="CanonicalUpdateBaseUrl"/>）。
    /// settings.json から書き換えても反映されない（悪意ある第三者ホストへの誘導を防ぐため）。
    /// </summary>
    [JsonIgnore]
    public string UpdateBaseUrl => CanonicalUpdateBaseUrl;

    /// <summary>
    /// 自動更新用のチャンネル名
    /// </summary>
    public string UpdateChannel { get; set; } = "release";

    /// <summary>
    /// メイン画面起動時に Velopack 自動更新チェックを走らせるかどうか。
    /// VelopackUpdateDialog.Avalonia の <see cref="App.Check4Update(bool)"/> 自動チェック経路の ON/OFF を切り替える。
    /// 設定 UI のチェックボックスからユーザーが変更できる。デフォルトは true（バックグラウンドで起動時に確認）。
    /// </summary>
    public bool Check4UpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// ファイルの右クリックメニューに「Lhamielで展開」を表示するかどうか。
    /// </summary>
    public bool AddExtractToContextMenu { get; set; }

    /// <summary>
    /// ファイルとフォルダの右クリックメニューに「Lhamielで圧縮」を表示するかどうか。
    /// </summary>
    public bool AddCompressToContextMenu { get; set; }

    /// <summary>
    /// ユーザーが「このバージョンをスキップ」を選択した Velopack リリースタグ名（例: "v1.0.166"）。
    /// 自動更新チェック (manually=false) でこのタグ名と一致するリリースが見つかった場合はダイアログを開かない。
    /// 手動チェック (manually=true) は無視タグを無視して常に最新を表示する。
    /// 空文字列は「無視タグ未設定」を示す。
    /// </summary>
    public string IgnoreUpdateTag { get; set; } = "";

    /// <summary>
    /// 展開完了後に展開先フォルダを開くかどうか
    /// </summary>
    public bool OpenExtractionOutputFolder { get; set; } = true;

    /// <summary>
    /// アーカイブ名でフォルダを作成するかどうか（二重フォルダ防止含む）
    /// </summary>
    public bool CreateArchiveNameFolder { get; set; } = true;

    /// <summary>
    /// 圧縮完了後に圧縮先フォルダを開くかどうか
    /// </summary>
    public bool OpenCompressionOutputFolder { get; set; } = true;

    /// <summary>
    /// 複数ファイル・フォルダを1つのアーカイブにまとめて圧縮するかどうか
    /// </summary>
    public bool CompressMultipleAsOne { get; set; } = true;

    /// <summary>
    /// 圧縮時のディレクトリ構造モード
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DirectoryStructureMode>))]
    public DirectoryStructureMode DirectoryStructureMode { get; set; } = DirectoryStructureMode.IncludeRoot;

    /// <summary>
    /// 圧縮時に隠し属性・システム属性のファイルやフォルダも含めるかどうか
    /// </summary>
    public bool IncludeHiddenAndSystemEntries { get; set; } = true;

    /// <summary>
    /// レガシー JSON 設定 (≤v1.0.170) との互換性のために残す書き込み専用プロパティ。
    /// 新しい仕組みではパターンを <see cref="LhaignoreFile"/>（.lhaignore ファイル）に保存する。
    /// 旧 settings.json をデシリアライズしたときだけ <see cref="_legacyExcludedFilePatterns"/>
    /// に値を渡し、<see cref="Load"/> がそれを .lhaignore へ移行したあと破棄する。
    /// デフォルト値の管理は <see cref="LhaignoreFile.CreateDefaultContent"/> に移管済み。
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("ExcludedFilePatterns")]
    internal List<string>? ExcludedFilePatternsLegacy
    {
        // CodeRabbit 指摘対応 (Outside diff): getter を _legacyExcludedFilePatterns を返すように変更。
        // 旧来は常に null を返していたが、それだと .lhaignore 移行失敗で legacyExcludedFilePatterns を
        // 温存しても次回 Save() でその値が消える問題があった。getter から実値を返すことで、
        // 移行失敗ケースでも保険として settings.json に残り続け、復旧時に再利用できる。
        // 移行成功時は Load() 内で _legacyExcludedFilePatterns = null に明示クリアされるため、
        // 通常パスでは null を返して "ExcludedFilePatterns" を JSON に出力しない振る舞いも維持される。
        get => _legacyExcludedFilePatterns;
        set
        {
            // 旧 settings.json の `ExcludedFilePatterns` 配列を移行用にキャッシュする。
            // 空配列 `[]` も「ユーザーが意図的に除外なしにした」状態として尊重し、
            // デフォルトパターンで上書きしないように null と区別して保持する。
            // 当プロパティの setter は JsonSerializer/JsonDocument 経路の両方から呼ばれる。
            if (value is not null)
                _legacyExcludedFilePatterns = value;
        }
    }

    [JsonIgnore]
    internal List<string>? _legacyExcludedFilePatterns;

    /// <summary>
    /// サポートされているテーマ一覧。UI および <see cref="SanitizeAfterLoad"/> で使用。
    /// マジック文字列の散在を避けるためここに集約する。
    /// </summary>
    public static readonly string[] SupportedThemes = ["System", "Dark", "Light"];

    /// <summary>
    /// サポートされている圧縮形式の一覧
    /// </summary>
    public static readonly string[] SupportedCompressionFormats = ["ZIP", "7z", "TAR"];

    public const string FileIconVariantClassic = "Classic";
    public const string FileIconVariantFolder = "Folder";
    public const string FileIconVariantCute = "Cute";
    public const string FileIconVariantIce = "Ice";

    public static readonly string[] SupportedFileIconVariants =
    [
        FileIconVariantClassic,
        FileIconVariantFolder,
        FileIconVariantCute,
        FileIconVariantIce
    ];

    internal static string NormalizeFileIconVariant(string? variant) =>
        Array.Find(SupportedFileIconVariants, v => string.Equals(v, variant, StringComparison.OrdinalIgnoreCase))
        ?? FileIconVariantClassic;

    public const string AppIconVariantClassic = "Classic";
    public const string AppIconVariantCrystal = "Crystal";
    public const string AppIconVariantLegacy = "Legacy";

    public static readonly string[] SupportedAppIconVariants =
    [
        AppIconVariantClassic,
        AppIconVariantCrystal,
        AppIconVariantLegacy
    ];

    internal static string NormalizeAppIconVariant(string? variant) =>
        Array.Find(SupportedAppIconVariants, v => string.Equals(v, variant, StringComparison.OrdinalIgnoreCase))
        ?? AppIconVariantClassic;

    /// <summary>
    /// サポートされている自動更新チャンネルの一覧（canonical な小文字形）。
    /// 将来 stable / beta / canary 等を追加する場合はここに足すだけで SanitizeAfterLoad が追従する。
    /// </summary>
    public static readonly string[] SupportedUpdateChannels = ["release", "prerelease"];

    /// <summary>
    /// 圧縮パスワード入力モードの canonical 一覧。
    /// <c>"PromptEachTime"</c> = ドロップごとにダイアログで確認。
    /// <c>"Remember"</c> = DPAPI 暗号化して settings.json に保存し再利用。
    /// </summary>
    public static readonly string[] SupportedPasswordModes = ["PromptEachTime", "Remember"];

    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    public static readonly string[] SupportedExtractionFormats = ["ZIP", "7z", "TAR", "GZ", "BZ2", "LZMA", "XZ", "RAR", "LZH", "CAB", "ARJ", "Z"];

    /// <summary>
    /// 展開専用形式の一覧
    /// </summary>
    public static readonly string[] ExtractOnlyFormats = ["RAR", "ARJ", "Z"];

    /// <summary>
    /// 圧縮除外リストの既定値を作成する。
    /// </summary>
    /// <summary>
    /// 旧 API 互換用。新しい仕組みでは <see cref="LhaignoreFile.CreateDefaultContent"/> を直接使う。
    /// </summary>
    [Obsolete("Use LhaignoreFile.ResetToDefaults() / ReadPatterns() instead.")]
    public static List<string> CreateDefaultExcludedFilePatterns() =>
        [.. ArchiveExtractor.IgnoredSystemFiles, .. ArchiveExtractor.IgnoredSystemDirectories];

    /// <summary>
    /// ログファイルの最大サイズ (MB)
    /// </summary>
    public int LogMaxSizeMB { get; set; } = 10;

    /// <summary>
    /// ログファイルの保持日数（この日数より古いログファイルは自動削除される）
    /// </summary>
    public int LogRetentionDays { get; set; } = 7;

    /// <summary>
    /// ZIP圧縮レベルの設定
    /// </summary>
    public int ZipCompressionLevel { get; set; } = 5; // Normal

    /// <summary>
    /// 7z圧縮レベルの設定
    /// </summary>
    public int SevenZipCompressionLevel { get; set; } = 5; // Normal

    /// <summary>
    /// [Legacy / no-op] 展開後にアーカイブの CRC 整合性検証を実行するかどうか。
    /// v1.0.183 以降は CRC を展開中に 7z.dll が常時照合する (不一致は展開自体が失敗する) ため、
    /// 展開後の二度読み再検証パスは廃止され、この設定は参照されない。
    /// 既存 settings.json との互換のためプロパティのみ維持する。
    /// </summary>
    public bool VerifyAfterExtraction { get; set; } = true;

    /// <summary>
    /// ファイル名の Unicode 正規化 (NFC) を有効にするかどうか。
    /// macOS HFS+ は NFD を使用するため、macOS 作成アーカイブの展開時に有効。
    /// </summary>
    public bool NormalizeUnicodeFileNames { get; set; } = true;

    /// <summary>
    /// 展開時に元アーカイブの Mark of the Web (Zone.Identifier) を展開先ファイルに伝播するかどうか。
    /// </summary>
    public bool PropagateMarkOfTheWeb { get; set; } = true;


    /// <summary>
    /// 圧縮対象のディレクトリツリー内に <c>.gitignore</c> があれば、そのルールを <c>.lhaignore</c> と
    /// 混合して除外判定に使う。各 <c>.gitignore</c> はそれがあるディレクトリ以下にスコープされる。
    /// デフォルトは OFF（オプトイン）。
    /// </summary>
    public bool RespectNestedGitignore { get; set; } = false;

    /// <summary>
    /// <see cref="RespectNestedGitignore"/> が有効なとき、圧縮元の各ディレクトリで探す
    /// 除外ルールファイル名。上から順に確認し、最初に存在する 1 ファイルだけを使用する。
    /// 子孫では祖先と同じ候補またはより高優先の候補だけを使用し、低優先候補へは戻らない。
    /// この候補一覧自体は全圧縮に共通するグローバル設定。
    /// </summary>
    public string[] SourceIgnoreFileNames { get; set; } = CreateDefaultSourceIgnoreFileNames();

    // ──────────────────────────────────────────────────────────
    // パスワード保護（v1.0.181+）
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 圧縮アーカイブをパスワードで保護するかどうか。
    /// ON のとき ZIP=AES-256（WinZip AE-2）、7z=AES-256 で暗号化。TAR は非対応（UI でガード）。
    /// パスワード平文は <see cref="EncryptedCompressionPassword"/>（DPAPI 暗号化バイト列）のみ永続化し、
    /// 解決後の平文は <see cref="CompressionPasswordSession"/> 経由で短寿命ローカル変数に閉じる。
    /// </summary>
    public bool IsPasswordProtectionEnabled { get; set; }

    /// <summary>
    /// パスワード入力モード: <c>"PromptEachTime"</c>（ドロップごとに確認）または <c>"Remember"</c>（DPAPI で保存）。
    /// 未知値は <see cref="SanitizeAfterLoad"/> で <c>"PromptEachTime"</c> に矯正される。
    /// </summary>
    public string PasswordMode { get; set; } = "PromptEachTime";

    /// <summary>
    /// 圧縮パスワードを DPAPI（<see cref="System.Security.Cryptography.DataProtectionScope.CurrentUser"/>）で
    /// 暗号化したバイト列。<see cref="PasswordMode"/> が <c>"Remember"</c> のときだけ書き込まれ、
    /// <c>"PromptEachTime"</c> 切替で null 化される。System.Text.Json は byte[] を Base64 文字列として
    /// シリアライズする（AOT 安全）。別ユーザー / 別 PC / Windows パスワードリセット後は復号失敗 →
    /// 呼出側（<see cref="CompressionPasswordSession.TryUnprotect"/>）が null を返し、UI 側で再設定を要求する。
    /// 長さは 4096 バイト上限とし <see cref="SanitizeAfterLoad"/> でクランプ。
    /// ⚠️ 値は wholesale-replace 限定。<c>Array.Clear</c> でその場破壊しないこと（共有 byte[] 参照が
    /// 並行 <see cref="Snapshot"/> を巻き添えにする恐れがあるため）。
    /// </summary>
    public byte[]? EncryptedCompressionPassword { get; set; }

    /// <summary>
    /// 圧縮時にファイル名（アーカイブヘッダ）も暗号化するかどうか（7z の <c>he=on</c> 相当）。
    /// ZIP は仕様上ファイル名を暗号化できないので無視される。
    /// <para>
    /// この値は <c>[JsonIgnore]</c> で永続化対象外（decision #4: パスワード ON のたびに ON 強制リセット）。
    /// VM 側で <c>[ObservableProperty]</c> として保持し、ドロップ直前に
    /// <see cref="SettingsManager.Mutate"/> でこのフィールドへ同期させて Snapshot 経由で
    /// <see cref="ArchiveProcessor.TryResolveCompressionPasswordAsync"/> まで伝播させる。
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EncryptFileNames { get; set; } = true;

    /// <summary>
    /// 並列アクセスに対して安全なスナップショット（浅いコピー）を返す。
    /// 呼び出し元は処理開始時に1回だけ呼び出し、その後はスナップショットを使うことで
    /// UI スレッド側の設定変更との race を回避する。
    /// </summary>
    /// <remarks>
    /// ⚠️ 重要: <c>MemberwiseClone</c> は参照型フィールドを「参照のみ」コピーするため、
    /// 新しく <see cref="List{T}"/> / <see cref="Dictionary{TKey,TValue}"/> / その他 mutable コレクションを
    /// 追加した場合は、必ず下記に明示的な深コピーを追加すること。漏れるとバックグラウンド処理中に
    /// UI スレッド側の変更を拾ってしまい、<c>InvalidOperationException</c>（列挙中変更）の race が
    /// 発生する。値型・不変型（<see cref="string"/> / <see cref="int"/> / <see cref="bool"/> / enum）は
    /// <c>MemberwiseClone</c> で安全にコピーされるので追記不要。
    /// </remarks>
    public Settings Snapshot()
    {
        var copy = (Settings)MemberwiseClone();
        // 参照型コレクションは明示的に深コピー（新しく追加した場合は下に追記すること）
        copy.SourceIgnoreFileNames = [.. SourceIgnoreFileNames];
        // 除外パターンは .lhaignore ファイルが真の源なので Settings 上に状態は持たない。
        // EncryptedCompressionPassword (byte[]?) は wholesale-replace 規約のため参照共有で安全
        //   （Array.Clear 等で in-place 破壊しない、必ず別の byte[] を代入する。CompressionPasswordSession
        //   経由の運用でこれが保証される）。
        return copy;
    }

    /// <summary>
    /// 設定をファイルから読み込むメソッド
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load()
    {
        Settings? settings = null;
        try
        {
            // 旧パス（アプリケーション実行ディレクトリ）
            var oldSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

            // ディレクトリ作成
            if (!Directory.Exists(AppDataDirectory))
            {
                Directory.CreateDirectory(AppDataDirectory);
            }

            // 移行処理：新しい場所に設定ファイルがなく、古い場所にある場合は移動する
            if (!File.Exists(SettingsFilePath) && File.Exists(oldSettingsFilePath))
            {
                try
                {
                    File.Move(oldSettingsFilePath, SettingsFilePath);
                    // Logger.Log を使うと再帰の恐れがあるため、デバッグ出力のみ
                    Debug.WriteLine($"設定ファイルを移行しました: {oldSettingsFilePath} -> {SettingsFilePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"設定ファイルの移行に失敗しました: {ex.Message}");
                }
            }

            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                try
                {
                    settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);
                }
                catch (JsonException ex)
                {
                    // 二段階デコード: JsonSerializer が失敗しても、JsonDocument で個別プロパティを
                    // 救済できる可能性がある（1 プロパティの型不整合で全滅を防ぐ）。
                    settings = TryRecoverFromJsonDocument(json);

                    if (settings == null)
                    {
                        // JsonDocument でも回収不能 → 破損ファイル退避して全デフォルト化
                        var backupPath = $"{SettingsFilePath}.corrupt_{DateTime.Now:yyyyMMddHHmmss_fff}.bak";
                        var sanitizationSucceeded = false;
                        try
                        {
                            File.Move(SettingsFilePath, backupPath, overwrite: true);
                            sanitizationSucceeded = true;
                        }
                        catch (Exception moveEx)
                        {
                            Debug.WriteLine($"破損 settings.json の Move に失敗: {moveEx.Message}");
                            try
                            {
                                File.Delete(SettingsFilePath);
                                sanitizationSucceeded = true;
                            }
                            catch (Exception deleteEx)
                            {
                                Debug.WriteLine($"破損 settings.json の Delete に失敗: {deleteEx.Message}");
                                try
                                {
                                    // 最終フォールバックの空 JSON 上書きも atomic 書込で部分破損を防ぐ。
                                    // RTK レビュー #B1-008 対応。
                                    WriteAtomically(SettingsFilePath, "{}");
                                    sanitizationSucceeded = true;
                                }
                                catch (Exception writeEx)
                                {
                                    Debug.WriteLine($"破損 settings.json の空 JSON 上書きに失敗: {writeEx.Message}");
                                    // RTK レビュー #F-012 対応: 3 段フォールバック全失敗ケースで、Logger も
                                    // 未初期化な可能性が高いため、emergency.log に直接書く最終フォールバック。
                                    Logger.WriteEmergencyLog(
                                        $"settings.json の退避・削除・空 JSON 上書きが全て失敗。次回起動時も同じエラーが再発します。元エラー: {ex.Message}",
                                        writeEx);
                                }
                            }
                        }
                        Debug.WriteLine($"設定ファイルの解析に失敗しました（デフォルトに戻します）: {ex.Message}");
                        try
                        {
                            var statusMsg = sanitizationSucceeded
                                ? $"破損ファイルは {backupPath} に退避（または空 JSON 化）済みです"
                                : $"破損ファイルの退避に失敗しました（次回起動時も同じエラーが再発する可能性があります）";
                            Logger.Log(
                                $"settings.json の解析に失敗したためデフォルトに戻しました。{statusMsg}。理由: {ex.Message}",
                                LogLevel.Warning);
                        }
                        catch { /* Logger 未初期化のケース */ }
                    }
                    else
                    {
                        try
                        {
                            Logger.Log(
                                $"settings.json の一部プロパティに不整合がありましたが、有効なプロパティは回収しました。理由: {ex.Message}",
                                LogLevel.Warning);
                        }
                        catch { /* Logger 未初期化のケース */ }
                    }
                }

                if (settings != null)
                {
                    var migratedLegacyContextMenu = MigrateLegacyContextMenuSettings(settings, json);
                    settings.SanitizeAfterLoad();
                    if (migratedLegacyContextMenu)
                    {
                        try
                        {
                            // 旧キーを新しい2キーへ置き換え、ヘッドレス起動だけでも移行を完了させる。
                            settings.Save();
                        }
                        catch (Exception migrationSaveException)
                        {
                            // 値はメモリ上で移行済みなので起動は継続し、次回の通常保存で再試行する。
                            Debug.WriteLine($"右クリックメニュー設定の移行保存に失敗しました: {migrationSaveException.Message}");
                        }
                    }
                }
            }
            else
            {
                var defaultSettings = new Settings();
                defaultSettings.Save(); // 新規作成時にファイルに書き込む
                settings = defaultSettings;
            }
        }
        catch (Exception ex)
        {
            // ここも再帰回避のため Logger.Log は控える
            Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        settings ??= new Settings();

        // .lhaignore の初期化。レガシー ExcludedFilePatterns があれば移行する。
        // 既にファイルがあれば何もしないので何度呼んでも安全。
        // EnsureExists が失敗した（戻り値 false かつファイルも作成されなかった）場合は、
        // 次回 Save で旧 ExcludedFilePatterns が消えて復元不能にならないよう、レガシー値を温存する。
        if (!File.Exists(LhaignoreFile.FilePath))
        {
            var created = LhaignoreFile.EnsureExists(settings._legacyExcludedFilePatterns);
            if (created || File.Exists(LhaignoreFile.FilePath))
                settings._legacyExcludedFilePatterns = null;
        }

        return settings;
    }

    /// <summary>
    /// 単一だった旧 AddToContextMenu を展開／圧縮の独立設定へ移行する。
    /// 新キーが一部だけ存在する場合は、その明示値を維持して欠けている側だけ旧値で補う。
    /// </summary>
    internal static bool MigrateLegacyContextMenuSettings(Settings settings, string json)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetBool(root, "AddToContextMenu", out var legacyValue))
            {
                return false;
            }

            var migrated = false;
            if (!TryGetBool(root, nameof(AddExtractToContextMenu), out _))
            {
                settings.AddExtractToContextMenu = legacyValue;
                migrated = true;
            }

            if (!TryGetBool(root, nameof(AddCompressToContextMenu), out _))
            {
                settings.AddCompressToContextMenu = legacyValue;
                migrated = true;
            }

            return migrated;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Load 直後に不正値をデフォルトに戻すサニタイズ処理。
    /// 外部から書き換えられ得る settings.json に対する軽量防御として機能する。
    /// </summary>
    /// <remarks>
    /// **NOTE: 新しい列挙型・enum 系のプロパティを Settings に追加したら、
    /// 必ずこのメソッドにも allow-list 化のガードを追加すること。**
    /// 例: 文字列列挙（"foo" / "bar"）には Array.Find + canonical 正規化、
    /// enum 型には JsonStringEnumConverter の挙動を確認の上、未知値→デフォルト変換を行う。
    /// 漏れると settings.json 改竄経由で未定義値が下流に流れ込み、
    /// switch 式の default 分岐や JsonException の起動詰みに繋がる。
    /// </remarks>
    internal void SanitizeAfterLoad()
    {
        // UpdateChannel の allow-list 化: 未知の値を渡されても Velopack に無効な channel を渡さない。
        // case-insensitive で受理しつつ、下流（Velopack CLI 引数や URL パス）が
        // case-sensitive 比較を行う可能性があるため canonical な小文字に正規化する。
        // SupportedThemes / SupportedCompressionFormats と同じ Array.Find パターンに統一。
        UpdateChannel = Array.Find(SupportedUpdateChannels,
                            c => string.Equals(c, UpdateChannel, StringComparison.OrdinalIgnoreCase))
                        ?? "release";

        // Theme の allow-list 化 + canonical ケース正規化。
        // App.axaml.cs の GetThemeVariant は "Light"/"Dark"/"System" の固定文字列で switch するため、
        // "DARK" 等の入力が下流でサイレントにフォールバックしないよう SupportedThemes 側のケースに揃える。
        Theme = Array.Find(SupportedThemes, t => string.Equals(t, Theme, StringComparison.OrdinalIgnoreCase))
                ?? "System";

        // CompressionFormat も同様に canonical ケース正規化。
        CompressionFormat = Array.Find(SupportedCompressionFormats, f => string.Equals(f, CompressionFormat, StringComparison.OrdinalIgnoreCase))
                            ?? "ZIP";

        FileIconVariant = NormalizeFileIconVariant(FileIconVariant);
        AppIconVariant = NormalizeAppIconVariant(AppIconVariant);

        // 圧縮元内で探す除外ルールファイル名は単純ファイル名だけを許可する。
        // settings.json の手書き編集・破損でパスやワイルドカードが入った場合は、既存動作と
        // 互換な既定候補 `.gitignore` へ戻す。
        if (!TryNormalizeSourceIgnoreFileNames(SourceIgnoreFileNames, out var sourceIgnoreFileNames))
            sourceIgnoreFileNames = CreateDefaultSourceIgnoreFileNames();
        SourceIgnoreFileNames = sourceIgnoreFileNames;

        // IgnoreUpdateTag は VelopackUpdateDialog の VersionIgnored イベント経由でユーザーが
        // 「このバージョンをスキップ」を押した GitHub Release タグ名が保存される。
        // settings.json 直接編集や JSON null (System.Text.Json が non-nullable string に null を代入する経路)、
        // 攻撃者によるタグ名巨大化への自衛として、null → "" 正規化 + 長さ・制御文字 allow-list を適用する。
        // Velopack のタグ正規化（"v" prefix と空白の自動正規化）と整合するよう、Trim のみ追加で 'v' prefix は触らない。
        IgnoreUpdateTag ??= "";
        if (IgnoreUpdateTag.Length > 0)
        {
            // 長さ 256 文字超 or 制御文字（\0〜\x1F）を含むタグは異常値として破棄
            if (IgnoreUpdateTag.Length > 256 || IgnoreUpdateTag.AsSpan().IndexOfAnyInRange('\0', '\x1F') >= 0)
                IgnoreUpdateTag = "";
            else
                IgnoreUpdateTag = IgnoreUpdateTag.Trim();
        }

        // 出力先ディレクトリのパス妥当性チェック（存在確認 + 保護ディレクトリ除外）
        // 不正値はデスクトップにフォールバック
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!IsUsableOutputDirectory(ExtractionOutputDirectory))
            ExtractionOutputDirectory = desktop;
        if (!IsUsableOutputDirectory(CompressionOutputDirectory))
            CompressionOutputDirectory = desktop;

        // ログ容量・保持日数の Clamp（settings.json 改竄による異常値で TB 級ログ生成や
        // 未来日付化による全削除を防ぐ防御。1 MB〜200 MB、0〜365 日 に制限）。
        // RTK レビュー #F-005 対応。
        LogMaxSizeMB = Math.Clamp(LogMaxSizeMB, 1, 200);
        LogRetentionDays = Math.Clamp(LogRetentionDays, 0, 365);

        // 圧縮レベルの allow-list 化。有効値は {0,1,3,5,7,9}（UI 提示値）のみだが、
        // ZipCompressionLevel/SevenZipCompressionLevel はプレーンな int なので settings.json
        // 改竄/旧ビルド/手書きで範囲外値 (999 / -1 / 2 等) が入りうる。VM 経路は
        // OnZipCompressionLevelChanged の自己修復で矯正されるが、VM を通さない CLI/シェル
        // 関連付け圧縮経路 (App.ProcessCompression → ArchiveCompressor) には防御が無く、
        // ArchiveCompressor の (CompressionLevel)int 直キャストで未定義 enum 値が native
        // 7z.dll に渡り不透明な圧縮エラーになる。Log 容量 Clamp と同じ load 時防御として、
        // 最近傍の有効値にスナップする。
        ZipCompressionLevel = SnapToValidCompressionLevel(ZipCompressionLevel);
        SevenZipCompressionLevel = SnapToValidCompressionLevel(SevenZipCompressionLevel);

        // PasswordMode の allow-list 化（未知値は PromptEachTime に矯正）。
        PasswordMode = Array.Find(SupportedPasswordModes,
                          m => string.Equals(m, PasswordMode, StringComparison.OrdinalIgnoreCase))
                      ?? "PromptEachTime";

        // EncryptedCompressionPassword の長さ Clamp（異常巨大化を防御）。
        // DPAPI ciphertext は通常 256〜512 bytes 程度。1024 chars 上限 plaintext + DPAPI metadata でも 2KB に収まる。
        // 4096 を超えるサイズは明らかに改竄や破損。null 化して UI 側で再設定を要求する。
        if (EncryptedCompressionPassword is { Length: > 4096 })
        {
            try { Logger.Log($"EncryptedCompressionPassword が異常な長さ ({EncryptedCompressionPassword.Length} bytes) のため破棄しました。", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
            EncryptedCompressionPassword = null;
        }

        // 「TAR + 保護 ON」という矛盾状態を矯正する (codex P2 #3384524013)。
        // TAR はパスワード保護非対応で、この状態が永続層にあるとシェル/CLI 圧縮が
        // TryResolveCompressionPasswordAsync の fail-loud guard で必ず失敗する。
        // 通常は ApplySettingsToManager 側の coerce で書き込まれないが、
        // 旧ビルドが書いた settings.json や手書き編集への防御として load 時にも矯正する。
        // PasswordMode / EncryptedCompressionPassword は ZIP/7z 用の選好として保持する。
        if (IsPasswordProtectionEnabled
            && string.Equals(CompressionFormat, "TAR", StringComparison.OrdinalIgnoreCase))
        {
            IsPasswordProtectionEnabled = false;
        }

        // 「Remember + ciphertext なし」は中間状態として保持する。
        // ユーザが「保存して再利用」を選んだ直後にアプリを閉じた場合や、
        // ChangeSavedPassword から削除した直後でモード自体は Remember のままにしたい
        // という意図をくむ。TryResolveCompressionPasswordAsync 側で null ciphertext を
        // 「初回プロンプト → 保存」として正しく扱うため、ここで PromptEachTime に
        // 巻き戻すと Remember 選好が失われる (CodeRabbit/codex #3381313190)。
    }

    /// <summary>
    /// 圧縮レベルの有効値（UI 提示値）。0=無圧縮 〜 9=最大圧縮。
    /// ライブラリ (Cube.FileSystem.SevenZip) の CompressionLevel enum の定義値に対応する。
    /// MainWindowViewModel.CompressionLevels の Level と同期させること。
    /// </summary>
    internal static readonly int[] ValidCompressionLevels = [0, 1, 3, 5, 7, 9];

    /// <summary>
    /// 範囲外の圧縮レベルを最近傍の有効値 (<see cref="ValidCompressionLevels"/>) にスナップする。
    /// 同距離のときはより軽い (小さい) 圧縮レベルを選ぶ。未定義 enum 値が native 7z.dll に
    /// 渡るのを防ぐための load 時防御。
    /// </summary>
    internal static int SnapToValidCompressionLevel(int level)
    {
        // (long) キャストして距離計算する: level が int.MinValue のとき
        // `level - 0` の int 結果は int.MinValue で、Math.Abs(int.MinValue) は
        // OverflowException を投げる。改竄された settings.json を Load() の catch
        // が SanitizeAfterLoad の途中で吐き出して、せっかくの load 時防御が
        // バイパスされて (CompressionLevel)int.MinValue が native 7z.dll に届いてしまう。
        var nearest = ValidCompressionLevels[0];
        var bestDistance = Math.Abs((long)level - nearest);
        foreach (var valid in ValidCompressionLevels)
        {
            var distance = Math.Abs((long)level - valid);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = valid;
            }
        }
        return nearest;
    }

    private static bool IsUsableOutputDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            // 構文妥当性チェック: 不正文字や malformed なパスは Path.GetFullPath が例外を投げる。
            // 注意: ここで Directory.Exists は呼ばない。
            //   ユーザーが NAS / USB / リムーバブルメディアを出力先に設定しているケースで、
            //   起動時にドライブが未接続だと Directory.Exists が false を返し、SanitizeAfterLoad が
            //   サイレントに Desktop へ書き換える経路があった。その後 AutoSave 経由で settings.json も
            //   更新されると、ドライブを再接続しても元の設定が失われる「永続化破壊」を引き起こす。
            //   実際にディスクに書き込む段階（ArchiveExtractor / ArchiveCompressor）で
            //   Directory.CreateDirectory + 失敗時のフォールバック UI を出す経路があるため、
            //   起動時の sanitize では構文と保護判定のみに限定する。
            _ = Path.GetFullPath(path);

            // 注意: IsProtectedDirectory ではなく IsSystemCriticalDirectory を使う。
            // 前者は Desktop / Documents / Downloads などの一般的な出力先までブロックし、
            // ユーザーが選択した出力先設定を起動毎に Desktop へ書き換えてしまう
            // （出力先設定の永続化破壊）。実害が確実な OS 構造（Windows / Program Files /
            // System32 / ドライブルート / プロファイル根）のみを拒否する。
            if (PathValidator.IsSystemCriticalDirectory(path)) return false;
        }
        catch { return false; }
        return true;
    }

    /// <summary>
    /// 旧 API 互換用の入力正規化（Trim + 空除去 + 大小無視重複排除）。
    /// 新しい仕組みでは <see cref="LhaignoreFile"/> 経由でファイルへ書き出すため通常は不要。
    /// </summary>
    internal static List<string> NormalizeExcludedFilePatterns(IEnumerable<string> patterns)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            var normalized = pattern.Trim();
            if (normalized.Length == 0)
                continue;
            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// 圧縮元内で探す除外ルールファイル名の既定候補を返す。
    /// 配列を呼び出し毎に作成し、Settings インスタンス間の参照共有を避ける。
    /// </summary>
    public static string[] CreateDefaultSourceIgnoreFileNames() => [".gitignore"];

    /// <summary>
    /// 除外ルールファイル名候補を Trim・大小無視重複除去し、安全な単純ファイル名だけに正規化する。
    /// 空行は無視する。1 件でも不正な名前がある、候補が 0 件、上限を超える場合は false。
    /// </summary>
    internal static bool TryNormalizeSourceIgnoreFileNames(
        IEnumerable<string>? fileNames,
        out string[] normalizedFileNames)
    {
        normalizedFileNames = [];
        if (fileNames is null)
            return false;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawName in fileNames)
        {
            if (rawName is null)
                return false;

            var name = rawName.Trim();
            if (name.Length == 0)
                continue;
            if (!IsValidSourceIgnoreFileName(name))
                return false;
            if (!seen.Add(name))
                continue;

            result.Add(name);
            if (result.Count > MaxSourceIgnoreFileNames)
                return false;
        }

        if (result.Count == 0)
            return false;

        normalizedFileNames = [.. result];
        return true;
    }

    private static bool IsValidSourceIgnoreFileName(string name)
    {
        if (name.Length > MaxSourceIgnoreFileNameLength
            || name is "." or ".."
            || name.EndsWith(' ')
            || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        // Windows の予約デバイス名は拡張子の有無にかかわらず無効になる。
        // Path.GetFileNameWithoutExtension("CON.rules.txt") は "CON.rules" となるため、
        // 最初のピリオドより前を照合する（先頭がピリオドの通常の dotfile は許可）。
        var firstDot = name.IndexOf('.');
        var deviceStem = firstDot >= 0 ? name[..firstDot] : name;
        return deviceStem.Length == 0 || !ReservedSourceIgnoreFileNames.Contains(deviceStem);
    }

    /// <summary>
    /// 設定をファイルに保存するメソッド。
    /// <para>
    /// ⚠️ atomic 性: <c>File.WriteAllText</c> は OS のディスクキャッシュへの flush タイミングと
    /// プロセス強制終了 / 電源断のレースで、settings.json が 0 バイト truncate や中途半端な JSON で
    /// 残るリスクがある。AutoSave (300ms デバウンス) で頻繁に走る経路なので、
    /// <see cref="LhaignoreFile.WriteAtomically"/> と同じ「tmp + Move overwrite」パターンで
    /// 部分書き込みを排除する。RTK レビュー #B1-007 対応。
    /// </para>
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, AppJsonContext.Default.Settings);
            WriteAtomically(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(App.Text("Error.SaveSettingsFailed", ex.Message), ex);
        }
    }

    /// <summary>
    /// 内容を一時ファイルに書いてから <see cref="File.Move(string, string, bool)"/> で
    /// 上書きすることで、部分書き込みを排除する atomic 書込ヘルパ。
    /// プロセス強制終了 / 電源断のレースで対象ファイルが 0 バイト truncate されるリスクを防ぐ。
    /// 同一ボリューム上では <c>File.Move</c> は MoveFileEx の <c>MOVEFILE_REPLACE_EXISTING</c>
    /// + Rename で atomic に振る舞う（クロスボリューム時はコピー扱いで atomic 性は劣化するが、
    /// 通常 <c>%LocalAppData%\Lhamiel</c> 配下は同一ボリュームなので問題なし）。
    /// </summary>
    private static void WriteAtomically(string destinationPath, string content)
    {
        // GUID 付き一時ファイル名で並列書込時の衝突も回避する
        var tmpPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmpPath, content);
            // tmp → 本ファイルの上書き move は、AV / 検索インデクサ / バックアップが
            // settings.json を一瞬掴むと SHARING_VIOLATION / 一時 AccessDenied で散発失敗する
            // (300ms デバウンス AutoSave・Remember パスワード保存・IgnoreUpdateTag 書込が競合し
            // うる)。プロジェクト共通の LockedFileRetryPolicy (指数バックオフ) でリトライして
            // 一時ロックを乗り越える。永続エラー (ディスクフル / パス不正等) は即時打ち切りされる。
            LockedFileRetryPolicy.Execute(
                () => File.Move(tmpPath, destinationPath, overwrite: true),
                destinationPath);
        }
        catch
        {
            // tmp の後始末（best-effort）
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
            catch { /* tmp 削除失敗は無視（次回起動時の TempCleanup に任せる） */ }
            throw;
        }
    }

    /// <summary>
    /// 設定をデフォルト値にリセットする
    /// </summary>
    public void ResetToDefaults()
    {
        Theme = "System";
        CompressionFormat = "ZIP";
        FileIconVariant = FileIconVariantClassic;
        AppIconVariant = AppIconVariantClassic;
        ExtractionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        CompressionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        ExtractionOutputToSameDirectory = false;
        CompressionOutputToSameDirectory = false;
        OpenExtractionOutputFolder = true;
        OpenCompressionOutputFolder = true;
        CreateArchiveNameFolder = true;
        DirectoryStructureMode = DirectoryStructureMode.IncludeRoot;
        IncludeHiddenAndSystemEntries = true;
        UpdateChannel = "release";
        Check4UpdatesOnStartup = true;
        AddExtractToContextMenu = false;
        AddCompressToContextMenu = false;
        IgnoreUpdateTag = "";
        LogMaxSizeMB = 10;
        LogRetentionDays = 7;
        CompressMultipleAsOne = true;
        Locale = "";
        ZipCompressionLevel = 5;
        SevenZipCompressionLevel = 5;
        VerifyAfterExtraction = true;
        NormalizeUnicodeFileNames = true;
        PropagateMarkOfTheWeb = true;
        RespectNestedGitignore = false;
        SourceIgnoreFileNames = CreateDefaultSourceIgnoreFileNames();
        IsPasswordProtectionEnabled = false;
        PasswordMode = "PromptEachTime";
        EncryptedCompressionPassword = null;
        EncryptFileNames = true; // CodeRabbit #3381138447: ResetToDefaults でもデフォルトに戻す。
        // 除外パターンは .lhaignore ファイルが真の源なので、リセット時もそちらを更新する。
        LhaignoreFile.ResetToDefaults();
        // NOTE: 新しいプロパティを追加したら必ずここにも追加すること（リセット漏れ防止）
    }

    /// <summary>
    /// JsonSerializer が失敗した JSON から、JsonDocument で個別プロパティを回収する二段階デコード。
    /// JSON 自体がパースできない（構文エラー）場合は null を返す。
    /// プロパティ個別の型不整合はスキップしてデフォルト値を維持する。
    /// </summary>
    private static Settings? TryRecoverFromJsonDocument(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var s = new Settings();
            var root = doc.RootElement;
            var recoveredCount = 0;

            if (TryGetString(root, nameof(Theme), out var theme)) { s.Theme = theme!; recoveredCount++; }
            if (TryGetString(root, nameof(Locale), out var locale)) { s.Locale = locale!; recoveredCount++; }
            if (TryGetString(root, nameof(CompressionFormat), out var cf)) { s.CompressionFormat = cf!; recoveredCount++; }
            if (TryGetString(root, nameof(FileIconVariant), out var fiv)) { s.FileIconVariant = fiv!; recoveredCount++; }
            if (TryGetString(root, nameof(AppIconVariant), out var aiv)) { s.AppIconVariant = aiv!; recoveredCount++; }
            if (TryGetString(root, nameof(ExtractionOutputDirectory), out var eod)) { s.ExtractionOutputDirectory = eod!; recoveredCount++; }
            if (TryGetString(root, nameof(CompressionOutputDirectory), out var cod)) { s.CompressionOutputDirectory = cod!; recoveredCount++; }
            // UpdateBaseUrl は [JsonIgnore] ハードコード固定のため回収不要
            if (TryGetString(root, nameof(UpdateChannel), out var uc)) { s.UpdateChannel = uc!; recoveredCount++; }
            if (TryGetString(root, nameof(IgnoreUpdateTag), out var iut)) { s.IgnoreUpdateTag = iut!; recoveredCount++; }
            if (TryGetBool(root, nameof(Check4UpdatesOnStartup), out var c4uos)) { s.Check4UpdatesOnStartup = c4uos; recoveredCount++; }
            if (TryGetBool(root, nameof(AddExtractToContextMenu), out var aetcm)) { s.AddExtractToContextMenu = aetcm; recoveredCount++; }
            if (TryGetBool(root, nameof(AddCompressToContextMenu), out var actcm)) { s.AddCompressToContextMenu = actcm; recoveredCount++; }
            // 旧キーは MigrateLegacyContextMenuSettings が新2設定へ移す。回収件数には含め、
            // 他プロパティの型不整合があっても旧ユーザーの ON/OFF を失わないようにする。
            if (TryGetBool(root, "AddToContextMenu", out _)) { recoveredCount++; }

            if (TryGetBool(root, nameof(ExtractionOutputToSameDirectory), out var eotsd)) { s.ExtractionOutputToSameDirectory = eotsd; recoveredCount++; }
            if (TryGetBool(root, nameof(CompressionOutputToSameDirectory), out var cotsd)) { s.CompressionOutputToSameDirectory = cotsd; recoveredCount++; }
            if (TryGetBool(root, nameof(OpenExtractionOutputFolder), out var oeof)) { s.OpenExtractionOutputFolder = oeof; recoveredCount++; }
            if (TryGetBool(root, nameof(CreateArchiveNameFolder), out var canf)) { s.CreateArchiveNameFolder = canf; recoveredCount++; }
            if (TryGetBool(root, nameof(OpenCompressionOutputFolder), out var ocof)) { s.OpenCompressionOutputFolder = ocof; recoveredCount++; }
            if (TryGetBool(root, nameof(CompressMultipleAsOne), out var cmao)) { s.CompressMultipleAsOne = cmao; recoveredCount++; }
            if (TryGetBool(root, nameof(IncludeHiddenAndSystemEntries), out var ihase)) { s.IncludeHiddenAndSystemEntries = ihase; recoveredCount++; }
            if (TryGetBool(root, nameof(VerifyAfterExtraction), out var vae)) { s.VerifyAfterExtraction = vae; recoveredCount++; }
            if (TryGetBool(root, nameof(NormalizeUnicodeFileNames), out var nufn)) { s.NormalizeUnicodeFileNames = nufn; recoveredCount++; }
            if (TryGetBool(root, nameof(PropagateMarkOfTheWeb), out var pmotw)) { s.PropagateMarkOfTheWeb = pmotw; recoveredCount++; }
            if (TryGetBool(root, nameof(RespectNestedGitignore), out var rng)) { s.RespectNestedGitignore = rng; recoveredCount++; }
            if (TryGetStringArray(root, nameof(SourceIgnoreFileNames), out var sourceIgnoreFileNames))
            {
                s.SourceIgnoreFileNames = sourceIgnoreFileNames;
                recoveredCount++;
            }

            if (TryGetInt(root, nameof(LogMaxSizeMB), out var lms)) { s.LogMaxSizeMB = lms; recoveredCount++; }
            if (TryGetInt(root, nameof(LogRetentionDays), out var lrd)) { s.LogRetentionDays = lrd; recoveredCount++; }
            if (TryGetInt(root, nameof(ZipCompressionLevel), out var zcl)) { s.ZipCompressionLevel = zcl; recoveredCount++; }
            if (TryGetInt(root, nameof(SevenZipCompressionLevel), out var szcl)) { s.SevenZipCompressionLevel = szcl; recoveredCount++; }

            if (TryGetEnum<DirectoryStructureMode>(root, nameof(DirectoryStructureMode), out var dsm))
            {
                s.DirectoryStructureMode = dsm;
                recoveredCount++;
            }

            // パスワード保護関連の救出（v1.0.181+）。
            // 他プロパティの型不整合で stage-2 に落ちても DPAPI ciphertext が消えないよう、
            // ここで明示的に拾い上げる（critique security blocker #1 対応）。
            if (TryGetBool(root, nameof(IsPasswordProtectionEnabled), out var ipe)) { s.IsPasswordProtectionEnabled = ipe; recoveredCount++; }
            if (TryGetString(root, nameof(PasswordMode), out var pmStr)) { s.PasswordMode = pmStr!; recoveredCount++; }
            if (root.TryGetProperty(nameof(EncryptedCompressionPassword), out var ecpEl) && ecpEl.ValueKind == JsonValueKind.String)
            {
                try
                {
                    s.EncryptedCompressionPassword = ecpEl.GetBytesFromBase64();
                    recoveredCount++;
                }
                catch (FormatException) { /* Base64 破損 → null のまま（UI 側で再設定要求） */ }
                catch (InvalidOperationException) { /* ValueKind 不一致（理論上ここには来ない） */ }
            }

            // レガシー ExcludedFilePatterns 配列があれば .lhaignore 移行用にキャッシュする。
            // - 真の空配列 `[]`: ユーザーの「意図的に除外なし」を尊重して空のまま保持する
            // - 全要素が型不正（例: `[123, true]`）で文字列を 1 件も回収できなかった: 破損とみなし、
            //   デフォルトパターン（CreateDefaultContent）に温存させる
            if (root.TryGetProperty("ExcludedFilePatterns", out var efpEl) && efpEl.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var list = new List<string>();
                    var hadElements = false;
                    foreach (var item in efpEl.EnumerateArray())
                    {
                        hadElements = true;
                        if (item.ValueKind == JsonValueKind.String)
                            list.Add(item.GetString()!);
                    }
                    // 「要素はあったが 1 件も文字列が無かった」ケースは破損扱いで未回収にする。
                    // 真の空配列 (hadElements=false) は意図的設定として保持する。
                    if (!hadElements || list.Count > 0)
                    {
                        s._legacyExcludedFilePatterns = list;
                        recoveredCount++;
                    }
                }
                catch { /* 配列回収失敗 → デフォルト維持 */ }
            }

            Debug.WriteLine($"JsonDocument による個別プロパティ回収: {recoveredCount} 件");
            return recoveredCount > 0 ? s : null;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString();
            return true;
        }
        return false;
    }

    private static bool TryGetBool(JsonElement root, string name, out bool value)
    {
        value = default;
        if (root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }
        return false;
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = default;
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            return true;
        return false;
    }

    private static bool TryGetStringArray(JsonElement root, string name, out string[] value)
    {
        value = [];
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return false;

        var result = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            result.Add(item.GetString()!);
        }

        value = [.. result];
        return true;
    }

    private static bool TryGetEnum<T>(JsonElement root, string name, out T value) where T : struct, Enum
    {
        value = default;
        if (root.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.String && Enum.TryParse(el.GetString(), true, out value))
                return true;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var intVal) && Enum.IsDefined(typeof(T), intVal))
            {
                value = (T)(object)intVal;
                return true;
            }
        }
        return false;
    }

}
