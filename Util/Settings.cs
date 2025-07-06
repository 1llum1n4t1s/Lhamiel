using System.Text.Json;
using System.IO;

namespace GGEZArchiver.Util;

/// <summary>
/// アプリケーション設定を管理するクラス
/// 設定の保存・読み込みとデフォルト値の提供を担当
/// </summary>
public class Settings
{
    /// <summary>
    /// 設定ファイルのパス
    /// アプリケーションの実行ファイルと同じディレクトリに配置される
    /// </summary>
    private static readonly string SettingsFilePath = Path.Combine(
        System.AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>
    /// 圧縮形式の設定
    /// デフォルトはZIP形式
    /// </summary>
    public string CompressionFormat { get; set; } = "zip";

    /// <summary>
    /// 展開用出力ディレクトリの設定
    /// デフォルトはデスクトップ
    /// </summary>
    public string ExtractionOutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// 圧縮用出力ディレクトリの設定
    /// デフォルトはデスクトップ
    /// </summary>
    public string CompressionOutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// 展開用出力先パターンの設定
    /// true: 元のファイルと同じディレクトリに出力、false: 指定したディレクトリに出力
    /// デフォルトは指定したディレクトリに出力
    /// </summary>
    public bool ExtractionOutputToSameDirectory { get; set; } = false;

    /// <summary>
    /// 圧縮用出力先パターンの設定
    /// true: 元のファイルと同じディレクトリに出力、false: 指定したディレクトリに出力
    /// デフォルトは指定したディレクトリに出力
    /// </summary>
    public bool CompressionOutputToSameDirectory { get; set; } = false;

    /// <summary>
    /// ショートカット作成の有効/無効設定
    /// デフォルトは有効
    /// </summary>
    public bool EnableShortcutCreation { get; set; } = true;

    /// <summary>
    /// サポートされている圧縮形式の一覧
    /// ユーザーが選択可能な形式を定義
    /// </summary>
    public static readonly string[] SupportedCompressionFormats = new[]
    {
        "zip", "7z", "tar", "gz", "bz2", "lzma", "xz"
    };

    /// <summary>
    /// サポートされている展開形式の一覧
    /// 圧縮・展開両方に対応する形式と展開のみ対応する形式を含む
    /// </summary>
    public static readonly string[] SupportedExtractionFormats = new[]
    {
        "zip", "7z", "tar", "gz", "bz2", "lzma", "xz", "rar", "lzh", "cab", "arj", "z"
    };

    /// <summary>
    /// 展開専用形式の一覧
    /// これらの形式は圧縮には使用できない
    /// </summary>
    public static readonly string[] ExtractOnlyFormats = new[]
    {
        "rar", "lzh", "cab", "arj", "z"
    };

    /// <summary>
    /// 設定をファイルから読み込む
    /// ファイルが存在しない場合はデフォルト設定を返す
    /// </summary>
    /// <returns>読み込まれた設定オブジェクト</returns>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                return settings ?? new Settings();
            }
        }
        catch (Exception)
        {
            // エラーが発生した場合はデフォルト設定を返す
        }

        return new Settings();
    }

    /// <summary>
    /// 設定をファイルに保存する
    /// JSON形式でシリアライズしてファイルに書き込む
    /// </summary>
    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"設定の保存に失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 設定が有効かどうかを検証する
    /// 必須項目の存在と値の妥当性をチェックする
    /// </summary>
    /// <returns>設定が有効な場合はtrue、そうでなければfalse</returns>
    public bool IsValid()
    {
        // 圧縮形式がサポートされているかチェック
        if (!SupportedCompressionFormats.Contains(CompressionFormat))
        {
            return false;
        }

        // 展開用出力ディレクトリが存在するかチェック
        if (!Directory.Exists(ExtractionOutputDirectory))
        {
            return false;
        }

        // 圧縮用出力ディレクトリが存在するかチェック
        if (!Directory.Exists(CompressionOutputDirectory))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 設定をデフォルト値にリセットする
    /// すべてのプロパティを初期値に戻す
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