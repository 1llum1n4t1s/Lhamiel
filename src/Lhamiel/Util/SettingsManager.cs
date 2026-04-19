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
    /// 現在の設定を取得（読み取り専用の参照を返す）。
    /// 並列処理や長時間の処理で使う場合は <see cref="CreateSnapshot"/> を使うこと。
    /// </summary>
    public Settings Current => _settings;

    /// <summary>
    /// 現在の設定の浅いコピー（スナップショット）を作成する。
    /// 並列処理中に UI スレッドから設定変更が起きても、スナップショットは影響を受けない。
    /// </summary>
    public Settings CreateSnapshot() => _settings.Snapshot();

    /// <summary>
    /// プライベートコンストラクタ
    /// </summary>
    private SettingsManager()
    {
        try
        {
            _settings = Settings.Load();
            // Logger が未初期化の場合は設定を渡して初期化（循環参照防止）
            Logger.Initialize(new LoggerConfig
            {
                LogDirectory = Settings.AppDataDirectory,
                FilePrefix = "Lhamiel",
                MaxSizeMB = _settings.LogMaxSizeMB,
                RetentionDays = _settings.LogRetentionDays
            });
            Logger.Log("設定を読み込みました");
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
            Logger.Log("設定を保存しました");
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の保存に失敗しました", ex);
            throw;
        }
    }

}
