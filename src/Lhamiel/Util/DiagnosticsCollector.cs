using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lhamiel.Util;

/// <summary>
/// サポート用の診断情報を ZIP にまとめてエクスポートする。
/// ログ・マスク済み settings.json・環境情報・ダンプを収集。
/// </summary>
internal static partial class DiagnosticsCollector
{
    // マスク対象は「真の秘密 (API キー / トークン / パスワード / 個人情報)」のみに限定する。
    // 以下は意図的にマスク対象から除外:
    //   - UpdateBaseUrl: Settings に [JsonIgnore] でハードコード固定、settings.json に出ない
    //   - UpdateChannel: allow-list ("release" / "prerelease") の 2 択、公開情報
    //   - IgnoreUpdateTag: Velopack リリースタグ名 (公開情報、診断時にサポート担当に見せる方が有用)
    // 新たに秘密情報を Settings に追加した場合はここに列挙すること。
    // EncryptedCompressionPassword は SensitivePatternRegex の "password" マッチでも捕捉されるが、
    // regex 経由のみだとリファクタリング時に regex を緩めると漏洩しうるため明示的に列挙する。
    private static readonly string[] _sensitiveKeys = [
        "EncryptedCompressionPassword",
    ];

    /// <summary>
    /// 診断 ZIP を指定パスに作成する。
    /// </summary>
    internal static async Task<string> ExportAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lhamiel_diag_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await CollectEnvironmentInfo(tempDir, cancellationToken);
            await CollectMaskedSettings(tempDir, cancellationToken);
            await Task.Run(() =>
            {
                CollectLogs(tempDir);
                // ダンプ (MiniDump) は support ZIP に含めない (v1.0.181+)。
                // パスワード保護機能の追加により、圧縮中クラッシュ時のメモリ snapshot には
                // スタック上の平文パスワード参照が含まれる可能性があるため、構造的に漏洩経路を塞ぐ。
                // 既存ログ (Lhamiel_*.log) でクラッシュ原因の特定は概ね可能。
            }, cancellationToken);

