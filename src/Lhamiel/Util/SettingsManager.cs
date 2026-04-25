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
    /// 変更・保存・スナップショット作成を直列化するためのロック。
    /// UI スレッド上の AutoSave と、バックグラウンド Task.Run 上の
    /// <see cref="CreateSnapshot"/> が同時に走っても <see cref="Settings.ExcludedFilePatterns"/> 等の
    /// コレクション列挙で <see cref="InvalidOperationException"/> が起きないようにする。
    /// </summary>
    private readonly object _lock = new();

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
    /// <see cref="Mutate"/> / <see cref="Save"/> と同一ロック下で実行されるため、
    /// コレクション列挙中の <see cref="InvalidOperationException"/> を避けられる。
    /// </summary>
    public Settings CreateSnapshot()
    {
        lock (_lock) return _settings.Snapshot();
    }

    /// <summary>
    /// 設定を同一ロック下で変更する。<c>_settings</c> 自体のプロパティ代入や
    /// <see cref="Settings.ExcludedFilePatterns"/> 等のコレクション差し替えで使用する。
    /// </summary>
    /// <param name="mutator">変更アクション</param>
    public void Mutate(Action<Settings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_lock) mutator(_settings);
    }

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
            // 重要: Settings.Load 失敗時、Logger.Initialize がまだ実行されていない可能性があるため
            // Logger.LogException がサイレントに握りつぶされる経路がある。
            // 起動失敗の詳細をどこにも残せず再現困難になる事態を避けるため、
            // 緊急 Logger 初期化（デフォルト構成）を試みてからログ出力する。
            try
            {
                Logger.Initialize(new LoggerConfig
                {
                    LogDirectory = Settings.AppDataDirectory,
                    FilePrefix = "Lhamiel",
                });
            }
            catch (Exception initEx)
            {
                System.Diagnostics.Debug.WriteLine($"緊急 Logger 初期化にも失敗: {initEx.Message}");
            }
            Logger.LogException("設定の読み込みに失敗しました。デフォルト設定を使用します", ex);
            // Logger も使えない最終フォールバック: Debug.WriteLine だけは残す
            System.Diagnostics.Debug.WriteLine($"Settings.Load 失敗: {ex}");
            _settings = new Settings();
        }
    }

    /// <summary>
    /// 設定を保存。変更・Snapshot と同一ロック下で保護する。
    /// </summary>
    public void Save()
    {
        try
        {
            lock (_lock) _settings.Save();
            Logger.Log("設定を保存しました");
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の保存に失敗しました", ex);
            throw;
        }
    }

}
