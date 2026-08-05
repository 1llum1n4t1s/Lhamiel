using Avalonia.Controls;
using Cube.FileSystem.SevenZip;
using System.Security;
namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// 定数: サポートされている展開形式の一覧
    /// </summary>
    internal static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz",
        ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tz"
    };

    /// <summary>
    /// 無視するシステムディレクトリ名（展開時の構造解析・圧縮時の除外で共用）
    /// </summary>
    internal static readonly HashSet<string> IgnoredSystemDirectories = new(StringComparer.OrdinalIgnoreCase) { "__MACOSX" };

    /// <summary>
    /// 無視するシステムファイル名（展開時の構造解析・圧縮時の除外で共用）
    /// </summary>
    internal static readonly HashSet<string> IgnoredSystemFiles = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "Thumbs.db", ".DS_Store" };

    /// <summary>展開フィルタ用の統合済みシステム名配列（static で1回だけ生成）</summary>
    private static readonly string[] FilterNames = [.. IgnoredSystemDirectories, .. IgnoredSystemFiles];

    /// <summary>
    /// 圧縮のみの拡張子（単体ではアーカイブでない）。.tar と組み合わせた複合拡張子のみ2段除去する。
    /// 定義は <see cref="ArchiveFormatConstants.CompressionOnlyExtensions"/> を参照する（DRY 統合）。
    /// </summary>
    private static HashSet<string> CompressionOnlyExtensions => ArchiveFormatConstants.CompressionOnlyExtensions;

    /// <summary>
    /// アーカイブファイル名から拡張子を除去したベース名を返す。
    /// 複合コンテナ（.tar.gz, .tar.xz 等）は両方除去し、それ以外は最外の拡張子のみ除去する。
    /// 例: "foo.tar.gz" → "foo", "project.zip" → "project", "foo.rar.zip" → "foo.rar"
    /// </summary>
    internal static string GetArchiveBaseName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext) || !SupportedExtensions.Contains(ext)) return name;

        // 最外の拡張子を除去
        name = Path.GetFileNameWithoutExtension(name);

        // 圧縮のみの拡張子だった場合、内側が .tar なら追加除去（.tar.gz → foo）
        if (CompressionOnlyExtensions.Contains(ext))
        {
            var innerExt = Path.GetExtension(name);
            if (string.Equals(innerExt, ".tar", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
        }

        return name;
    }

    /// <summary>
    /// 指定されたファイルがサポートされているアーカイブ形式かどうかを確認する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>サポートされている形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 指定されたパス一覧がすべてサポート対象のアーカイブファイルかどうかを判定する。
    /// フォルダや非アーカイブファイルが1つでも含まれていればfalseを返す。
    /// </summary>
    public static bool AreAllSupportedArchives(IEnumerable<string> paths)
    {
        return paths.All(p => File.Exists(p) && IsSupportedArchiveType(p));
    }

    /// <summary>
    /// アーカイブファイルの展開先ディレクトリを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>展開先ディレクトリのパス（アーカイブ名フォルダを含む）</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var baseDir = GetBaseOutputDirectory(archivePath, defaultOutputDir, outputToSameDirectory);
        return Path.Combine(baseDir, GetArchiveBaseName(archivePath));
    }

    /// <summary>
    /// 基準となる出力ディレクトリを取得（アーカイブ名フォルダを含まない）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>基準となる出力ディレクトリのパス</returns>
    public static string GetBaseOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? "";
        var baseDirectory = outputToSameDirectory ? directory : defaultOutputDir;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            // outputToSameDirectory=true でもディレクトリ部が空なら defaultOutputDir にフォールバック
            baseDirectory = string.IsNullOrWhiteSpace(defaultOutputDir) ? directory : defaultOutputDir;
        }
        return baseDirectory;
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持するデータ構造。
    /// record にすることで <c>with</c> 式による部分コピーを可能にし、プロパティ追加時の
    /// コピー漏れバグ（各呼び出し側で全プロパティを列挙するパターン）を防ぐ。
    /// </summary>
    public record ArchiveStructureInfo
    {
        /// <summary>
        /// フォルダ作成をスキップすべきか。
        /// ルートフォルダがアーカイブ名と一致する場合、フォルダを作成すると二重ネストになるためスキップする。
        /// </summary>
        public bool ShouldSkipFolderCreation { get; init; }

        /// <summary>
        /// ルートレベルが単一アイテムの場合、その名前（上書き確認パス精密化用）
        /// </summary>
        public string? SingleRootItemName { get; init; }

        /// <summary>
        /// 展開時に使われた <c>CreateArchiveNameFolder</c> 設定のスナップショット値。
        /// 展開中にユーザーが設定を変更しても、完了後の「開くフォルダ」決定と矛盾しないよう
        /// <see cref="FolderOpener.OpenExtractionResult"/> にここの値を渡す。
        /// null の場合は現在の設定値が使われる（下位互換）。
        /// </summary>
        public bool? CapturedCreateArchiveNameFolder { get; init; }

        /// <summary>
        /// アーカイブ内の非圧縮サイズ合計（バイト）。<see cref="GetArchiveStructureInfo"/> が
        /// reader.Items を走査するついでに計算するため、別途 <c>DiskSpaceChecker.GetArchiveUncompressedSize</c>
        /// を呼ぶ必要はない。-1 の場合は未計算（取得失敗または旧経路）。
        /// </summary>
        public long TotalUncompressedSize { get; init; } = -1;

        /// <summary>
        /// ルートレベルの全アイテム名。MotW 伝播で multi-root アーカイブの各アイテムに個別適用するために使用。
        /// </summary>
        public IReadOnlyList<string> RootItemNames { get; init; } = [];

        /// <summary>
        /// アーカイブを開けず構造を解析できなかったことを示す (codex P2 #3384706128)。
        /// 7z のヘッダ暗号化 (he=on) はパスワード無しだと ctor が SevenZipException (IsNotArc) を
        /// 投げ、7z.dll 自体が「暗号化ヘッダ」と「破損」を区別できない (実機確認済み)。
        /// 呼び出し側 (ArchiveProcessor) はこのフラグ + 拡張子 (7z/rar) でパスワード再試行を判断する。
        /// </summary>
        public bool OpenFailed { get; init; }
    }

    /// <summary>
    /// アーカイブの構造を一度の解析で取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="password">ヘッダ暗号化 (he=on) アーカイブを開くためのパスワード (通常は null)</param>
    /// <returns>解析結果を格納したArchiveStructureInfo</returns>
    public static ArchiveStructureInfo GetArchiveStructureInfo(string archivePath, string? password = null)
    {
        if (!File.Exists(archivePath))
        {
            return new ArchiveStructureInfo();
        }

        try
        {
            // ネイティブ 7z.dll 直列化ゲート（reader より外側で取得して生成→使用→Dispose を覆う）
            using var nativeGate = NativeArchiveGate.Enter();
            using var reader = password is null
                ? new ArchiveReader(archivePath)
                : new ArchiveReader(archivePath, password, new ArchiveOption());
            var structure = ParseArchiveRootLevel(reader);

            var rootFolders = structure.RootFolders;
            var rootFiles = structure.RootFiles;

            var allRootItems = new HashSet<string>(rootFolders, StringComparer.OrdinalIgnoreCase);
            allRootItems.UnionWith(rootFiles);
            var hasSingleRootItem = allRootItems.Count == 1;
            var singleRootItemName = hasSingleRootItem ? allRootItems.FirstOrDefault() : null;

            var archiveName = GetArchiveBaseName(archivePath);

            // ルートフォルダのみ（ファイルなし）でアーカイブ名と一致 → フォルダ作成すると二重ネストになるのでスキップ
            var shouldSkipFolderCreation = rootFolders.Count == 1 && rootFiles.Count == 0 &&
                string.Equals(rootFolders.First(), archiveName, StringComparison.OrdinalIgnoreCase);

            if (shouldSkipFolderCreation)
                Logger.Log($"アーカイブ名と一致するルートフォルダを検出（フォルダ作成スキップ）: {rootFolders.First()}");

            return new ArchiveStructureInfo
            {
                ShouldSkipFolderCreation = shouldSkipFolderCreation,
                SingleRootItemName = singleRootItemName,
                TotalUncompressedSize = structure.TotalUncompressedSize,
                RootItemNames = [.. allRootItems]
            };
        }
        catch (Exception ex)
        {
            // パスワード付き再解析の失敗時は例外メッセージをログへ出さない (codex P2 #3385301557)。
            // ライブラリ例外のテキストに入力パスワードが混入する可能性があり、Extract は既存書庫
            // 互換のため 1〜3 文字パスワードも受理する = Logger redaction (4 文字下限) では守れない。
            // パスワード無し (通常経路) は従来どおりメッセージを残して破損診断に使う。
            Logger.Log(password is null
                ? $"アーカイブ構造解析エラー: {ex.Message}"
                : $"アーカイブ構造解析エラー (パスワード付き再解析): {ex.GetType().Name} (HResult=0x{ex.HResult:X8})");
            return new ArchiveStructureInfo { OpenFailed = true };
        }
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持する内部データ構造
    /// </summary>
    private class ArchiveStructure
    {
        /// <summary>
        /// プロパティ: ルートレベルのフォルダ名のセット
        /// </summary>
        public HashSet<string> RootFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: ルートレベルのファイル名のセット
        /// </summary>
        public HashSet<string> RootFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 非圧縮サイズ合計（バイト）。reader.Items のループで同時集計する。
        /// </summary>
        public long TotalUncompressedSize { get; set; }
    }

    private const string TempDirPrefix = "Lhamiel_";

    /// <summary>
    /// 一時ディレクトリを作成する。suffixで用途を区別する。
    /// </summary>
    private static string CreateTempDirectory(string suffix, string? basePath = null)
    {
        var dir = Path.Combine(basePath ?? Path.GetTempPath(), $"{TempDirPrefix}{suffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 展開用の一時ディレクトリを、可能なら <paramref name="outputPath"/> と同一ボリューム
    /// （その親ディレクトリ）に作成する。
    /// </summary>
    /// <remarks>
    /// 一時ディレクトリの中身は最終的に <see cref="MoveDirectoryContents"/> で
    /// <paramref name="outputPath"/> へ移動される。<see cref="Directory.Move"/> は<b>同一ボリューム内
    /// でのみ</b>機能し、別ボリュームだと <c>ERROR_NOT_SAME_DEVICE</c> (IOException) を投げる。
    /// さらに別ボリュームではコピーになり 2 倍の空き容量と時間を要する。出力ボリュームに
    /// 一時ディレクトリを置くことで、移動を高速・確実なリネームにする。
    /// 出力ボリュームへの作成に失敗した場合のみ %TEMP% にフォールバックする
    /// （その場合はサブディレクトリを含む書庫で移動が失敗しうるが、従来挙動を踏襲）。
    /// </remarks>
    private static string CreateExtractionTempDirectory(string outputPath)
    {
        var baseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(outputPath));
        if (!string.IsNullOrEmpty(baseDir))
        {
            try
            {
                if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
                return CreateTempDirectory("Extract", baseDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"出力ボリュームへの一時ディレクトリ作成に失敗、%TEMP% にフォールバック: {baseDir}, {ex.Message}", LogLevel.Warning);
            }
        }
        return CreateTempDirectory("Extract");
    }

    /// <summary>
    /// ディレクトリの直下の全アイテム（サブディレクトリ・ファイル）を宛先に移動する
    /// </summary>
    private static void MoveDirectoryContents(string sourceDir, string destDir)
    {
        // ライブ列挙だと親ディレクトリのハンドルを保持したまま子を移動するため、
        // AV/EDR フィルタドライバが過敏な環境で稀に AccessDenied を誘発する。
        // 先に配列化して列挙ハンドルを閉じてから移動する。
        var dirs = Directory.GetDirectories(sourceDir);
        var files = Directory.GetFiles(sourceDir);
        foreach (var dir in dirs)
            MoveWithRetry(() => Directory.Move(dir, Path.Combine(destDir, Path.GetFileName(dir))), dir);
        foreach (var file in files)
            MoveWithRetry(() => File.Move(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true), file);
    }

    private static void MoveWithRetry(Action moveAction, string sourcePath)
        => LockedFileRetryPolicy.Execute(moveAction, sourcePath);

    /// <summary>
    /// 個別エントリの展開を試行する（一時ディレクトリからの移動/コピー）。
    /// 指数バックオフ付きリトライで一時的なロック等に対応する。
    /// 全リトライ失敗時は false を返し、<paramref name="skipRelativePaths"/> に追加する。
    /// </summary>
    internal static async Task<(bool success, Exception? lastError)> TryExtractEntryAsync(
        string tempPath, string outputPath, string relativePath, bool isDirectory,
        HashSet<string>? skipRelativePaths = null,
        int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        try
        {
            await LockedFileRetryPolicy.ExecuteAsync(
                () => Task.Run(
                    () => FileOperations.CopyExtractedItem(tempPath, outputPath, relativePath, isDirectory, overwrite: true),
                    cancellationToken),
                relativePath, maxAttempts: maxRetries, initialDelayMs: 200, cancellationToken: cancellationToken);
            return (true, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"エントリ展開が全リトライ失敗: {relativePath} - {ex.Message}", LogLevel.Error);
            skipRelativePaths?.Add(relativePath);
            return (false, ex);
        }
    }

    /// <summary>
    /// パス内に無視対象のシステムディレクトリが含まれるかチェック（ゼロアロケーション）
    /// </summary>
    private static bool ContainsIgnoredDirectory(ReadOnlySpan<char> path)
    {
        while (path.Length > 0)
        {
            var sepIndex = path.IndexOfAny('/', '\\');
            var segment = sepIndex < 0 ? path : path[..sepIndex];
            if (segment.Length > 0)
            {
                foreach (var dir in IgnoredSystemDirectories)
                {
                    if (segment.Equals(dir.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            path = sepIndex < 0 ? [] : path[(sepIndex + 1)..];
        }
        return false;
    }

    /// <summary>
    /// アーカイブのルートレベルのフォルダ・ファイルを解析する
    /// </summary>
    private static ArchiveStructure ParseArchiveRootLevel(ArchiveReader reader)
    {
        var structure = new ArchiveStructure();

        foreach (var item in reader.Items)
        {
            // 非圧縮サイズの集計はディレクトリエントリを除外して 1 度のループで完結させる。
            // (long) キャストで item.Length が int の場合のオーバーフローも防ぐ。
            if (!item.IsDirectory)
                structure.TotalUncompressedSize += (long)item.Length;

            var path = item.FullName.AsSpan();

            // 最初のセグメント（ルート名）を切り出す（配列アロケーション不要）
            var firstSep = path.IndexOfAny('/', '\\');
            var rootName = (firstSep < 0 ? path : path[..firstSep]).ToString();
            var hasSubPath = firstSep >= 0 && firstSep < path.Length - 1;

            if (rootName.Length == 0) continue;
            if (IgnoredSystemDirectories.Contains(rootName)) continue;

            // ファイル名（最後のセグメント）のシステムファイルチェック
            if (!item.IsDirectory)
            {
                var lastSep = path.LastIndexOfAny('/', '\\');
                var fileName = lastSep < 0 ? rootName : path[(lastSep + 1)..].ToString();
                if (IgnoredSystemFiles.Contains(fileName)) continue;
            }

            if (!hasSubPath && !item.IsDirectory)
                structure.RootFiles.Add(rootName);
            else
                structure.RootFolders.Add(rootName);
        }

        return structure;
    }

    /// <summary>
    /// パス境界チェックに使う OS 依存の文字列比較モード。
    /// Windows は case-insensitive（NTFS 既定）、Linux/macOS は case-sensitive。
    /// 全プラットフォームで <see cref="StringComparison.OrdinalIgnoreCase"/> を使うと
    /// case-sensitive FS で <c>../output/evil</c> 形の traversal を許してしまうため、
    /// 実ファイルシステムの挙動に合わせた比較を採用する。
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// パス境界チェック用に <paramref name="basePath"/> を絶対化 + 末尾セパレータ付与した形に正規化する。
    /// エントリ単位のループで繰り返し正規化するコストを避けるため、ループ外で 1 度だけ呼び出すのが想定用途。
    /// </summary>
    internal static string NormalizeBaseDirectory(string basePath) =>
        Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <summary>
    /// アーカイブ内のエントリ名を展開先の相対パスとして安全に解決する（呼び出しごとに basePath を正規化する版）。
    /// 単発呼び出し用。ループ内からの呼び出しには <see cref="TryResolveSafeEntryPathFromNormalized"/> を使い、
    /// <see cref="NormalizeBaseDirectory"/> でループ外に正規化コストを追い出すこと。
    /// </summary>
    /// <param name="basePath">展開先のベースディレクトリ（未正規化可）</param>
    /// <param name="entryName">アーカイブ内のエントリ名（攻撃者制御）</param>
    /// <param name="safeFullPath">境界内に収まる結合済み絶対パス</param>
    /// <returns>境界内なら true、境界を超える場合は false</returns>
    internal static bool TryResolveSafeEntryPath(string basePath, string entryName, out string safeFullPath)
    {
        try
        {
            return TryResolveSafeEntryPathFromNormalized(NormalizeBaseDirectory(basePath), entryName, out safeFullPath);
        }
        catch
        {
            safeFullPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// <see cref="NormalizeBaseDirectory"/> で事前正規化したベースディレクトリを使って境界チェックを行う。
    /// アーカイブ内エントリをループ走査する場面では、1 回だけ正規化して本メソッドを繰り返し呼ぶことで
    /// <see cref="Path.GetFullPath(string)"/> の繰り返しコストを避けられる。
    /// </summary>
    /// <param name="normalizedBase">末尾セパレータ付きの絶対パス（<see cref="NormalizeBaseDirectory"/> の戻り値）</param>
    /// <param name="entryName">アーカイブ内のエントリ名（攻撃者制御）</param>
    /// <param name="safeFullPath">境界内に収まる結合済み絶対パス</param>
    /// <param name="normalizeUnicode">NFC 正規化を適用するか（呼び出し元の設定スナップショットから渡す）</param>
    internal static bool TryResolveSafeEntryPathFromNormalized(string normalizedBase, string entryName, out string safeFullPath, bool normalizeUnicode = true)
    {
        safeFullPath = string.Empty;
        if (string.IsNullOrEmpty(entryName)) return false;

        // Zip Slip ガード: パス区切り・絶対パス・UNC・ドライブレター・デバイスパスを拒否
        // 先頭 `/` `\` チェックでカバーされる攻撃面:
        //   - UNC パス: `\\server\share`、`\\?\C:\...`、`\\.\COM1` など先頭 `\\` 系は全て弾く
        //   - 絶対パス先頭の `/`（POSIX 風）も弾く
        // この後の Path.IsPathRooted で `C:\...` 等のドライブレター付き絶対パスを弾く。
        // 順序が重要: 先頭文字チェックを先に行うことで、後の正規化で `\` が削られて
        //            UNC が相対パスに化ける経路をブロックしている。
        if (entryName.StartsWith('/') || entryName.StartsWith('\\')) return false;
        if (Path.IsPathRooted(entryName)) return false;

        // NTFS 代替データストリーム (ADS) 拒否: `file.txt:hidden:$DATA` のように `:` を含むエントリ名は
        // Windows で ADS として書き込まれ、検索やウイルス対策が見落とす隠しファイルが作成される。
        // 7z.dll が `:` を含むエントリ名を返すアーカイブが実在しうるため、明示的に拒否する。
        // ドライブレター（`C:`）は既に Path.IsPathRooted で弾かれているのでここに到達しない。
        if (entryName.Contains(':')) return false;

        // `/` と `\` の両方を Path.DirectorySeparatorChar に統一する。
        // Linux/macOS では `\` がファイル名として正当なため Path.GetFullPath が
        // 区切りと認識せず、アーカイブ内の `a\..\b` が traversal として解釈されない
        // リスクがある。両方を事前置換して Path.GetFullPath の境界判定に確実に乗せる。
        var normalized = entryName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        // Unicode NFC 正規化: macOS HFS+ は NFD でファイル名を保存するため、
        // macOS 作成アーカイブの NFD エントリ名を NTFS 向けに NFC 変換する。
        if (normalizeUnicode && !normalized.IsNormalized(System.Text.NormalizationForm.FormC))
            normalized = normalized.Normalize(System.Text.NormalizationForm.FormC);

        try
        {
            // basePath が相対パスでも CWD に依存しないよう、Path.GetFullPath(path, basePath)
            // オーバーロード（.NET Core 2.1+）で事前正規化済みベースを使って結合する。
            var combined = Path.GetFullPath(normalized, normalizedBase);

            // 比較は OS 依存。case-sensitive FS で `../output/evil` 形のケース違い traversal が
            // 通らないよう、実際のファイルシステムの挙動に合わせる。
            if (!combined.StartsWith(normalizedBase, PathComparison) &&
                !string.Equals(combined, normalizedBase.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
            {
                return false;
            }

            safeFullPath = combined;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// アーカイブ内のファイルと展開先の既存ファイルを突き合わせて衝突を検出する。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="normalizeUnicode">ファイル名を Unicode NFC 正規化して突き合わせるか</param>
    /// <param name="password">ヘッダ暗号化 (he=on) アーカイブの一覧取得に使うパスワード (通常は null)</param>
    /// <returns>衝突するファイルの競合グループリスト。衝突がなければ空リスト</returns>
    public static List<Models.FileConflictGroup> DetectExtractionConflicts(string archivePath, string outputPath, bool normalizeUnicode = true, string? password = null)
    {
        var conflicts = new List<Models.FileConflictGroup>();

        try
        {
            // ネイティブ 7z.dll 直列化ゲート（reader より外側で取得して生成→使用→Dispose を覆う）
            using var nativeGate = NativeArchiveGate.Enter();
            using var reader = password is null
                ? new ArchiveReader(archivePath)
                : new ArchiveReader(archivePath, password, new ArchiveOption());
            // outputPath の絶対化はループ外で 1 回だけ行い、ループ内は正規化済み版を使う
            var normalizedOutputBase = NormalizeBaseDirectory(outputPath);

            foreach (var item in reader.Items)
            {
                var relativePath = item.FullName.Replace('\\', '/');

                // システムファイル・ディレクトリの除外（ディレクトリエントリの末尾 '/' を除いて名前判定）
                var fileName = Path.GetFileName(relativePath.TrimEnd('/'));
                if (IgnoredSystemFiles.Contains(fileName)) continue;
                if (ContainsIgnoredDirectory(relativePath)) continue;

                // Zip Slip ガード: outputPath 境界外に出るエントリ（`..` / 絶対パス / UNC）はスキップ
                if (!TryResolveSafeEntryPathFromNormalized(normalizedOutputBase, relativePath, out var destFilePath, normalizeUnicode))
                {
                    Logger.Log($"展開衝突検出で境界外パスを検出しスキップ: {relativePath}", LogLevel.Warning);
                    continue;
                }
                // ディレクトリエントリは Path.GetFullPath が末尾区切りを残すことがあるので、
                // 実体（ファイル/ディレクトリ）判定の前に除去して既存ファイルと突き合わせられるようにする。
                destFilePath = TrimTrailingSeparators(destFilePath);

                // 宛先に存在する実体を 1 回の stat で判定する（ファイルかディレクトリか + サイズ/更新日時）。
                var existing = ProbeExistingEntry(destFilePath);

                // NFC 正規化 ON の場合、destFilePath は NFC 形のパスになる。一方、ライブラリの
                // reader.Save は生のエントリ名 (macOS 由来は NFD) でファイルを書くため、過去に展開した
                // 既存ファイルは NFD 形で残っていることがある。NFC 形だけで存在チェックすると、その
                // NFD 既存ファイルとの衝突を取りこぼし「上書き確認なし」で消えてしまう。生エントリ形でも
                // 突き合わせて、NFD/NFC のズレで衝突警告が抜けないようにする（MotW 伝播は実体列挙のため影響なし）。
                if (!existing.Exists && normalizeUnicode &&
                    TryResolveSafeEntryPathFromNormalized(normalizedOutputBase, relativePath, out var rawDestPath, normalizeUnicode: false) &&
                    !string.Equals(rawDestPath, destFilePath, StringComparison.Ordinal))
                {
                    var rawTrimmed = TrimTrailingSeparators(rawDestPath);
                    var rawProbe = ProbeExistingEntry(rawTrimmed);
                    if (rawProbe.Exists)
                    {
                        destFilePath = rawTrimmed;
                        existing = rawProbe;
                    }
                }

                if (!existing.Exists) continue;

                // パス型衝突の判定:
                //  - アーカイブのファイルエントリ × 既存ファイル/ディレクトリ → 衝突（上書き / 型衝突）。
                //  - アーカイブのディレクトリエントリ × 既存ファイル → 衝突（型衝突）。
                //  - アーカイブのディレクトリエントリ × 既存ディレクトリ → 衝突にしない（マージされ、
                //    配下の個別ファイルはそれぞれのエントリで検出される）。
                // 旧実装は item.IsDirectory を continue で飛ばし、かつ FileInfo.Exists が
                // ディレクトリに対して false だったため、dir↔file の型衝突を両方向とも取りこぼし、
                // 直接展開経路（parentWindow==null: CLI / 関連付け / アイコンドロップ）で
                // 上書き確認なしに既存を破壊していた。temp フォルダ経路の DetectFileSystemConflicts と判定を揃える。
                if (item.IsDirectory && existing.IsDirectory) continue;

                // 衝突発見: 左=アーカイブ内エントリ（ソース）、右=既存ファイル/ディレクトリ（宛先）
                var archiveEntry = new Models.FileConflictEntry(
                    archivePath, relativePath, item.IsDirectory ? 0 : item.Length, item.LastWriteTime);
                var existingEntry = new Models.FileConflictEntry(
                    destFilePath, relativePath, existing.Size, existing.LastWrite);

                conflicts.Add(new Models.FileConflictGroup
                {
                    ConflictingName = relativePath,
                    Entries = [archiveEntry, existingEntry]
                });
            }
        }
        catch (Exception ex)
        {
            // パスワード付きで開いた場合は例外メッセージをログへ出さない (GetArchiveStructureInfo の
            // 同種対応 codex P2 #3385301557 と同じ契約: 1〜3 文字パスワードは redaction 対象外のため、
            // ライブラリ例外テキスト経由の平文混入をログ出力側で構造的に防ぐ)。
            Logger.Log(password is null
                ? $"展開衝突検出でエラー: {ex.Message}"
                : $"展開衝突検出でエラー (パスワード付き): {ex.GetType().Name} (HResult=0x{ex.HResult:X8})");
        }

        return conflicts;
    }

    /// <summary>
    /// 末尾のディレクトリ区切り（<c>\</c> / <c>/</c>）を除去する。ルート（例 <c>C:\</c>）を
    /// 削り切って空文字にならないよう、全部消えた場合は元の文字列を返す。
    /// </summary>
    private static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>
    /// 指定パスに存在する実体を 1 回の stat で判定する（ファイル / ディレクトリ / サイズ / 更新日時）。
    /// File.Exists + Directory.Exists + FileInfo の多重 stat を避けるため File.GetAttributes を 1 回だけ呼ぶ。
    /// </summary>
    private static (bool Exists, bool IsDirectory, long Size, DateTime LastWrite) ProbeExistingEntry(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0)
                return (true, true, 0, Directory.GetLastWriteTime(path));
            var info = new FileInfo(path);
            return (true, false, info.Length, info.LastWriteTime);
        }
        catch (FileNotFoundException) { return (false, false, 0, default); }
        catch (DirectoryNotFoundException) { return (false, false, 0, default); }
        catch (IOException) { return (false, false, 0, default); }
        catch (UnauthorizedAccessException) { return (false, false, 0, default); }
        catch (ArgumentException) { return (false, false, 0, default); }
        // File.GetAttributes / FileInfo は、長すぎるパス・無効文字・CAS / Mark-of-the-Web 拒否
        // 等で SecurityException や NotSupportedException も投げる。"存在しない扱い" に倒すのは
        // 「衝突なし=ダイアログを出さない」を意味し、その後の展開で正規エラー経路に流れるので
        // ここで未捕捉のままアプリを落とすより安全 (gemini レビュー指摘)。
        catch (System.Security.SecurityException) { return (false, false, 0, default); }
        catch (NotSupportedException) { return (false, false, 0, default); }
    }

    /// <summary>
    /// 上書き確認ダイアログを表示すべきかどうかを判定する（親フォルダ直下展開時は実際に上書きされるパスのみで判定）
    /// </summary>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定。親フォルダ直下展開時は実際に上書きされるパスのみ渡す）</param>
    /// <returns>上書き対象が存在する場合true</returns>
    public static bool ShouldShowOverwriteDialog(string outputPath, IReadOnlyList<string>? overwriteCheckPaths)
    {
        return overwriteCheckPaths is { Count: > 0 }
            ? overwriteCheckPaths.Any(Path.Exists)
            : Path.Exists(outputPath);
    }

    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定）</param>
    /// <param name="progressWindow">進捗表示ウィンドウ（マーキー切替や通知の表示に使う。null なら表示しない）</param>
    /// <param name="precomputedUncompressedSize">構造解析で集計済みの非圧縮サイズ。-1 ならここで再集計する</param>
    /// <param name="normalizeUnicode">展開時にファイル名を Unicode NFC 正規化するか</param>
    /// <param name="knownPassword">構造解析時に検証済みのパスワード (he=on 7z 等)。初回のパスワード要求をダイアログなしでこの値で応答する</param>
    /// <param name="suppressPasswordPrompt">展開中の AsyncPasswordQuery でダイアログを出さずに失敗させるか（構造解析側で既に試行済みの場合の二重プロンプト防止）</param>
    /// <param name="onPasswordPrompted">展開中の AsyncPasswordQuery でユーザーがパスワードを入力するたびに呼ばれるコールバック (7z.dll 由来のスレッドから呼ばれる)。呼び出し側 (ArchiveProcessor) が自身の catch/finally 寿命での redaction 登録と CRC 検証用パスワードの捕捉に使う (codex P2 #3386876537/#3386876542)</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, Window? parentWindow = null, CancellationToken cancellationToken = default, IReadOnlyList<string>? overwriteCheckPaths = null, View.ProgressWindow? progressWindow = null, long precomputedUncompressedSize = -1, bool normalizeUnicode = true, string? knownPassword = null, bool suppressPasswordPrompt = false, Action<string>? onPasswordPrompted = null)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}");
        cancellationToken.ThrowIfCancellationRequested();

        // 展開前のディスク容量チェック（アーカイブのメタデータ上の非圧縮サイズ）。
        // GetArchiveStructureInfo で既に reader.Items を 1 周しており、その時点で集計済みの
        // サイズを引き回すことで「同じアーカイブを 2 回開いて Items 列挙」を回避する（#10 軽量統合）。
        // 旧経路や呼び出し元が事前計算していない場合（precomputedUncompressedSize < 0）は
        // 従来通り DiskSpaceChecker 側で再計算する（後方互換）。
        var requiredSize = precomputedUncompressedSize >= 0
            ? precomputedUncompressedSize
            : DiskSpaceChecker.GetArchiveUncompressedSize(archivePath);
        if (requiredSize > 0)
        {
            var hasSpace = await DiskSpaceChecker.EnsureDiskSpaceAsync(
                outputPath, requiredSize, parentWindow, cancellationToken);
            if (!hasSpace)
                throw new OperationCanceledException(App.Text("Error.DiskSpaceCancelled"));
        }

        // 展開中のランタイム容量監視（Zip bomb 対策 / 悪意あるメタデータサイズへの保険）
        // operationCts と linkedCts で外側のキャンセルとも連携する。
        // requiredSize <= 0 の場合（メタデータが読めない / 空アーカイブ）は相対的な「必要量の
        // 10%」基準での判定が意味を持たないため 0 を渡し、DiskSpaceChecker 側で絶対閾値
        // （MinFreeSpaceThresholdBytes）のみの判定にフォールバックさせる。
        //
        // 戻り値の IDisposable は PeriodicCheckDisposable（DiskSpaceChecker 内部の型）で、
        // Dispose() 内で checkCts.Cancel() を呼ぶ実装になっているため、通常の
        // CancellationTokenSource のように「Dispose ではキャンセルされない」問題はない。
        // 内部の linkedCts は Task.Run の using スコープで別途破棄される。
        // using でスコープを抜けた時点で監視 Task.Run は確実に停止する。
        using var extractCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var periodicCheck = DiskSpaceChecker.StartPeriodicCheck(
            outputPath, requiredSize > 0 ? requiredSize : 0, parentWindow, extractCts);
        cancellationToken = extractCts.Token;

        // 展開先に既存ファイルがあるかチェック（一時展開方式の判定）
        var hasExistingFiles = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (hasExistingFiles && parentWindow != null)
        {
            // 一時フォルダ方式: 一時展開 → 衝突検出 → ダイアログ → 移動
            await ExtractViaTempFolderAsync(archivePath, outputPath, progress, parentWindow, cancellationToken, progressWindow, normalizeUnicode, knownPassword, suppressPasswordPrompt, onPasswordPrompted);
        }
        else
        {
            // 衝突なし: 直接展開
            await Task.Run(async () =>
            {
                var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
                try
                {
                    await ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, false, cancellationToken, overwriteCheckPaths, null, normalizeUnicode, knownPassword, suppressPasswordPrompt, onPasswordPrompted);
                }
                finally
                {
                    NativeInteropHelper.KeepAliveCallbacks(progressCallback, progress);
                }
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 一時フォルダ方式で展開する。
    /// ①一時フォルダに全展開 → ②衝突検出 → ③ダイアログ表示 → ④選択結果に基づいて移動
    /// </summary>
    private static async Task ExtractViaTempFolderAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress, Window parentWindow, CancellationToken cancellationToken, View.ProgressWindow? progressWindow, bool normalizeUnicode = true, string? knownPassword = null, bool suppressPasswordPrompt = false, Action<string>? onPasswordPrompted = null)
    {
        // 一時フォルダを出力先ディレクトリ直下に作成（同一ドライブでFile.Moveが高速、かつ書き込み権限が確実）
        // outputPathがファイルの場合は親ディレクトリを使用
        var tempBaseDir = File.Exists(outputPath) ? (Path.GetDirectoryName(outputPath) ?? Path.GetTempPath()) : outputPath;
        var tempDir = CreateTempDirectory("Temp", tempBaseDir);
        Logger.Log($"一時フォルダ方式: tempDir={tempDir}");

        try
        {
            // ① 一時フォルダに展開（注意書き表示）
            progressWindow?.SetNotice(App.Text("Progress.ConflictNotice"));

            await Task.Run(async () =>
            {
                var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
                try
                {
                    await ExtractArchive(archivePath, tempDir, progressCallback, null, false, cancellationToken, null, null, normalizeUnicode, knownPassword, suppressPasswordPrompt, onPasswordPrompted);
                }
                finally
                {
                    NativeInteropHelper.KeepAliveCallbacks(progressCallback, progress);
                }
            }, cancellationToken);

            progressWindow?.ClearNotice();
            cancellationToken.ThrowIfCancellationRequested();

            // ② 一時フォルダ vs 展開先を比較して衝突検出
            var conflicts = DetectFileSystemConflicts(tempDir, outputPath);
            HashSet<string>? skipArg = null;

            if (conflicts.Count > 0)
            {
                Logger.Log($"一時展開後のファイル衝突: {conflicts.Count}件");

                // ③ ダイアログ表示（両方実在するのでサムネイル完全対応）
                var (result, selectedFiles) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                if (result == Models.FileConflictResult.Cancel)
                {
                    Logger.Log("ユーザーが展開をキャンセル");
                    throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"));
                }

                // ④ 選択結果に基づいて移動
                // 左ペイン（一時フォルダ側=アーカイブから展開済み）が選択されたファイルのみ上書き移動する
                // fullPath が tempDir 内にあるものが左ペイン（アーカイブ側）の選択
                var archiveSideSelected = new HashSet<string>(
                    selectedFiles
                        .Where(f => f.fullPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
                        .Select(f => f.relativePath),
                    StringComparer.OrdinalIgnoreCase);

                // 衝突ファイルのうちアーカイブ側が選択されなかったもの = スキップ（既存を保持）
                var skipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var conflict in conflicts)
                {
                    if (!archiveSideSelected.Contains(conflict.ConflictingName))
                        skipPaths.Add(conflict.ConflictingName);
                }

                if (skipPaths.Count > 0)
                    Logger.Log($"ユーザーが {skipPaths.Count} 件のファイルをスキップ指定");

                skipArg = skipPaths.Count > 0 ? skipPaths : null;
            }

            // ④ ファイル配置（衝突有無共通）
            progressWindow?.SetIndeterminate(App.Text("Progress.MovingFiles"));
            try
            {
                await MoveExtractedFilesAsync(tempDir, outputPath, skipArg, cancellationToken);
            }
            finally
            {
                progressWindow?.UpdateProgress(100);
            }
        }
        finally
        {
            // 一時フォルダを削除
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    Logger.Log($"一時フォルダ削除完了: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"一時フォルダ削除失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// ファイルシステム上の2つのディレクトリを比較して衝突を検出する。
    /// </summary>
    private static List<Models.FileConflictGroup> DetectFileSystemConflicts(string sourceDir, string destDir)
    {
        var conflicts = new List<Models.FileConflictGroup>();

        if (!Directory.Exists(sourceDir))
            return conflicts;

        // 宛先がファイルの場合（アーカイブ名フォルダと同名のファイルが存在）は
        // そのファイル自体を衝突として報告し、内部ファイルの個別チェックはスキップ
        if (File.Exists(destDir))
        {
            var fileInfo = new FileInfo(destDir);
            var destName = Path.GetFileName(destDir);
            conflicts.Add(new Models.FileConflictGroup
            {
                ConflictingName = destName,
                Entries =
                [
                    new Models.FileConflictEntry(sourceDir, destName, 0, Directory.GetLastWriteTime(sourceDir)),
                    new Models.FileConflictEntry(destDir, destName, fileInfo.Length, fileInfo.LastWriteTime)
                ]
            });
            return conflicts;
        }

        if (!Directory.Exists(destDir))
            return conflicts;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');
            var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            // ファイル同士の衝突、またはファイル↔ディレクトリのパス型衝突を検出。
            // File.Exists + Directory.Exists + FileInfo で 3〜4 回 stat が走るのを避けるため、
            // File.GetAttributes を 1 回だけ呼んで属性フラグで分岐する。
            FileAttributes destAttrs;
            try { destAttrs = File.GetAttributes(destFile); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            // システムファイル・ディレクトリをスキップ
            var fileName = Path.GetFileName(relativePath);
            if (IgnoredSystemFiles.Contains(fileName)) continue;
            if (ContainsIgnoredDirectory(relativePath)) continue;

            var sourceInfo = new FileInfo(sourceFile);

            // 宛先がファイルかディレクトリかを FileAttributes で判定
            long destSize;
            DateTime destLastWrite;
            if ((destAttrs & FileAttributes.Directory) != 0)
            {
                destSize = 0;
                // ディレクトリの最終更新日時は Directory.GetLastWriteTime を使うと FileInfo 経由より syscall が軽い
                destLastWrite = Directory.GetLastWriteTime(destFile);
            }
            else
            {
                var destInfo = new FileInfo(destFile);
                destSize = destInfo.Length;
                destLastWrite = destInfo.LastWriteTime;
            }

            // 左=アーカイブから展開されたファイル、右=既存ファイル/ディレクトリ
            var archiveEntry = new Models.FileConflictEntry(
                sourceFile, relativePath, sourceInfo.Length, sourceInfo.LastWriteTime);
            var existingEntry = new Models.FileConflictEntry(
                destFile, relativePath, destSize, destLastWrite);

            conflicts.Add(new Models.FileConflictGroup
            {
                ConflictingName = relativePath,
                Entries = [archiveEntry, existingEntry]
            });
        }

        return conflicts;
    }

    /// <summary>
    /// 一時フォルダから展開先にファイルを移動する。
    /// 同一ドライブならFile.Moveで瞬時、異なるドライブならコピー＋削除。
    /// </summary>
    private static Task MoveExtractedFilesAsync(string sourceDir, string destDir, HashSet<string>? skipPaths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            // 宛先がファイルの場合（パス型衝突）
            if (File.Exists(destDir))
            {
                // ユーザーが既存ファイルの保持を選択した場合はスキップ（移動しない）
                var destFileName = Path.GetFileName(destDir);
                if (skipPaths != null && skipPaths.Contains(destFileName))
                {
                    Logger.Log($"宛先ファイルを保持（ユーザー選択）: {destDir}");
                    return;
                }

                File.Delete(destDir);
            }
            Directory.CreateDirectory(destDir);

            // 空ディレクトリも保持するため、先にディレクトリ構造を作成する。
            // （ファイル側ループ内の CreateDirectory は親ディレクトリにしか届かないため、
            //   子孫空ディレクトリを確実に保持するにはこの 1 回の走査が必要）
            foreach (var sourceSubDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relDir = Path.GetRelativePath(sourceDir, sourceSubDir);
                var destSubDir = Path.Combine(destDir, relDir);
                if (!Directory.Exists(destSubDir))
                    Directory.CreateDirectory(destSubDir);
            }

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');

                // スキップ対象チェック
                if (skipPaths != null && skipPaths.Contains(relativePath))
                    continue;

                var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                // 親ディレクトリは上のディレクトリ構造作成ループで既に作られているため
                // CreateDirectory の重複呼び出しは基本的に不要。ただし空ディレクトリ列挙で
                // 親が拾えないエッジケース（権限等）に備えて、未存在時のみ作成する。
                var destFileDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDir) && !Directory.Exists(destFileDir))
                    Directory.CreateDirectory(destFileDir);

                // パス型衝突: 宛先にディレクトリがあるがソースはファイルの場合は削除
                if (Directory.Exists(destFile))
                    Directory.Delete(destFile, recursive: true);

                // 上書き移動（ReadOnly属性がある場合は解除してから実行、失敗時はロールバック）
                FileAttributes originalAttrs = 0;
                var clearedReadOnly = false;
                if (File.Exists(destFile))
                {
                    originalAttrs = File.GetAttributes(destFile);
                    if ((originalAttrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(destFile, originalAttrs & ~FileAttributes.ReadOnly);
                        clearedReadOnly = true;
                    }
                }
                try
                {
                    MoveWithRetry(() => File.Move(sourceFile, destFile, overwrite: true), sourceFile);
                }
                catch
                {
                    // 移動失敗時: ReadOnly属性を元に戻す
                    if (clearedReadOnly && File.Exists(destFile))
                        try { File.SetAttributes(destFile, originalAttrs); } catch { /* ベストエフォート */ }
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// <see cref="ArchiveReader"/> の生成（内部で <c>FormatFactory.From(string)</c> がアーカイブを
    /// 排他オープンする）が「使用中（SHARING_VIOLATION）」例外で失敗するケースを救済する。
    ///
    /// よくある原因:
    /// <list type="bullet">
    ///   <item>圧縮終了直後で 7z 自身がまだファイルを掴んでいる</item>
    ///   <item>Windows Defender / Windows Indexer / Explorer プレビューが瞬間的にロック中</item>
    ///   <item>クラウドストレージ同期クライアントが書き込み完了直後にハッシュ計算でロック</item>
    /// </list>
    ///
    /// これらは数百 ms 以内に解放されることが多いので、<see cref="LockedFileRetryPolicy.Execute{T}"/>
    /// で指数バックオフリトライする（3 回 / 200ms→400ms）。永続的ロックや破損ファイルは
    /// <see cref="LockedFileRetryPolicy.IsTransientLockError"/> が false を返すので即時 throw される。
    ///
    /// 同期版を使う理由: 旧実装は内側の <c>Task.Run</c> で reader を生成し、呼び出し側は
    /// <c>await</c> の継続スレッドで <c>Save</c> / <c>Dispose</c> していたため、生成と使用が
    /// 別のスレッドプールスレッドに分かれていた。ライブラリ (1llum1n4t1s.Sevenzip) の契約は
    /// 1.0.84 で「同時に触るスレッドは常に 1 つ」へ緩和され、<see cref="NativeArchiveGate"/> で
    /// 直列化していればスレッドを跨いでも合法になったが、同期化しておくと reader の全寿命が
    /// 1 スレッドに収まって追いやすく、<c>Task</c> 割り当てと async ステートマシンも 1 つ減る。
    /// 呼び出し元は既に <c>Task.Run</c> 配下（<see cref="ExtractArchiveAsync"/> 参照）で、直後の
    /// <c>reader.Save</c> が同じスレッドを長時間占有するため、ここでブロックしても問題ない。
    /// リトライ待機も <see cref="System.Threading.Thread.Sleep(int)"/> 直呼びではなく
    /// <see cref="WaitHandle"/> ベースのキャンセル対応版なので、CT の応答性は落ちない。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="passwordQuery">パスワード要求時に呼ばれるコールバック</param>
    /// <param name="extractOption">展開オプション</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>生成された <see cref="ArchiveReader"/></returns>
    private static ArchiveReader OpenArchiveReaderWithRetry(
        string archivePath,
        AsyncPasswordQuery passwordQuery,
        ArchiveOption extractOption,
        CancellationToken cancellationToken) =>
        // maxAttempts / initialDelayMs は非同期版の既定値 (3 回 / 200ms) を明示継承する。
        // 同期版の既定は 6 回 / 50ms 起点で挙動が変わってしまうため省略しない。
        LockedFileRetryPolicy.Execute(
            () => new ArchiveReader(archivePath, passwordQuery, extractOption),
            archivePath,
            maxAttempts: 3,
            initialDelayMs: 200,
            cancellationToken: cancellationToken);

    public static async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, IReadOnlyList<string>? overwriteCheckPaths = null, HashSet<string>? skipRelativePaths = null, bool normalizeUnicode = true, string? knownPassword = null, bool suppressPasswordPrompt = false, Action<string>? onPasswordPrompted = null)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(App.Text("Error.ArchiveNotFound", archivePath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ファイル単位の衝突検出（上位で未確認の場合）
        var outputOrOverwriteExists = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (outputOrOverwriteExists)
        {
            if (!overwriteConfirmed)
            {
                var conflicts = DetectExtractionConflicts(archivePath, outputPath, normalizeUnicode, knownPassword);
                if (conflicts.Count > 0)
                {
                    Logger.Log($"ExtractArchive内でファイル衝突を検出: {conflicts.Count}件");
                    var (result, _) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                    if (result == Models.FileConflictResult.Cancel)
                        throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"));
                }
            }

            // 保護されたディレクトリ（デスクトップ自体など）の場合は上書き確認（削除）をさせない
            // overwriteCheckPaths がある場合は実際に退避・削除される各パスをチェック、
            // ない場合は outputPath 自体が退避・削除対象となるのでそちらをチェック
            var pathsToProtect = overwriteCheckPaths is { Count: > 0 }
                ? (IEnumerable<string>)overwriteCheckPaths
                : [outputPath];
            foreach (var protectPath in pathsToProtect)
            {
                if (PathValidator.IsProtectedDirectory(protectPath))
                {
                    Logger.Log($"上書き不可: 保護されたディレクトリです: {protectPath}", LogLevel.Warning);
                    throw new InvalidOperationException($"'{protectPath}' はシステムによって保護されているため、上書き展開できません。別の場所を選択してください。");
                }
            }
        }

        var tempOutputPath = CreateExtractionTempDirectory(outputPath);

        // 通常 tempOutputPath は outputPath と同一ボリュームに作られる（CreateExtractionTempDirectory）。
        // ただし出力ボリュームへの作成に失敗して %TEMP% にフォールバックした場合は別ドライブに
        // なりうる。その場合に TEMP 側の空き容量枯渇を外側の periodicCheck（outputPath 監視）では
        // 検出できないため、両者が別ドライブのときのみ TEMP 側も並行監視する（同一ドライブなら省略）。
        // requiredBytes は上位で確保済みなので 0 を渡し、絶対閾値のみで判定。
        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tempDriveRoot = Path.GetPathRoot(tempOutputPath);
        var outputDriveRoot = Path.GetPathRoot(outputPath);
        IDisposable? tempPeriodicCheck = null;
        if (!string.IsNullOrEmpty(tempDriveRoot) &&
            !string.IsNullOrEmpty(outputDriveRoot) &&
            !string.Equals(tempDriveRoot, outputDriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            tempPeriodicCheck = DiskSpaceChecker.StartPeriodicCheck(
                tempOutputPath, 0, parentWindow, innerCts);
        }
        cancellationToken = innerCts.Token;

        // パスワード取得の中止追跡フラグ。詳細コメントは下の passwordQuery 構築部を参照。
        // open 時 (he=on) の EncryptionException を OCE に変換する catch フィルタから
        // 参照するため、try ブロックの外で宣言する (try 内宣言は catch 句から見えない)。
        var passwordAcquisitionCancelled = 0;

        // 展開中プロンプトで入力されたパスワードの redaction scope (codex P2 #3386876537)。
        // ヘッダ可視の暗号化アーカイブ (パスワード ZIP / he=off 7z) は AsyncPasswordQuery が
        // 平文パスワードを知る唯一の経路で、登録しないと下の generic catch がライブラリ例外
        // 由来の詳細 (errorInfo.Details = ex.Message 等) を生ログしてしまう。
        // 7z.dll 由来のコールバックスレッドから追加し finally (catch のログ後) で解放するため、
        // リスト自身を lock に使う。redaction 不能な 1〜3 文字入力 (Extract は既存書庫互換で
        // 受理) は hasUnredactablePromptedPassword 経由で catch 側のログ詳細を抑止する。
        var promptedPasswordRedactions = new List<IDisposable>();
        var hasUnredactablePromptedPassword = false;

        // redaction 不能 (4 文字未満) のパスワードが scope にあるかの共通判定。
        // 該当する catch (汎用 / 非キャンセル OCE 昇格) は例外詳細を生ログせず
        // 型名 + HResult の要約に置換する (codex P2 #3386732834 / #3386876537 / #3390292697)。
        bool HasUnredactablePasswordInScope()
        {
            bool prompted;
            lock (promptedPasswordRedactions)
            {
                prompted = hasUnredactablePromptedPassword;
            }
            return (knownPassword is not null && !Logger.CanRedactToken(knownPassword)) || prompted;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extractOption = new ArchiveOption { Filter = Filter.From(FilterNames) };

            // パスワード保護アーカイブ対応:
            // 7z.dll は暗号化エントリに遭遇した時点で ICryptoGetTextPassword コールバックを呼ぶ。
            // AsyncPasswordQuery 経由で UI ダイアログに委譲する。
            // 誤入力時は 7z.dll が同じハンドラを再度呼ぶため、attemptCount で UI に再試行表示を出す。
            // ExtractArchive は既に Task.Run 配下で呼ばれている（ExtractArchiveAsync 参照）ので
            // AsyncPasswordQuery の「UI スレッドから呼ぶな」制約に抵触しない。
            //
            // キャンセル追跡の必要性:
            // パスワード取得が中止される条件は 2 つ:
            //   (1) ユーザーがダイアログで Cancel を押す → ShowFromBackgroundAsync が null
            //   (2) 再試行上限（MaxPasswordAttempts）を超過 → 自動キャンセル扱い
            // どちらも空文字を 7z.dll に返すため EncryptionException が投げられる。
            // 「パスワードが違います」という誤解を招く通知を回避するため、
            // 上記いずれかが発生したことをフラグ追跡し、Save() 後に
            // OperationCanceledException に変換する。
            var archiveName = Path.GetFileName(archivePath);
            var attemptCount = 0;
            // passwordAcquisitionCancelled は PasswordDialog のコールバックスレッド（7z.dll 由来）から書き込まれ、
            // reader.Save() 例外ハンドラ側のスレッドから読み取られるため、
            // volatile 相当のメモリ可視性保証が必要。attemptCount と対称に Interlocked で扱う。
            // 命名: 「ユーザーキャンセル」だけでなく「再試行上限超過」も 1 で表す（パスワード取得が中止された
            //       原因を問わず追跡）。旧名 userCancelledPassword は前者だけを示唆するため改名。
            // 宣言自体は open 時の catch フィルタから参照するため try の外にある（上記参照）。
            //
            // CS1628 について（レビュー bot の誤検知対策コメント）:
            // この変数は直下のラムダ（passwordQuery）にキャプチャされるため、コンパイラによって
            // closure クラスのフィールドに hoist される。`Interlocked.Increment(ref ...)` /
            // `Volatile.Read(ref ...)` は closure フィールドへの ref を取るが、これは合法。
            // CS1628 は async メソッドの ref/out パラメータを await を跨いで使う場合の制限であり、
            // 「captured local への ref」とは別。Volatile.Read は同期 catch when フィルタ内で
            // 使われており、await を跨ぐ可能性もない。実ビルドも 0 errors / 0 warnings。
            //
            // 再試行上限: 悪意あるアーカイブや構造的に誤判定されるアーカイブでの無限ダイアログループを防ぐ
            const int MaxPasswordAttempts = 3;
            var passwordQuery = new AsyncPasswordQuery(async _ =>
            {
                var currentAttempt = System.Threading.Interlocked.Increment(ref attemptCount);
                var isRetry = currentAttempt > 1;

                // 構造解析時に検証済みのパスワード (he=on 7z) があれば初回はダイアログなしで応答する。
                // 万一不一致なら 7z.dll が再度コールバックするので 2 回目以降は通常のダイアログに進む。
                if (currentAttempt == 1 && knownPassword is not null)
                {
                    return knownPassword;
                }

                // 構造解析段階でパスワード試行上限まで失敗している場合、ここで再びダイアログを
                // 出すと「上限 3 回 + さらに 3 回」の二重プロンプトになる (codex P2 #3386575724)。
                // 追加ダイアログなしでキャンセル扱いにする (本当に破損したアーカイブはこの
                // コールバック自体が呼ばれずエラー表示経路に進むため影響なし)。
                if (suppressPasswordPrompt)
                {
                    Logger.Log("構造解析でパスワード試行上限に達しているため、追加ダイアログなしで展開を中止します", LogLevel.Warning);
                    System.Threading.Interlocked.Exchange(ref passwordAcquisitionCancelled, 1);
                    return string.Empty;
                }

                // 上限を超えたら自動キャンセル扱い（null 返しと同じ経路）
                if (currentAttempt > MaxPasswordAttempts)
                {
                    Logger.Log($"パスワード入力上限（{MaxPasswordAttempts}回）を超えたため展開を中止します", LogLevel.Warning);
                    System.Threading.Interlocked.Exchange(ref passwordAcquisitionCancelled, 1);
                    return string.Empty;
                }

                // cancellationToken を渡し、展開キャンセル時に PasswordDialog が画面に残らないようにする。
                // ArchiveProcessor.PasswordDialogImpl 経由でテスト時に差し替え可能。
                // ダイアログ表示そのものは構造解析プロンプト (ArchiveProcessor) と共有の葉ゲートで
                // 直列化し、混在バッチでモーダルが積み重ならないようにする (codex P2 #3386575715)。
                // ここは NativeArchiveGate 保持中なので、取得順は常に
                // 「NativeArchiveGate → ダイアログゲート (葉)」の一方向のみ。
                string? pw;
                await ArchiveProcessor.ExtractionPasswordDialogGate.WaitAsync(cancellationToken);
                try
                {
                    pw = await ArchiveProcessor.PasswordDialogImpl.PromptForPasswordAsync(
                        archiveName, View.PasswordDialogMode.Extract, isRetry, parentWindow, cancellationToken);
                }
                finally
                {
                    ArchiveProcessor.ExtractionPasswordDialogGate.Release();
                }
                if (pw is null)
                {
                    System.Threading.Interlocked.Exchange(ref passwordAcquisitionCancelled, 1);
                    // 空文字を返すと AsyncPasswordQuery 側で Cancel=true にマップされる
                    return string.Empty;
                }
                // 入力パスワードを 7z.dll に渡す前に redaction 登録する (codex P2 #3386876537)。
                // 誤入力でもライブラリ例外メッセージ経由でログに混入しうるため全試行を登録する。
                lock (promptedPasswordRedactions)
                {
                    promptedPasswordRedactions.Add(Logger.RegisterRedactionToken(pw));
                    if (!Logger.CanRedactToken(pw))
                        hasUnredactablePromptedPassword = true;
                }
                // 呼び出し側 (ArchiveProcessor) にも通知し、上位 catch/finally 寿命での
                // redaction 登録と CRC 検証用パスワードの捕捉を可能にする (codex P2 #3386876542)。
                onPasswordPrompted?.Invoke(pw);
                return pw;
            }, cancellationToken);

            // ネイティブ側（7z.dll）との連携を確実に保護するため
            // using スコープ内で reader と progress を管理する。
            // `new ArchiveReader` 内部で FormatFactory.From がアーカイブを排他オープンするが、
            // 圧縮直後の自プロセスロックや Defender / Indexer の瞬間ロックで
            // SHARING_VIOLATION (0x80070020) が出ることがあるので OpenArchiveReaderWithRetry で
            // 指数バックオフリトライする（200ms → 400ms、計 3 回）。
            // ネイティブ 7z.dll 直列化ゲート: ライブラリ (1llum1n4t1s.Sevenzip) の共有シングルトン
            // SevenZipLibrary は ArchiveReader の並行動作をサポートしないため、reader の
            // 生成 → Save → Dispose 全体を 1 スロットに直列化する（バッチ展開の IoBoundParallelism
            // 並列実行時もネイティブ接触が重ならないよう保証）。reader 生成前に取得し、reader の
            // using より外側で取得することで「Acquire → 使用 → Dispose」全体を覆う。
            //
            // スレッド固定: ゲート取得の await から復帰したスレッド上で「生成 → Save → Dispose」を
            // 完結させる。OpenArchiveReaderWithRetry は同期メソッドで、この using ブロック内に
            // await は 1 つも無い（ライブラリ 1.0.84 の契約はスレッドを跨いでも直列なら合法だが、
            // await を足すと nativeGate の保持中に別の非ネイティブ処理が挟まってゲートの
            // 保持時間が伸びるので、ここには await を置かない）。
            using (var nativeGate = await NativeArchiveGate.EnterAsync(cancellationToken))
            using (var reader = OpenArchiveReaderWithRetry(archivePath, passwordQuery, extractOption, cancellationToken))
            {
                Logger.Log($"一時ディレクトリへの展開処理開始: {archivePath} -> {tempOutputPath}");

                // Zip Slip プリチェック: reader.Save() が全エントリを展開する前に、
                // アーカイブ内の全エントリ名が tempOutputPath 境界内に収まるかを検証する。
                // （DetectExtractionConflicts は衝突検出専用で、境界外エントリをスキップするだけなので
                //   ここで改めて全エントリを検証して安全性を担保する）
                // tempOutputPath の絶対化はループ外で 1 回だけ行う。
                var normalizedTempBase = NormalizeBaseDirectory(tempOutputPath);
                foreach (var item in reader.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = item.FullName ?? string.Empty;
                    if (string.IsNullOrEmpty(entryName)) continue;

                    // Zip Slip ガードを最優先で実行。攻撃者が `__MACOSX/../../evil.txt` のような
                    // エントリ名を仕込んだ場合、`ContainsIgnoredDirectory` が先頭の `__MACOSX` を
                    // 検出して無視判定を下すと、本来境界外へ書き込まれるエントリの検証が
                    // スキップされてしまう。フィルタリングより前にセキュリティ境界を確定させる。
                    if (!TryResolveSafeEntryPathFromNormalized(normalizedTempBase, entryName, out _, normalizeUnicode))
                    {
                        Logger.Log($"危険なエントリ名を検出しアーカイブ展開を中止: {entryName}", LogLevel.Warning);
                        throw new InvalidOperationException(
                            App.Text("Error.ZipSlipDetected", entryName));
                    }

                    // Zip Slip チェック通過後にライブラリ側フィルタ（__MACOSX / .DS_Store 等）と
                    // 歩調を合わせた無視判定。ここで continue しても上記セキュリティ境界は既に
                    // 通過済みなので、攻撃者が親ディレクトリ名を偽装してもバイパスにはならない。
                    var fileName = Path.GetFileName(entryName);
                    if (IgnoredSystemFiles.Contains(fileName)) continue;
                    if (ContainsIgnoredDirectory(entryName.Replace('\\', '/'))) continue;
                }

                // 進捗コールバックの有無に関わらず CancellableProgress<Report> を介して
                // reader.Save() に cancellationToken を接続する。旧実装は progressCallback が
                // null のとき `reader.Save(tempOutputPath)` を直接呼んでいたため、
                // DiskSpaceChecker の定期チェック等で extractCts.Cancel() が発火しても
                // ネイティブ展開処理がキャンセルを受け取らず走り続ける問題があった。
                var throttler = progressCallback != null ? new ProgressThrottler() : null;
                using var progress = new CancellableProgress<Report>(report =>
                {
                    if (progressCallback is null || throttler is null) return;
                    var percentage = (int)(report.GetRatio() * 100);
                    if (throttler.ShouldReport(percentage))
                        progressCallback(new ProgressInfo(percentage, ""));
                }, cancellationToken);

                try
                {
                    reader.Save(tempOutputPath, progress);
                }
                catch (EncryptionException) when (System.Threading.Volatile.Read(ref passwordAcquisitionCancelled) == 1)
                {
                    // ユーザーがパスワードダイアログで Cancel を押した結果、または再試行上限超過で
                    // 自動キャンセルされた結果としての EncryptionException。
                    // 「パスワードが違います」ではなく通常のキャンセル扱いにする。
                    // Volatile.Read で別スレッドの書き込みを確実に可視化する。
                    Logger.Log("パスワード入力がキャンセルされたため展開を中止します");
                    // cancellationToken を OCE に紐付ける。CT が既にキャンセル済みの場合は
                    // 呼び出し元の Task が TaskStatus.Canceled に正しく遷移する。
                    // CT が未キャンセル（純粋にユーザーがダイアログ Cancel を押しただけ）でも
                    // OCE.CancellationToken プロパティに記録されるため診断情報が向上する。
                    throw CreatePasswordCancelledOce(cancellationToken);
                }

                // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                cancellationToken.ThrowIfCancellationRequested();

                // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                progressCallback?.Invoke(new ProgressInfo(100, ""));

                // ネイティブ側のコールバック完了を確実に保証
                NativeInteropHelper.KeepAliveCallbacks(progress, progressCallback);

                // reader自体の生存も保証
                NativeInteropHelper.KeepAliveCallbacks(reader);
            }

            // スキップ対象ファイルを一時ディレクトリから削除（移動前に除外）。
            // 攻撃者制御のエントリ名 `../foo` などで tempOutputPath 外のファイルを削除されないよう
            // TryResolveSafeEntryPath で境界内に収まっているかを必ず検証する。
            if (skipRelativePaths is { Count: > 0 })
            {
                Logger.Log($"スキップ対象の {skipRelativePaths.Count} ファイルを一時ディレクトリから除外");
                // ループ外で 1 回だけ正規化してから繰り返し境界チェックする
                var normalizedSkipBase = NormalizeBaseDirectory(tempOutputPath);
                foreach (var relativePath in skipRelativePaths)
                {
                    if (!TryResolveSafeEntryPathFromNormalized(normalizedSkipBase, relativePath, out var tempFilePath, normalizeUnicode))
                    {
                        Logger.Log($"スキップ対象の境界外パスを拒否: {relativePath}", LogLevel.Warning);
                        continue;
                    }
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                            Logger.Log($"スキップ: {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"スキップファイルの削除に失敗: {relativePath}, {ex.Message}");
                        }
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 最終的な展開先への移動処理（原子性のため既存は削除せず退避し、移動成功後にバックアップを削除）
            Logger.Log($"一時ディレクトリから最終展開先へ移動します: {tempOutputPath} -> {outputPath}");
            var backupPaths = new List<(string Original, string Backup)>();

            // 上書きが許可された（または確認済み）の場合は既存の対象を退避（削除せず移動で原子性を確保）
            try
            {
                if (overwriteCheckPaths is { Count: > 0 })
                {
                    // 親フォルダ直下展開時: 実際に上書きされるパスのみ退避（outputPathは退避しない）
                    foreach (var path in overwriteCheckPaths)
                    {
                        var moved = MoveExistingToBackup(path, backupPaths);
                        if (moved)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                else
                {
                    // 複数ルート等でoutputPathを新規作成する場合: outputPathを退避してから作成
                    MoveExistingToBackup(outputPath, backupPaths);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"既存対象の退避に失敗しました: {ex.Message}");
                // 退避を途中まで行った分を元へ戻してから中止する（一部だけ退避された
                // 状態で放置すると、その原本が .Lhamiel_backup_<guid> に残ったまま消える）。
                RestoreFromBackup(backupPaths);
                throw new InvalidOperationException(App.Text("Error.PreparationFailed"), ex);
            }

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            Logger.Log("一時ディレクトリの内容を最終展開先に移動します");

            try
            {
                MoveDirectoryContents(tempOutputPath, outputPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"一時ディレクトリの内容移動に失敗しました: {ex.Message}");
                // 退避済みの原本を元の場所へ戻す（原子性の完成）。退避だけで復元しないと、
                // 失敗時に原本が .Lhamiel_backup_<guid> に退避されたまま宛先が空/部分になり、
                // ユーザーは「失敗＝元のまま」と誤認したまま原本を失う。
                RestoreFromBackup(backupPaths);
                throw new InvalidOperationException(App.Text("Error.MoveFailed"), ex);
            }

            // 移動成功後のみバックアップを削除（原子性の完了）
            foreach (var (_, backupPath) in backupPaths)
            {
                try
                {
                    if (Directory.Exists(backupPath))
                    {
                        RemoveReadOnlyAttributes(backupPath);
                        Directory.Delete(backupPath, true);
                    }
                    else if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Logger.Log($"バックアップの削除に失敗しました（手動削除可能）: {backupPath}, {ex.Message}", LogLevel.Warning);
                }
            }

            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");

        }
        catch (Exception ex) when (ex is EncryptionException or SevenZipException
            && System.Threading.Volatile.Read(ref passwordAcquisitionCancelled) == 1)
        {
            // パスワード取得が中止された (ダイアログキャンセル / 再試行上限 / 構造解析上限による
            // 抑止 codex P2 #3386575724) 結果の失敗。reader.Save 周りの EncryptionException catch は
            // open 時 (he=on はヘッダ復号のため open 中にパスワードコールバックが走る) を覆わない上、
            // open 時のキャンセルは CryptoGetTextPassword が SevenZipCode.Cancel を返すだけで
            // cb.Exceptions に記録されず SevenZipException (IsNotArc) になる (EncryptionException
            // ではない)。フラグが立っている = この失敗は中止に起因するので、種別を問わず通常の
            // キャンセル扱いに変換する。一時ディレクトリは finally が掃除する。
            Logger.Log("パスワード取得が中止されたため展開を中止します");
            throw CreatePasswordCancelledOce(cancellationToken);
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested && oce.InnerException is not null)
        {
            // ユーザー主導でないキャンセル。ライブラリ (1llum1n4t1s.Sevenzip 1.0.73) が I/O 失敗
            // (ディスク満杯・デバイス切断等) を OperationCanceledException で包んで返す経路があり、
            // これをキャンセル扱いすると本当の失敗が握り潰される。一時ディレクトリを掃除した上で
            // 内側の実例外を昇格させてエラーとして処理する（修正済みライブラリでは
            // SevenZipException で返るためこの経路には入らない）。
            // redaction 不能な短パスワードが scope にあるときは InnerException.Message を
            // 生ログしない (codex P2 #3390292697、汎用 catch と同じ契約)。
            if (!HasUnredactablePasswordInScope())
                Logger.Log($"非キャンセル要因の中断を検出、内部例外へ昇格: {oce.InnerException.Message}");
            else
                Logger.Log($"非キャンセル要因の中断を検出、内部例外へ昇格 (パスワード付き・詳細抑止): {oce.InnerException.GetType().Name} (HResult=0x{oce.InnerException.HResult:X8})");
            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"中断時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {ex.Message}", LogLevel.Warning);
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(oce.InnerException);
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"展開処理がキャンセルされました。一時ディレクトリを削除: {tempOutputPath}");

            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"キャンセル時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {ex.Message}", LogLevel.Warning);
            }
            throw;
        }
        catch (Exception ex)
        {
            // 一時ディレクトリのクリーンアップ
            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Logger.Log($"エラー発生時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {cleanupEx.Message}", LogLevel.Warning);
            }

            var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, archivePath, outputPath);
            // 1〜3 文字のパスワード (knownPassword / 展開中プロンプト入力) は Logger redaction
            // (4 文字下限) の対象外のため、ライブラリ例外由来の詳細 (errorInfo.Details =
            // ex.Message 等) を生ログしない (codex P2 #3386732834 / #3386876537)。
            // redaction 可能なら従来どおり (ログ側でマスクされる)。
            if (!HasUnredactablePasswordInScope())
            {
                Logger.Log($"アーカイブ展開でエラーが発生しました: {errorInfo.Message}");
                Logger.Log($"エラー詳細: {errorInfo.Details}");
            }
            else
            {
                Logger.Log($"アーカイブ展開でエラーが発生しました (パスワード付き・詳細抑止): {ex.GetType().Name} (HResult=0x{ex.HResult:X8})");
            }

            throw;
        }
        finally
        {
            // TEMP ドライブ監視を停止（開始していない場合は no-op）
            tempPeriodicCheck?.Dispose();

            // 一時展開ディレクトリを確実に掃除する。CreateExtractionTempDirectory は temp を
            // outputPath と同一ボリューム（その親 = outputPath の親ディレクトリ）に作るため、
            // 成功時に MoveDirectoryContents で中身を移しても空の temp ディレクトリが残る。
            // これを放置すると出力先に `<prefix>Extract<guid>` の空フォルダが溜まる（バッチ展開時は
            // 出力ディレクトリ直下に展開数だけ残る）。成功・失敗・キャンセルいずれの経路でも
            // ここで best-effort 削除する（エラー/キャンセル経路の catch が先に削除済みなら no-op）。
            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"一時展開ディレクトリの最終掃除に失敗しました（手動削除可能）: {tempOutputPath}, {ex.Message}", LogLevel.Warning);
            }

            // 展開中プロンプト入力パスワードの redaction を解放する。finally は同レベル catch の
            // ログ出力後に走るため、ここのエラーログはマスク済みで出力されている。呼び出し元
            // (ArchiveProcessor) の catch は onPasswordPrompted 経由で登録された自前の scope が守る。
            lock (promptedPasswordRedactions)
            {
                foreach (var scope in promptedPasswordRedactions)
                    scope.Dispose();
                promptedPasswordRedactions.Clear();
            }
        }
    }


    /// <summary>
    /// 既存のファイルまたはディレクトリを退避用バックアップパスへ移動する（原子性のため削除せず移動）
    /// </summary>
    /// <param name="path">退避対象のパス（ファイルまたはディレクトリ）</param>
    /// <param name="backups">退避元と退避先のペアを追加するリスト</param>
    /// <returns>退避を行った場合はtrue、対象が存在しなかった場合はfalse</returns>
    private static bool MoveExistingToBackup(string path, List<(string Original, string Backup)> backups)
    {
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
            return false;

        var backupPath = path + ".Lhamiel_backup_" + Guid.NewGuid().ToString("N");
        if (isDirectory)
        {
            RemoveReadOnlyAttributes(path);
            MoveWithRetry(() => Directory.Move(path, backupPath), path);
        }
        else
        {
            MoveWithRetry(() => File.Move(path, backupPath), path);
        }
        backups.Add((path, backupPath));
        return true;
    }

    /// <summary>
    /// 「パスワード関連でキャンセルされた」ことを示すマーカー キー。
    /// OCE.Data に乗せて呼び出し側 (<see cref="ArchiveProcessor"/>) に区別可能なシグナルを伝える。
    /// <see cref="DiskSpaceChecker"/> の <c>extractCts.Cancel()</c> 由来 OCE と判別するための
    /// sentinel (CodeRabbit レビュー指摘)。
    /// </summary>
    internal const string PasswordCancelledOceDataKey = "Lhamiel.PasswordCancelled";

    /// <summary>
    /// パスワード関連キャンセル用の OCE を <see cref="PasswordCancelledOceDataKey"/> sentinel 付きで生成する。
    /// </summary>
    private static OperationCanceledException CreatePasswordCancelledOce(CancellationToken cancellationToken)
    {
        var oce = new OperationCanceledException(App.Text("Error.UserCancelledExtraction"), cancellationToken);
        oce.Data[PasswordCancelledOceDataKey] = true;
        return oce;
    }

    /// <summary>
    /// 退避済みバックアップを元のパスへ戻す（移動段で失敗したときのロールバック）。
    /// 退避だけ実装して復元が無いと、上書き展開が移動段でコケたとき原本が
    /// <c>.Lhamiel_backup_&lt;guid&gt;</c> サイドファイルに退避されたまま宛先が空/部分になり、
    /// ユーザーは「失敗＝元のまま」と誤認したまま原本を失う（実質データ損失）。
    /// 圧縮側（<c>ArchiveProcessor</c> の atomic swap）と同様に「移動先の残骸を除去 →
    /// バックアップを元へ戻す」best-effort 復元を行う。復元できなかったバックアップは
    /// 削除せず保持し、手動復旧の余地を残す。
    /// </summary>
    private static void RestoreFromBackup(List<(string Original, string Backup)> backups)
    {
        // LIFO (登録逆順) で復元する。バックアップ作成は親 → 子の順で登録される可能性があり
        // (例: `a/`, `a/b/`, `a/b/c.txt` を退避すると Move 時にこの順序で entries が積まれる)、
        // 戻すときは逆順 (子 → 親) で処理しないと、親ディレクトリ復元時にまだ残っている
        // 子側の残骸と衝突する。ファイルシステム操作のロールバックは常に LIFO 順で行うのが
        // 堅牢な実践 (gemini レビュー指摘)。
        for (var index = backups.Count - 1; index >= 0; index--)
        {
            var (original, backup) = backups[index];
            try
            {
                // 移動段で original 側へ書き込まれた残骸を先に除去してから戻す
                // （残骸が残っていると Directory.Move/File.Move が失敗するため）。
                // ファイル残骸・ディレクトリ残骸とも read-only 属性を先に解除する。
                // 展開で MotW 由来 read-only ファイルが original へ移動済みのケースでは、
                // 属性を解除せず File.Delete すると UnauthorizedAccessException で残骸が残り、
                // 直後の File.Exists(original) が true のままバックアップ復元を見送ってしまう
                // （上書き失敗時に原本を失う実質データ損失、codex P2 #3389... 指摘）。
                try
                {
                    if (File.Exists(original))
                    {
                        RemoveReadOnlyAttributes(original);
                        File.Delete(original);
                    }
                    else if (Directory.Exists(original))
                    {
                        RemoveReadOnlyAttributes(original);
                        Directory.Delete(original, true);
                    }
                }
                catch (Exception residueEx) when (residueEx is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Logger.Log($"復元前の残骸除去に失敗しました: {original} ({residueEx.Message})", LogLevel.Warning);
                }

                // 残骸を除去できたときだけ復元する（残骸が残ったまま move すると失敗するため）。
                if (!File.Exists(original) && !Directory.Exists(original))
                {
                    if (Directory.Exists(backup))
                        MoveWithRetry(() => Directory.Move(backup, original), backup);
                    else if (File.Exists(backup))
                        MoveWithRetry(() => File.Move(backup, original), backup);
                    Logger.Log($"バックアップから復元しました: {backup} -> {original}");
                }
                else
                {
                    Logger.Log($"残骸を除去できず復元を見送りました（バックアップは保持）: {backup}", LogLevel.Warning);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"バックアップからの復元に失敗しました（手動復旧可能）: {backup} -> {original} ({ex.Message})", LogLevel.Warning);
            }
        }
    }

    /// <summary>
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    internal static void RemoveReadOnlyAttributes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                    fileInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            else if (Directory.Exists(path))
            {
                RemoveReadOnlyAttributesIterative(new DirectoryInfo(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Logger.Log($"読み取り専用属性の削除処理でエラーが発生しました: {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定されたディレクトリとその内容に対して、反復的に読み取り専用属性を解除します
    /// </summary>
    /// <param name="dirInfo">対象ディレクトリの DirectoryInfo インスタンス</param>
    private static void RemoveReadOnlyAttributesIterative(DirectoryInfo dirInfo)
    {
        if (!dirInfo.Exists) return;

        // スタックベースの反復処理（深い階層でのスタックオーバーフロー防止）
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dirInfo);

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();

            try
            {
                if (currentDir.Attributes.HasFlag(FileAttributes.ReadOnly))
                    currentDir.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"ディレクトリ属性変更失敗: {currentDir.FullName}: {ex.Message}");
            }

            try
            {
                foreach (var file in currentDir.GetFiles())
                {
                    try
                    {
                        if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                            file.Attributes &= ~FileAttributes.ReadOnly;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        Logger.Log($"ファイル属性変更エラー（無視）: {file.FullName}: {ex.Message}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"ディレクトリアクセスエラー（ファイル属性変更中）: {currentDir.FullName}: {ex.Message}", LogLevel.Warning);
            }

            try
            {
                foreach (var subDir in currentDir.GetDirectories())
                    stack.Push(subDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"サブディレクトリアクセスエラー: {currentDir.FullName}: {ex.Message}", LogLevel.Warning);
            }
        }
    }
}
