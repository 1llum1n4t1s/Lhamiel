using System.Runtime.InteropServices;
using System.Security;
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

        var adsPath = PathValidator.EnsureLongPathPrefix(archivePath) + ZoneIdentifierSuffix;
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
    /// 大量ファイル（数千〜数万）の展開で逐次実行すると遅いため、
    /// ProcessorCount に応じた並列度で並列書き込みする。
    /// </summary>
    internal static void PropagateToDirectory(string directoryPath, string zoneIdentifierContent, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
            return;

        // ジャンクション/シンボリックリンクを辿らない手書き DFS で列挙する。
        // AttributesToSkip=ReparsePoint は「結果に含めない（ShouldIncludeEntry）」だけで、
        // RecurseSubdirectories=true の再帰（ShouldRecurseIntoEntry）自体は止めない。そのため
        // 展開ツリー内に junction があると、その先（ディスク上の任意の場所、例 C:\Windows）の
        // 実ファイルにまで Zone.Identifier を書いてしまう（展開ツリー外への MotW 書き込み）。
        // 各ディレクトリを非再帰で列挙し、reparse point でないサブディレクトリだけ stack に積む
        // ことで out-of-tree 書き込みを防ぐ（ArchiveCompressor の圧縮スキャンと同じ対策）。
        var files = new List<string>();
        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            // reparse point は結果からもサブディレクトリ列挙からも除外され、stack に積まれない。
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var stack = new Stack<string>();
        stack.Push(directoryPath);
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            try
            {
                files.AddRange(Directory.EnumerateFiles(current, "*", enumOpts));
                foreach (var sub in Directory.EnumerateDirectories(current, "*", enumOpts))
                    stack.Push(sub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // 個別ディレクトリの列挙失敗はスキップして続行（全体を止めない）。
                Logger.Log($"MotW 伝播の列挙中にエラー (スキップ): {current} - {ex.Message}", LogLevel.Warning);
            }
        }

        if (files.Count == 0)
            return;

        // 並列度は ProcessorCount 上限。LockedFileRetryPolicy の Thread.Sleep が
        // ThreadPool を食いつぶさないよう、過剰並列化は避ける。
        var maxParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
        var propagatedCount = 0;
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxParallelism,
        };

        try
        {
            Parallel.ForEach(files, options, filePath =>
            {
                if (TryWriteZoneIdentifier(filePath, zoneIdentifierContent))
                    Interlocked.Increment(ref propagatedCount);
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException aex) when (aex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(cancellationToken);
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
            var adsPath = PathValidator.EnsureLongPathPrefix(filePath) + ZoneIdentifierSuffix;
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
