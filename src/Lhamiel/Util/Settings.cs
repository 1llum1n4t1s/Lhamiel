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
    /// <summary>
    /// アプリケーションデータディレクトリ
    /// </summary>
    internal static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lhamiel");

    /// <summary>
    /// 設定ファイルのパス
    /// </summary>
    private static readonly string SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>
    /// 自動更新で許可する GitHub リポジトリの正規値（悪意ある誘導を防ぐためハードコード固定）
    /// </summary>
    internal const string CanonicalUpdateRepoOwner = "1llum1n4t1s";
    internal const string CanonicalUpdateRepoName = "Lhamiel";

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
    /// 自動更新用のGitHubオーナー名。
    /// セキュリティ上の理由でハードコード固定。settings.json から書き換えても反映されない。
    /// </summary>
    [JsonIgnore]
    public string UpdateRepoOwner => CanonicalUpdateRepoOwner;

    /// <summary>
    /// 自動更新用のGitHubリポジトリ名。
    /// セキュリティ上の理由でハードコード固定。settings.json から書き換えても反映されない。
    /// </summary>
    [JsonIgnore]
    public string UpdateRepoName => CanonicalUpdateRepoName;

    /// <summary>
    /// 自動更新用のチャンネル名
    /// </summary>
    public string UpdateChannel { get; set; } = "release";

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
    /// 圧縮時に除外するファイル・フォルダのパターン。
    /// デフォルト値は ArchiveExtractor の無視リストから生成。
    /// </summary>
    public List<string> ExcludedFilePatterns { get; set; } =
        [.. ArchiveExtractor.IgnoredSystemFiles, .. ArchiveExtractor.IgnoredSystemDirectories];

    /// <summary>
    /// サポートされているテーマ一覧。UI および <see cref="SanitizeAfterLoad"/> で使用。
    /// マジック文字列の散在を避けるためここに集約する。
    /// </summary>
    public static readonly string[] SupportedThemes = ["System", "Dark", "Light"];

    /// <summary>
    /// サポートされている圧縮形式の一覧
    /// </summary>
    public static readonly string[] SupportedCompressionFormats = ["ZIP", "7z", "TAR"];

    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    public static readonly string[] SupportedExtractionFormats = ["ZIP", "7z", "TAR", "GZ", "BZ2", "LZMA", "XZ", "RAR", "LZH", "CAB", "ARJ", "Z"];

    /// <summary>
    /// 展開専用形式の一覧
    /// </summary>
    public static readonly string[] ExtractOnlyFormats = ["RAR", "ARJ", "Z"];

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
    /// 並列アクセスに対して安全なスナップショット（浅いコピー）を返す。
    /// 呼び出し元は処理開始時に1回だけ呼び出し、その後はスナップショットを使うことで
    /// UI スレッド側の設定変更との race を回避する。
    /// </summary>
    /// <remarks>
    /// ⚠️ 重要: <see cref="MemberwiseClone"/> は参照型フィールドを「参照のみ」コピーするため、
    /// 新しく <see cref="List{T}"/> / <see cref="Dictionary{TKey,TValue}"/> / その他 mutable コレクションを
    /// 追加した場合は、必ず下記に明示的な深コピーを追加すること。漏れるとバックグラウンド処理中に
    /// UI スレッド側の変更を拾ってしまい、<c>InvalidOperationException</c>（列挙中変更）の race が
    /// 発生する。値型・不変型（<see cref="string"/> / <see cref="int"/> / <see cref="bool"/> / enum）は
    /// <see cref="MemberwiseClone"/> で安全にコピーされるので追記不要。
    /// </remarks>
    public Settings Snapshot()
    {
        var copy = (Settings)MemberwiseClone();
        // 参照型コレクションは明示的に深コピー（新しく追加した場合は下に追記すること）
        copy.ExcludedFilePatterns = ExcludedFilePatterns is null ? [] : [.. ExcludedFilePatterns];
        return copy;
    }

    /// <summary>
    /// 設定をファイルから読み込むメソッド
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load()
    {
        try
        {
            // 旧パス（アプリケーション実行ディレクトリ）
            var oldSettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

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
                Settings? settings;
                try
                {
                    settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);
                }
                catch (JsonException ex)
                {
                    // JSON スキーマ不整合時はサイレントに全デフォルト化せず、破損ファイルを退避してから
                    // デフォルトに戻す。ユーザーが気付けるよう警告ログも残す。
                    // Logger は Initialize 前に呼ばれる可能性があるので null チェックは Logger 側で行う。
                    var backupPath = $"{SettingsFilePath}.corrupt_{DateTime.Now:yyyyMMddHHmmss}.bak";
                    try { File.Copy(SettingsFilePath, backupPath, overwrite: true); } catch { /* ベストエフォート */ }
                    Debug.WriteLine($"設定ファイルの解析に失敗しました（デフォルトに戻します）: {ex.Message}");
                    try
                    {
                        Logger.Log(
                            $"settings.json の解析に失敗したためデフォルトに戻しました。破損ファイルは {backupPath} に退避済みです。理由: {ex.Message}",
                            LogLevel.Warning);
                    }
                    catch { /* Logger 未初期化のケース */ }
                    settings = null;
                }

                if (settings != null)
                {
                    settings.SanitizeAfterLoad();
                    return settings;
                }

                return new Settings();
            }

            var defaultSettings = new Settings();
            defaultSettings.Save(); // 新規作成時にファイルに書き込む
            return defaultSettings;
        }
        catch (Exception ex)
        {
            // ここも再帰回避のため Logger.Log は控える
            Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        return new Settings();
    }

    /// <summary>
    /// Load 直後に不正値をデフォルトに戻すサニタイズ処理。
    /// 外部から書き換えられ得る settings.json に対する軽量防御として機能する。
    /// </summary>
    internal void SanitizeAfterLoad()
    {
        // UpdateChannel の allow-list 化: 未知の値を渡されても Velopack に無効な channel を渡さない。
        // case-insensitive で受理しつつ、下流（Velopack CLI 引数や URL パス）が
        // case-sensitive 比較を行う可能性があるため canonical な小文字に正規化する。
        UpdateChannel = string.Equals(UpdateChannel, "prerelease", StringComparison.OrdinalIgnoreCase)
            ? "prerelease"
            : "release";

        // Theme の allow-list 化 + canonical ケース正規化。
        // App.axaml.cs の GetThemeVariant は "Light"/"Dark"/"System" の固定文字列で switch するため、
        // "DARK" 等の入力が下流でサイレントにフォールバックしないよう SupportedThemes 側のケースに揃える。
        Theme = Array.Find(SupportedThemes, t => string.Equals(t, Theme, StringComparison.OrdinalIgnoreCase))
                ?? "System";

        // CompressionFormat も同様に canonical ケース正規化。
        CompressionFormat = Array.Find(SupportedCompressionFormats, f => string.Equals(f, CompressionFormat, StringComparison.OrdinalIgnoreCase))
                            ?? "ZIP";

        // 出力先ディレクトリのパス妥当性チェック（存在確認 + 保護ディレクトリ除外）
        // 不正値はデスクトップにフォールバック
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!IsUsableOutputDirectory(ExtractionOutputDirectory))
            ExtractionOutputDirectory = desktop;
        if (!IsUsableOutputDirectory(CompressionOutputDirectory))
            CompressionOutputDirectory = desktop;
    }

    private static bool IsUsableOutputDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Directory.Exists(path)) return false;
            if (PathValidator.IsProtectedDirectory(path)) return false;
        }
        catch { return false; }
        return true;
    }

    /// <summary>
    /// 設定をファイルに保存するメソッド
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, AppJsonContext.Default.Settings);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(App.Text("Error.SaveSettingsFailed", ex.Message), ex);
        }
    }

    /// <summary>
    /// 設定をデフォルト値にリセットする
    /// </summary>
    public void ResetToDefaults()
    {
        Theme = "System";
        CompressionFormat = "ZIP";
        ExtractionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        CompressionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        ExtractionOutputToSameDirectory = false;
        CompressionOutputToSameDirectory = false;
        OpenExtractionOutputFolder = true;
        OpenCompressionOutputFolder = true;
        CreateArchiveNameFolder = true;
        DirectoryStructureMode = DirectoryStructureMode.IncludeRoot;
        UpdateChannel = "release";
        LogMaxSizeMB = 10;
        LogRetentionDays = 7;
        CompressMultipleAsOne = true;
        Locale = "";
        ZipCompressionLevel = 5;
        SevenZipCompressionLevel = 5;
        ExcludedFilePatterns = [.. ArchiveExtractor.IgnoredSystemFiles, .. ArchiveExtractor.IgnoredSystemDirectories];
        // NOTE: 新しいプロパティを追加したら必ずここにも追加すること（リセット漏れ防止）
    }
}
