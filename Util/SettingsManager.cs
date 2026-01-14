using System;

namespace Lhamiel.Util;

/// <summary>
/// 設定管理のシングルトンクラス
/// アプリケーション全体で一つの Settings インスタンスを共有し、
/// 繰り返しファイルを読み込むことを防ぎます。
/// </summary>
public sealed class SettingsManager
{
    /// <summary>
    /// SettingsManager のシングルトンインスタンス
    /// </summary>
    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());
    private readonly Settings _settings;

    /// <summary>
    /// SettingsManager のシングルトンインスタンスを取得
    /// </summary>
    public static SettingsManager Instance => _instance.Value;

    /// <summary>
    /// 現在の設定を取得
    /// </summary>
    public Settings Current => _settings;

    /// <summary>
    /// プライベートコンストラクタ
    /// </summary>
    private SettingsManager()
    {
        try
        {
            _settings = Settings.Load();
            Logger.Log("設定を読み込みました", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の読み込みに失敗しました。デフォルト設定を使用します", ex);
            _settings = new Settings();
        }
    }

    /// <summary>
    /// 設定を保存
    /// </summary>
    public void Save()
    {
        try
        {
            _settings.Save();
            Logger.Log("設定を保存しました", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の保存に失敗しました", ex);
            throw;
        }
    }

    /// <summary>
    /// 設定を再読み込み
    /// </summary>
    public void Reload()
    {
        try
        {
            var newSettings = Settings.Load();

            // 既存の設定オブジェクトのプロパティを更新
            _settings.CompressionFormat = newSettings.CompressionFormat;
            _settings.ExtractionOutputDirectory = newSettings.ExtractionOutputDirectory;
            _settings.CompressionOutputDirectory = newSettings.CompressionOutputDirectory;
            _settings.ExtractionOutputToSameDirectory = newSettings.ExtractionOutputToSameDirectory;
            _settings.CompressionOutputToSameDirectory = newSettings.CompressionOutputToSameDirectory;
            _settings.EnableShortcutCreation = newSettings.EnableShortcutCreation;
            _settings.UpdateRepoOwner = newSettings.UpdateRepoOwner;
            _settings.UpdateRepoName = newSettings.UpdateRepoName;
            _settings.UpdateChannel = newSettings.UpdateChannel;

            Logger.Log("設定を再読み込みしました", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の再読み込みに失敗しました", ex);
            throw;
        }
    }

    /// <summary>
    /// 設定をデフォルト値にリセット
    /// </summary>
    public void ResetToDefaults()
    {
        try
        {
            _settings.ResetToDefaults();
            Logger.Log("設定をデフォルト値にリセットしました", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.LogException("設定のリセットに失敗しました", ex);
            throw;
        }
    }
}
