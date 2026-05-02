using System.Runtime.InteropServices;
namespace Lhamiel.Util;

/// <summary>
/// Mark of the Web (MotW) をアーカイブから展開ファイルに伝播する。
/// 元アーカイブの Zone.Identifier ADS を読み取り、展開先ファイルに書き込む。
/// </summary>
internal static class MotwPropagator
{
    private const string ZoneIdentifierSuffix = ":Zone.Identifier";

    /// <summary>
    /// 元アーカイブの Zone.Identifier を読み取る。存在しない場合は null を返す。
    /// </summary>
    internal static string? ReadZoneIdentifier(string archivePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        var adsPath = archivePath + ZoneIdentifierSuffix;
        try
        {
            if (!File.Exists(adsPath))
                return null;

            var content = File.ReadAllText(adsPath);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (Exception ex)
        {
            Logger.Log($"Zone.Identifier の読み取りに失敗: {archivePath} - {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    /// <summary>
    /// 展開先ディレクトリ内の全ファイルに Zone.Identifier を書き込む。
    /// </summary>
    internal static void PropagateToDirectory(string directoryPath, string zoneIdentifierContent, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
            return;

        var propagatedCount = 0;
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryWriteZoneIdentifier(filePath, zoneIdentifierContent))
                    propagatedCount++;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"MotW 伝播の列挙中にエラー: {directoryPath} - {ex.Message}", LogLevel.Warning);
        }

        if (propagatedCount > 0)
            Logger.Log($"MotW 伝播完了: {propagatedCount} ファイルに Zone.Identifier を付与 ({directoryPath})");
    }

    /// <summary>
    /// 単一ファイルに Zone.Identifier を書き込む。
    /// </summary>
    internal static bool TryWriteZoneIdentifier(string filePath, string zoneIdentifierContent)
    {
        try
        {
            var adsPath = filePath + ZoneIdentifierSuffix;
            LockedFileRetryPolicy.Execute(() => File.WriteAllText(adsPath, zoneIdentifierContent), filePath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Zone.Identifier の書き込みに失敗: {filePath} - {ex.Message}", LogLevel.Warning);
            return false;
        }
    }
}
