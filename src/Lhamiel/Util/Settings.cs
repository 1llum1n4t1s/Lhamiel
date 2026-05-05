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
    /// 圧縮時に隠し属性・システム属性のファイルやフォルダも含めるかどうか
    /// </summary>
    public bool IncludeHiddenAndSystemEntries { get; set; } = true;

    /// <summary>
    /// 圧縮時に除外するファイル・フォルダのパターン。
    /// デフォルト値は ArchiveExtractor の無視リストから生成。
    /// </summary>
    public List<string> ExcludedFilePatterns { get; set; } = CreateDefaultExcludedFilePatterns();

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
    /// サポートされている自動更新チャンネルの一覧（canonical な小文字形）。
    /// 将来 stable / beta / canary 等を追加する場合はここに足すだけで SanitizeAfterLoad が追従する。
    /// </summary>
    public static readonly string[] SupportedUpdateChannels = ["release", "prerelease"];

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
    /// 展開後にアーカイブの CRC 整合性検証を実行するかどうか
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
                                    File.WriteAllText(SettingsFilePath, "{}");
                                    sanitizationSucceeded = true;
                                }
                                catch (Exception writeEx)
                                {
                                    Debug.WriteLine($"破損 settings.json の空 JSON 上書きに失敗: {writeEx.Message}");
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

        ExcludedFilePatterns = NormalizeExcludedFilePatterns(ExcludedFilePatterns ?? CreateDefaultExcludedFilePatterns());

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
        IncludeHiddenAndSystemEntries = true;
        UpdateChannel = "release";
        LogMaxSizeMB = 10;
        LogRetentionDays = 7;
        CompressMultipleAsOne = true;
        Locale = "";
        ZipCompressionLevel = 5;
        SevenZipCompressionLevel = 5;
        ExcludedFilePatterns = CreateDefaultExcludedFilePatterns();
        VerifyAfterExtraction = true;
        NormalizeUnicodeFileNames = true;
        PropagateMarkOfTheWeb = true;
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
            if (TryGetString(root, nameof(ExtractionOutputDirectory), out var eod)) { s.ExtractionOutputDirectory = eod!; recoveredCount++; }
            if (TryGetString(root, nameof(CompressionOutputDirectory), out var cod)) { s.CompressionOutputDirectory = cod!; recoveredCount++; }
            // UpdateRepoOwner / UpdateRepoName は [JsonIgnore] ハードコード固定のため回収不要
            if (TryGetString(root, nameof(UpdateChannel), out var uc)) { s.UpdateChannel = uc!; recoveredCount++; }

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

            if (TryGetInt(root, nameof(LogMaxSizeMB), out var lms)) { s.LogMaxSizeMB = lms; recoveredCount++; }
            if (TryGetInt(root, nameof(LogRetentionDays), out var lrd)) { s.LogRetentionDays = lrd; recoveredCount++; }
            if (TryGetInt(root, nameof(ZipCompressionLevel), out var zcl)) { s.ZipCompressionLevel = zcl; recoveredCount++; }
            if (TryGetInt(root, nameof(SevenZipCompressionLevel), out var szcl)) { s.SevenZipCompressionLevel = szcl; recoveredCount++; }

            if (TryGetEnum<DirectoryStructureMode>(root, nameof(DirectoryStructureMode), out var dsm))
            {
                s.DirectoryStructureMode = dsm;
                recoveredCount++;
            }

            if (root.TryGetProperty(nameof(ExcludedFilePatterns), out var efpEl) && efpEl.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var list = new List<string>();
                    foreach (var item in efpEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            list.Add(item.GetString()!);
                    }
                    s.ExcludedFilePatterns = [.. list];
                    recoveredCount++;
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
