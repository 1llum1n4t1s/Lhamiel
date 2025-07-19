using System.IO;
using System.Text.Json;

namespace Lhamiel.Util;

/// <summary>
/// アプリケーション設定を管理するクラス
/// </summary>
public class Settings
{
    /// <summary>
    /// 設定ファイルのパス
    /// </summary>
    private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>
    /// JSONシリアライザーオプション
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 圧縮形式の設定
    /// </summary>
    public string CompressionFormat { get; set; } = "zip";

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
    /// サポートされている圧縮形式の一覧
    /// </summary>
    public static readonly string[] SupportedCompressionFormats = { "7z", "xz", "bz2", "gz", "tar", "zip", "wim", "cab" };

    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    public static readonly string[] SupportedExtractionFormats = { "zip", "7z", "tar", "gz", "bz2", "lzma", "xz", "rar", "lzh", "cab", "arj", "z" };

    /// <summary>
    /// 展開専用形式の一覧
    /// </summary>
    public static readonly string[] ExtractOnlyFormats = { "rar", "arj", "z" };

    /// <summary>
    /// 設定をファイルから読み込む
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                return settings ?? new Settings();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        return new Settings();
    }

    /// <summary>
    /// 設定をファイルに保存する
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
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
        CompressionFormat = "zip";
        ExtractionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        CompressionOutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        ExtractionOutputToSameDirectory = false;
        CompressionOutputToSameDirectory = false;
        EnableShortcutCreation = true;
    }
}