            var tempZip = outputPath + $".tmp_{Guid.NewGuid():N}";
            try
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, tempZip, CompressionLevel.Optimal, false), cancellationToken);
                await LockedFileRetryPolicy.ExecuteAsync(
                    () => Task.Run(() => File.Move(tempZip, outputPath, overwrite: true), cancellationToken),
                    outputPath, cancellationToken: cancellationToken);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* ベストエフォート */ }
            }

            Logger.Log($"診断 ZIP を作成しました: {outputPath}");
            return outputPath;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ベストエフォート */ }
        }
    }

    /// <summary>
    /// デフォルトの出力パス（デスクトップ）を生成。
    /// </summary>
    internal static string GetDefaultOutputPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktop))
            desktop = Path.GetTempPath();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(desktop, $"Lhamiel_Diagnostics_{timestamp}.zip");
    }

    private static async Task CollectEnvironmentInfo(string tempDir, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Lhamiel 診断情報 ===");
        sb.AppendLine($"収集日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("--- アプリケーション ---");
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        sb.AppendLine($"バージョン: {version}");
        sb.AppendLine($"プロセスパス: {Environment.ProcessPath}");
        sb.AppendLine($"起動ディレクトリ: {AppContext.BaseDirectory}");
        sb.AppendLine();

        sb.AppendLine("--- OS ---");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"アーキテクチャ: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"プロセスアーキテクチャ: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"フレームワーク: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();

        sb.AppendLine("--- ランタイム ---");
        sb.AppendLine($"プロセッサ数: {Environment.ProcessorCount}");
        sb.AppendLine($"64bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"64bit プロセス: {Environment.Is64BitProcess}");
        sb.AppendLine($"システムディレクトリ: {Environment.SystemDirectory}");
        sb.AppendLine($"ワーキングセット: {Environment.WorkingSet / 1024 / 1024} MB");
        sb.AppendLine();

        sb.AppendLine("--- パス ---");
        sb.AppendLine($"AppData: {Settings.AppDataDirectory}");
        sb.AppendLine($"TEMP: {Path.GetTempPath()}");

        var envInfoPath = Path.Combine(tempDir, "environment.txt");
        await File.WriteAllTextAsync(envInfoPath, sb.ToString(), cancellationToken);
    }

    private static async Task CollectMaskedSettings(string tempDir, CancellationToken cancellationToken)
    {
        var settingsPath = Path.Combine(Settings.AppDataDirectory, "settings.json");
        if (!File.Exists(settingsPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            var masked = MaskSensitiveValues(json);
            var outputPath = Path.Combine(tempDir, "settings_masked.json");
            await File.WriteAllTextAsync(outputPath, masked, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Log($"診断用 settings 読み取りに失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    internal static string MaskSensitiveValues(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                WriteElement(writer, doc.RootElement, null);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return "{ \"error\": \"JSON parse failed\" }";
        }
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteElement(writer, prop.Value, prop.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, null);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var str = element.GetString() ?? "";
                if (propertyName != null && ShouldMask(propertyName) && str.Length > 0)
                    writer.WriteStringValue("***");
                else
                    writer.WriteStringValue(str);
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (propertyName != null && ShouldMask(propertyName))
                    writer.WriteStringValue("***");
                else
                    element.WriteTo(writer);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool ShouldMask(string propertyName)
    {
        foreach (var key in _sensitiveKeys)
        {
            if (propertyName.Contains(key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return SensitivePatternRegex().IsMatch(propertyName);
    }

    [GeneratedRegex(@"(?i)(token|secret|password|key|credential|apikey|api_key)")]
    private static partial Regex SensitivePatternRegex();

    private static void CollectLogs(string tempDir)
    {
        var logDir = Settings.AppDataDirectory;
        if (!Directory.Exists(logDir))
            return;

        var logFiles = Directory.GetFiles(logDir, "Lhamiel_*.log");
        if (logFiles.Length == 0)
            return;

        var logsDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        // 最新 5 ファイルまで
        var recent = logFiles
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(5);

        foreach (var fi in recent)
        {
            try
            {
                CopyLogFileMasked(fi.FullName, Path.Combine(logsDir, fi.Name));
            }
            catch (FileNotFoundException)
            {
                // Logger.CleanupOldLogFiles の非同期タスクで削除された経路。
                // ベストエフォート収集なので Warning に上げない。
            }
            catch (DirectoryNotFoundException)
            {
                // 親ディレクトリごと消えた経路（手動クリーンアップ等）。
            }
            catch (Exception ex)
            {
                Logger.Log($"ログファイルコピーに失敗: {fi.Name} - {ex.Message}", LogLevel.Warning);
            }
        }
    }

    /// <summary>
    /// ログファイルを読み取り、本文に含まれる個人情報（Windows ユーザー名）をマスクしてコピーする。
    /// 診断 ZIP はサポート担当に共有されるため、ログに残る <c>C:\Users\&lt;name&gt;\…</c> の
    /// ユーザー名セグメントを伏せる。settings.json は別途 <see cref="MaskSensitiveValues"/> でマスク済み。
    /// </summary>
    private static void CopyLogFileMasked(string sourcePath, string destPath)
    {
        string content;
        using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(src))
            content = reader.ReadToEnd();

        File.WriteAllText(destPath, MaskLogPaths(content));
    }

    /// <summary>
    /// ログ本文の <c>X:\Users\&lt;name&gt;\</c> のユーザー名セグメントを <c>&lt;user&gt;</c> に置換する。
    /// パス構造（ドライブ・配下のディレクトリ）は診断価値のため残す。アーカイブ内のエントリ名
    /// （macOS 由来の <c>/Users/...</c> 等、スラッシュ区切り）はローカル PII ではないので対象外。
    /// </summary>
    internal static string MaskLogPaths(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        return UsersPathRegex().Replace(content, "$1<user>");
    }

    [GeneratedRegex(@"(?i)([A-Za-z]:\\Users\\)[^\\\r\n]+")]
    private static partial Regex UsersPathRegex();
}
