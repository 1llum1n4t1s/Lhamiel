using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Lhamiel.Util;

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
    /// ショートカット作成の有効/無効設定
    /// </summary>
    public bool EnableShortcutCreation { get; set; } = true;

    /// <summary>
    /// 自動更新用のGitHubオーナー名
    /// </summary>
    public string UpdateRepoOwner { get; set; } = "1llum1n4t1s";

    /// <summary>
    /// 自動更新用のGitHubリポジトリ名
    /// </summary>
    public string UpdateRepoName { get; set; } = "Lhamiel";

    /// <summary>
    /// 自動更新用のチャンネル名
    /// </summary>
    public string UpdateChannel { get; set; } = "release";

    /// <summary>
    /// 展開完了後に展開先フォルダを開くかどうか
    /// </summary>
    public bool OpenExtractionOutputFolder { get; set; } = true;

    /// <summary>
    /// 圧縮完了後に圧縮先フォルダを開くかどうか
    /// </summary>
    public bool OpenCompressionOutputFolder { get; set; } = true;

    /// <summary>
    /// 複数ファイル・フォルダを1つのアーカイブにまとめて圧縮するかどうか
    /// </summary>
    public bool CompressMultipleAsOne { get; set; }

    /// <summary>
    /// 圧縮時に除外するファイル・フォルダのパターン
    /// </summary>
    public List<string> ExcludedFilePatterns { get; set; } =
    [
        ".DS_Store",
        "Thumbs.db",

        "__MACOSX",
        "desktop.ini"
    ];

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
                var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);
                return settings ?? new Settings();
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
            throw new InvalidOperationException($"設定の保存に失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 設定が有効かどうかを検証する
    /// </summary>
    /// <returns>設定が有効な場合はtrue、そうでなければfalse</returns>
    public bool IsValid()
    {
        return SupportedCompressionFormats.Contains(CompressionFormat) &&
               Directory.Exists(ExtractionOutputDirectory) &&
               Directory.Exists(CompressionOutputDirectory);
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
        EnableShortcutCreation = true;
        OpenExtractionOutputFolder = true;
        OpenCompressionOutputFolder = true;
        UpdateRepoOwner = "1llum1n4t1s";
        UpdateRepoName = "Lhamiel";
        UpdateChannel = "release";
        LogMaxSizeMB = 10;
        LogRetentionDays = 7;
        CompressMultipleAsOne = false;
        Locale = "";
        ZipCompressionLevel = 5;
        SevenZipCompressionLevel = 5;
        ExcludedFilePatterns =
        [
            ".DS_Store",
            "Thumbs.db",
            "__MACOSX",
            "desktop.ini"
        ];
    }
}
