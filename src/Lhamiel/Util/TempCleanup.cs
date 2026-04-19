namespace Lhamiel.Util;

/// <summary>
/// 起動時に前回実行で残存した一時ディレクトリを掃除するユーティリティ。
/// OneDrive 同期フォルダや強制終了・ディスクフル等で Lhamiel_* 一時フォルダが
/// 残る不具合（/rere P1 #10）の軽減策。
/// </summary>
internal static class TempCleanup
{
    /// <summary>一時ディレクトリ名の先頭に付くプレフィックス</summary>
    private const string TempPrefix = "Lhamiel_";

    /// <summary>掃除対象とみなすまでの最小経過時間。現在実行中の他プロセスを誤って消さないためのガード</summary>
    private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(30);

    /// <summary>
    /// %TEMP% 配下および設定された出力先の直下にある Lhamiel_* 古いフォルダを削除する。
    /// </summary>
    /// <remarks>
    /// ベストエフォート。エラーはログだけ残して続行する。現在実行中のプロセスの tempDir を誤削除しないよう
    /// 最終更新日時が <see cref="MinAge"/> より古いものだけを対象にする。
    /// </remarks>
    public static void CleanupOrphanedTempDirectories()
    {
        try
        {
            var candidates = new List<string>
            {
                Path.GetTempPath()
            };

            // 設定が読み込めていれば展開先・圧縮先も掃除対象に含める
            try
            {
                var settings = SettingsManager.Instance.Current;
                if (!string.IsNullOrWhiteSpace(settings.ExtractionOutputDirectory))
                    candidates.Add(settings.ExtractionOutputDirectory);
                if (!string.IsNullOrWhiteSpace(settings.CompressionOutputDirectory))
                    candidates.Add(settings.CompressionOutputDirectory);
            }
            catch
            {
                // SettingsManager 未初期化でも続行
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var deleted = 0;
            foreach (var root in candidates)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                string normalized;
                try { normalized = Path.GetFullPath(root); }
                catch { continue; }
                if (!seen.Add(normalized)) continue;

                IEnumerable<string> dirs;
                try
                {
                    dirs = Directory.EnumerateDirectories(normalized, $"{TempPrefix}*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    Logger.Log($"一時フォルダ列挙に失敗: {normalized}, {ex.Message}", LogLevel.Warning);
                    continue;
                }

                foreach (var dir in dirs)
                {
                    try
                    {
                        var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                        if (now - lastWrite < MinAge) continue;

                        Directory.Delete(dir, recursive: true);
                        deleted++;
                        Logger.Log($"残存一時ディレクトリを削除: {dir}", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"残存一時ディレクトリ削除失敗（無視）: {dir}, {ex.Message}", LogLevel.Debug);
                    }
                }
            }

            if (deleted > 0)
                Logger.Log($"残存一時ディレクトリを {deleted} 件削除しました");
        }
        catch (Exception ex)
        {
            Logger.Log($"TempCleanup でエラー: {ex.Message}", LogLevel.Warning);
        }
    }
}
