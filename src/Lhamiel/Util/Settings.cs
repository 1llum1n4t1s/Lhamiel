using System.Diagnostics;
using System.Text;
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
    public static readonly string[] SupportedCompressionFormats = ["ZIP", "7z", "TAR", "LZH"];

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
    /// LZH 圧縮方式の設定。<c>LH0</c> は無圧縮、<c>LH5</c> / <c>LH6</c> / <c>LH7</c> は
    /// 辞書サイズが順に大きくなる LZH 圧縮方式。
    /// </summary>
    public string LzhCompressionMethod { get; set; } = "LH5";

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
    /// <paramref name="source"/>（<see cref="Snapshot"/> で取った変更前の複製）の値でこのインスタンスを巻き戻す。
    /// <see cref="SettingsManager"/> が保存の失敗・中止時に、ロック内でメモリ上の設定を変更前へ戻すために使う。
    /// 設定プロパティを追加したときは必ずここへ追記する（<c>SettingsSaveRollbackTests.MutateAndSave_WriteFails_RestoresEveryPropertyAndKeepsReference</c> が
    /// 公開プロパティを全部変えて巻き戻りを検査するため、追記漏れはそのテストが落ちて見つかる）。
    /// </summary>
    internal void CopyFrom(Settings source)
    {
        Theme = source.Theme;
        Locale = source.Locale;
        CompressionFormat = source.CompressionFormat;
        FileIconVariant = source.FileIconVariant;
        AppIconVariant = source.AppIconVariant;
        ExtractionOutputDirectory = source.ExtractionOutputDirectory;
        CompressionOutputDirectory = source.CompressionOutputDirectory;
        ExtractionOutputToSameDirectory = source.ExtractionOutputToSameDirectory;
        CompressionOutputToSameDirectory = source.CompressionOutputToSameDirectory;
        UpdateChannel = source.UpdateChannel;
        Check4UpdatesOnStartup = source.Check4UpdatesOnStartup;
        AddExtractToContextMenu = source.AddExtractToContextMenu;
        AddCompressToContextMenu = source.AddCompressToContextMenu;
        IgnoreUpdateTag = source.IgnoreUpdateTag;
        OpenExtractionOutputFolder = source.OpenExtractionOutputFolder;
        CreateArchiveNameFolder = source.CreateArchiveNameFolder;
        OpenCompressionOutputFolder = source.OpenCompressionOutputFolder;
        CompressMultipleAsOne = source.CompressMultipleAsOne;
        DirectoryStructureMode = source.DirectoryStructureMode;
        IncludeHiddenAndSystemEntries = source.IncludeHiddenAndSystemEntries;
        LogMaxSizeMB = source.LogMaxSizeMB;
        LogRetentionDays = source.LogRetentionDays;
        ZipCompressionLevel = source.ZipCompressionLevel;
        SevenZipCompressionLevel = source.SevenZipCompressionLevel;
        LzhCompressionMethod = source.LzhCompressionMethod;
        VerifyAfterExtraction = source.VerifyAfterExtraction;
        NormalizeUnicodeFileNames = source.NormalizeUnicodeFileNames;
        PropagateMarkOfTheWeb = source.PropagateMarkOfTheWeb;
        RespectNestedGitignore = source.RespectNestedGitignore;
        SourceIgnoreFileNames = [.. source.SourceIgnoreFileNames];
        IsPasswordProtectionEnabled = source.IsPasswordProtectionEnabled;
        PasswordMode = source.PasswordMode;
        EncryptedCompressionPassword = source.EncryptedCompressionPassword;
        EncryptFileNames = source.EncryptFileNames;
    }

    /// <summary>
    /// 設定をファイルから読み込むメソッド
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load() => LoadWithStatus().Settings;

    /// <summary>
    /// <see cref="Load"/> と同じ読み込みを行い、読み込み結果の種別も返す。
    /// 他プロセスの排他などで読めなかった場合は <see cref="SettingsLoadStatus.Unreadable"/> となり、
    /// 設定は既定値になる（起動は継続できるが、呼び出し側は既定値をファイルへ書き戻してはならない）。
    /// </summary>
    internal static SettingsLoadResult LoadWithStatus()
        => LoadWithStatusFrom(
            SettingsFilePath, Path.Combine(AppContext.BaseDirectory, "settings.json"), LhaignoreFile.FilePath);

    /// <summary>
    /// <see cref="LoadWithStatus"/> の本体。読込みと .lhaignore の初期化・移行の順序を、テストが実ユーザーの
    /// 設定・.lhaignore へ触れずに検証できるよう、3 つのパスを引数に取る。
    /// </summary>
    internal static SettingsLoadResult LoadWithStatusFrom(string settingsPath, string legacySettingsPath, string lhaignorePath)
    {
        var result = LoadFromPath(settingsPath, legacySettingsPath);
        // 読めなかったときの設定は既定値なので、それを根拠に .lhaignore（外部ファイル）を作成・移行しない。
        if (result.Status != SettingsLoadStatus.Unreadable)
            InitializeLhaignore(result.Settings, lhaignorePath);
        return result;
    }

    /// <summary>settings.json の読込み（一時ロックの再試行回数。待機は 40ms + 80ms 程度で合計 0.2 秒未満）。</summary>
    private const int ReadMaxAttempts = 3;

    private const int ReadInitialDelayMs = 40;

    /// <summary>
    /// テスト用の差し替え口。引き継ぎと既定値の新規作成が失敗・競合した後の分岐を、実ファイルの競合なしに決定的に試す。
    /// <see cref="AsyncLocal{T}"/> なので、並列実行される他テストの読込みへ影響しない。本番では常に null。
    /// </summary>
    internal sealed record LoadFileHooks(
        Func<string, string, Exception?>? AdoptLegacy = null,
        Func<string, string, bool>? CreateNew = null);

    internal static readonly AsyncLocal<LoadFileHooks?> LoadFileHooksForTests = new();

    /// <summary>
    /// 指定パスの設定を読み込む。パスを引数に取るのは、テストが実ユーザーの設定へ触れずに検証できるようにするため。
    /// </summary>
    /// <param name="settingsPath">settings.json のパス</param>
    /// <param name="legacySettingsPath">旧位置（実行ディレクトリ）の settings.json。新位置に無いときだけ移行する</param>
    internal static SettingsLoadResult LoadFromPath(string settingsPath, string legacySettingsPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // File.Exists は属性の取得失敗でも false を返すため、不存在と読取り不能を区別して調べる。
            var probe = ProbeSettingsFile(settingsPath);

            // 移行処理：新しい場所に設定ファイルがなく、古い場所にある場合は移動する
            if (probe.Error is null && !probe.Exists)
            {
                var legacyProbe = ProbeSettingsFile(legacySettingsPath);
                if (legacyProbe.Error is not null)
                    return UnreadableResult(legacyProbe.Error);
                if (legacyProbe.Exists)
                {
                    // 旧設定を引き継げないまま既定値を新規作成すると、旧設定が以後読まれず失われる。引き継げなければ読込不能にする。
                    var adoptError = (LoadFileHooksForTests.Value?.AdoptLegacy ?? AdoptLegacySettingsFile)(
                        legacySettingsPath, settingsPath);
                    probe = ProbeSettingsFile(settingsPath);
                    // 失敗しても、別プロセスが先に新位置を作っていれば読込みへ進む。旧ファイルが消えていて新位置も無いときは
                    // 引き継ぐ物が無いので、従来どおり既定値を作る。それ以外の失敗は旧設定を失わないよう読込不能にする。
                    if (adoptError is not null && !probe.Exists)
                    {
                        var legacyRecheck = ProbeSettingsFile(legacySettingsPath);
                        if (legacyRecheck.Error is not null || legacyRecheck.Exists)
                            return UnreadableResult(adoptError);
                    }
                }
            }

            if (probe.Error is not null)
            {
                return UnreadableResult(probe.Error);
            }

            if (probe.Exists)
            {
                var (json, readError) = ReadSettingsText(settingsPath);
                if (readError is not null)
                    return UnreadableResult(readError);
                if (json is not null)
                    return ParseLoadedSettings(json, settingsPath);
            }

            var defaultSettings = new Settings();
            try
            {
                // 新規作成は上書きしない。別プロセスが先に作っていたら、その内容を1回だけ読み直して採用する。
                var defaultJson = JsonSerializer.Serialize(defaultSettings, AppJsonContext.Default.Settings);
                var created = LoadFileHooksForTests.Value?.CreateNew is { } createNew
                    ? createNew(settingsPath, defaultJson)
                    : AtomicFileWriter.TryCreateNew(settingsPath, defaultJson, Utf8NoBom);
                if (!created)
                {
                    var (racedJson, racedError) = ReadSettingsText(settingsPath);
                    if (racedError is not null)
                        return UnreadableResult(racedError);
                    if (racedJson is not null)
                        return ParseLoadedSettings(racedJson, settingsPath);
                }
            }
            catch (Exception ex)
            {
                // 存在しないファイルへの書込み失敗なので、既存設定は失われない。起動は既定値で継続する。
                Debug.WriteLine($"設定ファイルの新規作成に失敗しました: {ex.Message}");
            }
            return new SettingsLoadResult(defaultSettings, SettingsLoadStatus.Created);
        }
        catch (Exception ex)
        {
            return UnreadableResult(ex);
        }
    }

    /// <summary>設定ファイルを読む（一時ロックは再試行）。調べた直後に消えていたら (null, null) を返し、不存在として扱わせる。</summary>
    private static (string? Json, Exception? Error) ReadSettingsText(string path)
    {
        try
        {
            return (LockedFileRetryPolicy.Execute<string?>(
                () => File.ReadAllText(path), path, ReadMaxAttempts, ReadInitialDelayMs), null);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>
    /// 旧位置の settings.json を新位置へ引き継ぐ。旧ファイルは移動せず、読んだ内容を一時ファイル経由で新位置へ
    /// 「上書きしない」原子的な作成で複製する（途中までの内容を新位置に残さない。別プロセスが先に作っていたらそちらを尊重する）。
    /// 旧ファイルの削除は best-effort で、残っても新位置がある間は旧位置を読まないので再移行されない。
    /// </summary>
    /// <returns>引き継げなかったときの原因。成功（競合で先に作られていた場合を含む）は null。</returns>
    internal static Exception? AdoptLegacySettingsFile(string legacySettingsPath, string settingsPath)
    {
        try
        {
            var json = LockedFileRetryPolicy.Execute(
                () => File.ReadAllText(legacySettingsPath), legacySettingsPath, ReadMaxAttempts, ReadInitialDelayMs);
            AtomicFileWriter.TryCreateNew(settingsPath, json, Utf8NoBom);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"設定ファイルの移行に失敗しました: {ex.Message}");
            return ex;
        }

        try { File.Delete(legacySettingsPath); }
        catch (Exception ex) { Debug.WriteLine($"旧設定ファイルを削除できませんでした（新位置を優先するため影響なし）: {ex.Message}"); }
        // Logger.Log を使うと再帰の恐れがあるため、デバッグ出力のみ
        Debug.WriteLine($"設定ファイルを移行しました: {legacySettingsPath} -> {settingsPath}");
        return null;
    }

    private static SettingsLoadResult UnreadableResult(Exception error)
    {
        // ここも再帰回避のため Logger.Log は控える
        Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {error.Message}");
        return new SettingsLoadResult(new Settings(), SettingsLoadStatus.Unreadable, error);
    }

    /// <summary>設定ファイルの有無を調べる。不存在以外の失敗（アクセス拒否など）は <c>Error</c> に返す。</summary>
    private static (bool Exists, Exception? Error) ProbeSettingsFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) == 0
                ? (true, null)
                : (false, new IOException($"設定ファイルのパスがフォルダーです: {path}"));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (false, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    /// <summary>読み込んだ JSON を設定へ変換する。破損時は回収を試み、回収不能なら退避して既定値にする。</summary>
    private static SettingsLoadResult ParseLoadedSettings(string json, string settingsPath)
    {
        Settings? settings = null;
        var status = SettingsLoadStatus.Loaded;
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
                status = SettingsLoadStatus.CorruptReset;
                var statusMsg = QuarantineCorruptSettingsFile(settingsPath, DateTime.Now, ex.Message);
                Debug.WriteLine($"設定ファイルの解析に失敗しました（デフォルトに戻します）: {ex.Message}");
                try
                {
                    statusMsg ??= "破損ファイルの退避に失敗しました（次回起動時も同じエラーが再発する可能性があります）";
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
                    settings.SaveTo(settingsPath);
                }
                catch (Exception migrationSaveException)
                {
                    // 値はメモリ上で移行済みなので起動は継続し、次回の通常保存で再試行する。
                    Debug.WriteLine($"右クリックメニュー設定の移行保存に失敗しました: {migrationSaveException.Message}");
                }
            }
        }

        return new SettingsLoadResult(settings ?? new Settings(), status);
    }

    /// <summary>
    /// 回収不能に破損した settings.json を片付ける。順序（Move → Copy → Delete → 空 JSON で上書き → 緊急ログ）は
    /// DESIGN.md / docs/SETTINGS_SCHEMA.md の契約。実際に起きた処理をログへ正しく出すため、どの段で片付いたかを返す。
    /// </summary>
    /// <param name="settingsPath">破損した settings.json のパス</param>
    /// <param name="timestamp">退避ファイル名に使う時刻</param>
    /// <param name="originalMessage">破損の原因となった <see cref="JsonException"/> のメッセージ（緊急ログ用）</param>
    /// <returns>ログ用の結果説明。全段が失敗したときは null。</returns>
    internal static string? QuarantineCorruptSettingsFile(string settingsPath, DateTime timestamp, string originalMessage)
    {
        var backupPath = $"{settingsPath}.corrupt_{timestamp:yyyyMMddHHmmss_fff}.bak";
        try
        {
            File.Move(settingsPath, backupPath, overwrite: true);
            return $"破損ファイルは {backupPath} に退避済みです";
        }
        catch (Exception moveEx)
        {
            Debug.WriteLine($"破損 settings.json の Move に失敗: {moveEx.Message}");
            return QuarantineByCopyAndDelete(settingsPath, backupPath, originalMessage);
        }
    }

    private static string? QuarantineByCopyAndDelete(string settingsPath, string backupPath, string originalMessage)
    {
        // Delete で内容を失う前に、コピーでの退避を試みる（Move はロック中でも読み取りは
        // できる場合がある: OneDrive 同期中・ウイルス対策の検査中など）。失敗しても続行する。
        var copiedToBackup = false;
        try
        {
            File.Copy(settingsPath, backupPath, overwrite: true);
            copiedToBackup = true;
        }
        catch (Exception copyEx)
        {
            Debug.WriteLine($"破損 settings.json のコピー退避に失敗: {copyEx.Message}");
        }
        try
        {
            File.Delete(settingsPath);
            return copiedToBackup
                ? $"破損ファイルは {backupPath} にコピーで退避し、元ファイルを削除しました"
                : "破損ファイルを退避できなかったため削除しました（内容は保存されていません）";
        }
        catch (Exception deleteEx)
        {
            Debug.WriteLine($"破損 settings.json の Delete に失敗: {deleteEx.Message}");
        }
        try
        {
            // 最終フォールバックの空 JSON 上書きも atomic 書込で部分破損を防ぐ。
            // RTK レビュー #B1-008 対応。
            WriteAtomically(settingsPath, "{}");
            return copiedToBackup
                ? $"破損ファイルは {backupPath} にコピーで退避し、元ファイルを空 JSON で上書きしました"
                : "破損ファイルを退避・削除できなかったため空 JSON で上書きしました（内容は保存されていません）";
        }
        catch (Exception writeEx)
        {
            Debug.WriteLine($"破損 settings.json の空 JSON 上書きに失敗: {writeEx.Message}");
            // RTK レビュー #F-012 対応: 3 段フォールバック全失敗ケースで、Logger も
            // 未初期化な可能性が高いため、emergency.log に直接書く最終フォールバック。
            Logger.WriteEmergencyLog(
                $"settings.json の退避・削除・空 JSON 上書きが全て失敗。次回起動時も同じエラーが再発します。元エラー: {originalMessage}",
                writeEx);
            return null;
        }
    }

    /// <summary>
    /// .lhaignore の初期化。レガシー ExcludedFilePatterns があれば移行する。
    /// 既にファイルがあれば何もしないので何度呼んでも安全。
    /// </summary>
    /// <remarks>
    /// このメソッドが移行内容を書けたと確認できた（戻り値 true）ときだけレガシー値を破棄する。
    /// 作成に失敗した場合や、作成競合に負けた（別プロセス・外部エディタが先に作成した。内容は旧パターンを含むとは限らない）
    /// 場合は、次回 Save で旧 ExcludedFilePatterns が消えて復元不能にならないよう、レガシー値を温存する。
    /// テストから実ユーザーの .lhaignore に触れずに検証できるよう、パスを引数で受け取る。
    /// </remarks>
    internal static void InitializeLhaignore(Settings settings, string lhaignorePath)
    {
        if (!File.Exists(lhaignorePath))
        {
            var created = LhaignoreFile.EnsureExistsAt(lhaignorePath, settings._legacyExcludedFilePatterns);
            if (created)
                settings._legacyExcludedFilePatterns = null;
        }
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
        // LHA は LZH の一般的な別名として受理し、作成形式は canonical な LZH に統一する。
        CompressionFormat = string.Equals(CompressionFormat, "LHA", StringComparison.OrdinalIgnoreCase)
            ? "LZH"
            : Array.Find(SupportedCompressionFormats, f => string.Equals(f, CompressionFormat, StringComparison.OrdinalIgnoreCase))
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
        LzhCompressionMethod = NormalizeLzhCompressionMethod(LzhCompressionMethod);

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
            && CompressionFormat is not ("ZIP" or "7z"))
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

    /// <summary>LZH 作成で選択できる圧縮方式（canonical な大文字表記）。</summary>
    public static readonly string[] SupportedLzhCompressionMethods = ["LH0", "LH5", "LH6", "LH7"];

    /// <summary>
    /// LZH 圧縮方式を canonical な大文字表記へ正規化する。
    /// null または未対応値は後方互換の既定値 <c>LH5</c> に戻す。
    /// </summary>
    internal static string NormalizeLzhCompressionMethod(string? method) =>
        Array.Find(
            SupportedLzhCompressionMethods,
            candidate => string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase))
        ?? "LH5";

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
    /// <see cref="AtomicFileWriter"/>（.lhaignore と共通）の「tmp + flush + Move overwrite」パターンで
    /// 部分書き込みを排除する。RTK レビュー #B1-007 対応。
    /// </para>
    /// </summary>
    public void Save() => SaveTo(SettingsFilePath);

    /// <summary>指定パスへ保存する（テストが実ユーザーの設定へ触れずに検証できるよう、パスを引数に取る）。</summary>
    internal void SaveTo(string settingsPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(this, AppJsonContext.Default.Settings);
            WriteAtomically(settingsPath, json);
        }
        catch (Exception ex)
        {
            // 利用者向けの接頭辞（Error.SaveSettingsFailed）は表示する側が1回だけ付ける。ここでは原因だけを運ぶ。
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>
    /// 内容を一時ファイルに書いてディスクへフラッシュし、<see cref="File.Move(string, string, bool)"/> で
    /// 上書きすることで、部分書き込みを排除する atomic 書込ヘルパ（実体は <see cref="AtomicFileWriter"/>）。
    /// プロセス強制終了 / 電源断のレースで対象ファイルが 0 バイト truncate されるリスクを防ぐ。
    /// 同一ボリューム上では <c>File.Move</c> は MoveFileEx の <c>MOVEFILE_REPLACE_EXISTING</c>
    /// + Rename で atomic に振る舞う（クロスボリューム時はコピー扱いで atomic 性は劣化するが、
    /// 通常 <c>%LocalAppData%\Lhamiel</c> 配下は同一ボリュームなので問題なし）。
    /// <para>
    /// tmp → 本ファイルの上書き move は、AV / 検索インデクサ / バックアップが settings.json を一瞬掴むと
    /// SHARING_VIOLATION / 一時 AccessDenied で散発失敗する（300ms デバウンス AutoSave・Remember パスワード保存・
    /// IgnoreUpdateTag 書込が競合しうる）ため、<see cref="AtomicFileWriter"/> 内で LockedFileRetryPolicy により
    /// リトライする。エンコーディングは旧実装の <c>File.WriteAllText(path, content)</c> と同じ BOM なし UTF-8。
    /// </para>
    /// </summary>
    private static void WriteAtomically(string destinationPath, string content)
        => AtomicFileWriter.WriteAllText(destinationPath, content, Utf8NoBom);

    /// <summary>settings.json の書込みに使う BOM なし UTF-8（<c>File.WriteAllText</c> の既定と同じ）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
        LzhCompressionMethod = "LH5";
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
    internal static Settings? TryRecoverFromJsonDocument(string json)
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
            if (TryGetString(root, nameof(LzhCompressionMethod), out var lcm)) { s.LzhCompressionMethod = lcm!; recoveredCount++; }

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
