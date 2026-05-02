namespace Lhamiel.Util;

/// <summary>
/// ファイルパスの検証を行うユーティリティクラス
/// </summary>
public static class PathValidator
{
    // Windows予約デバイス名
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // 不正文字セットをキャッシュ（毎回配列生成を避ける）
    private static readonly HashSet<char> InvalidPathCharSet = new(Path.GetInvalidPathChars());
    private static readonly HashSet<char> InvalidFileNameCharSet = new(Path.GetInvalidFileNameChars());

    private const int MaxPathLength = 260;
    private const int MaxDirectoryLength = 248;
    private const int MaxFilenameLength = 255;
    private const string LongPathPrefix = @"\\?\";
    private const string UncLongPathPrefix = @"\\?\UNC\";

    /// <summary>
    /// ファイルパスが有効かどうかを検証する
    /// </summary>
    /// <param name="filePath">検証するファイルパス</param>
    /// <param name="errorMessage">エラーメッセージ（エラーがある場合）</param>
    /// <returns>有効な場合はtrue、無効な場合はfalse</returns>
    public static bool IsValidFilePath(string filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = App.Text("Validation.PathEmpty");
            return false;
        }

        // パス長の検証
        if (!ValidatePathLength(filePath, out errorMessage))
            return false;

        // 不正な文字の検証
        if (!ValidateInvalidCharacters(filePath, out errorMessage))
            return false;

        // パストラバーサルの検証
        if (!ValidatePathTraversal(filePath, out errorMessage))
            return false;

        // 予約デバイス名の検証
        if (!ValidateReservedNames(filePath, out errorMessage))
            return false;

        return true;
    }

    /// <summary>
    /// ファイルパスが指定された基準ディレクトリ内にあるかどうかを検証する。
    /// Zip Slip ガードは <c>ArchiveExtractor.TryResolveSafeEntryPathFromNormalized</c> を使うこと。
    /// 本メソッドは末尾セパレータを強制付与してプレフィックス衝突（例: C:\Users\Bob と C:\Users\Bob-evil）
    /// によるバイパスを防ぐ。
    /// </summary>
    /// <param name="filePath">検証するファイルパス</param>
    /// <param name="baseDirectory">基準ディレクトリ</param>
    /// <returns>基準ディレクトリ内またはパスが完全に一致する場合は true</returns>
    [Obsolete("新規コードでは ArchiveExtractor.TryResolveSafeEntryPathFromNormalized を使うこと。本メソッドは将来削除予定。")]
    public static bool IsWithinDirectory(string filePath, string baseDirectory)
    {
        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullBasePath = Path.GetFullPath(baseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 完全一致はベース自身を指しているので true
            if (string.Equals(fullFilePath, fullBasePath, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefixWithSeparator = fullBasePath + Path.DirectorySeparatorChar;
            return fullFilePath.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// パス長が制限内かどうかを検証する
    /// </summary>
    private static bool ValidatePathLength(string path, out string? errorMessage)
    {
        errorMessage = null;

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return true;

        if (path.Length > MaxPathLength)
        {
            errorMessage = App.Text("Validation.PathTooLong", MaxPathLength, path.Length);
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            if (directory != null && directory.Length > MaxDirectoryLength)
            {
                errorMessage = App.Text("Validation.DirTooLong", MaxDirectoryLength);
                return false;
            }

            if (filename != null && filename.Length > MaxFilenameLength)
            {
                errorMessage = App.Text("Validation.FilenameTooLong", MaxFilenameLength);
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = App.Text("Validation.PathParseFailed", ex.Message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// パスに不正な文字が含まれていないかを検証する
    /// </summary>
    private static bool ValidateInvalidCharacters(string path, out string? errorMessage)
    {
        errorMessage = null;

        foreach (var c in path)
        {
            if (InvalidPathCharSet.Contains(c))
            {
                errorMessage = App.Text("Validation.InvalidPathChar", c, $"0x{((int)c):X2}");
                return false;
            }
        }

        try
        {
            var filename = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(filename))
            {
                foreach (var c in filename)
                {
                    if (InvalidFileNameCharSet.Contains(c))
                    {
                        errorMessage = App.Text("Validation.InvalidFileNameChar", c, $"0x{((int)c):X2}");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = App.Text("Validation.FileNameCheckFailed", ex.Message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// パストラバーサル攻撃のパターンをチェックする
    /// </summary>
    private static bool ValidatePathTraversal(string path, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            // 文字列としての ".." チェックは削除（Report..v1.txt などの正当なファイル名を誤検知するため）
            // Path.GetFullPath で正規化を試み、有効なパスかどうかのみチェック
            var fullPath = Path.GetFullPath(path);

            // 追加のセキュリティチェック: ルートディレクトリ外へのアクセスを防ぐ
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || !fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = App.Text("Validation.InvalidPathFormat");
                Logger.Log($"セキュリティ警告: 不正なパス形式 - 元パス: {path}, 正規化パス: {fullPath}", LogLevel.Warning);
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = App.Text("Validation.PathCheckFailed", ex.Message);
            Logger.Log($"パス検証エラー: {path}, {ex.Message}", LogLevel.Warning);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Windowsの予約デバイス名が使用されていないかを検証する
    /// </summary>
    private static bool ValidateReservedNames(string path, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var filename = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(filename))
                return true;

            if (ReservedDeviceNames.Contains(filename))
            {
                errorMessage = App.Text("Validation.ReservedDeviceName", filename);
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = App.Text("Validation.FileNameCheckFailed", ex.Message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 指定されたパスが保護されたディレクトリ（デスクトップ、マイドキュメント、ドライブのルートなど）かどうかを判定する
    /// </summary>
    /// <param name="path">検証するパス</param>
    /// <returns>保護されている場合はtrue</returns>
    // 保護フォルダのキャッシュ（初回アクセス時に構築）。
    // 「上書き／削除のターゲットとしてその shell folder 自身を指定された場合」に
    // 再帰削除を拒否するためのもの。エクスプローラの Desktop / Documents / Downloads 等を
    // 出力先として選ぶこと自体は許可する（中にサブフォルダを作って展開するのは安全）。
    private static readonly Lazy<HashSet<string>> ProtectedFolders = new(BuildProtectedFolders);

    // システム重大ディレクトリのキャッシュ（同上）。
    // こちらは「設定値として保存することすら許可しない」レベルの強い制限で、
    // 主に settings.json 改竄耐性のための判定に使う。
    // Subdir 版: そのフォルダ自身 + 配下サブディレクトリすべてを禁止（Windows / ProgramFiles / System32 等）
    // Exact 版: そのフォルダ自身のみ禁止（UserProfile = C:\Users\<user> 直下。
    //          サブの Desktop / Documents / Downloads は正当な出力先として許可するため）
    private static readonly Lazy<HashSet<string>> SystemCriticalSubdirFolders = new(BuildSystemCriticalSubdirFolders);
    private static readonly Lazy<HashSet<string>> SystemCriticalExactFolders = new(BuildSystemCriticalExactFolders);

    private static HashSet<string> BuildProtectedFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specialFolders = new[]
        {
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.CommonDocuments,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos
        };

        foreach (var sf in specialFolders)
        {
            var p = Environment.GetFolderPath(sf);
            if (!string.IsNullOrEmpty(p))
                folders.Add(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            var downloads = Path.Combine(userProfile, "Downloads");
            folders.Add(Path.GetFullPath(downloads).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return folders;
    }

    private static HashSet<string> BuildSystemCriticalSubdirFolders()
    {
        // 出力先として「絶対に保存させてはいけない」OS / プログラム本体ディレクトリ。
        // ここに含めたフォルダは「自身 + サブディレクトリすべて」が禁止される。
        // 例: Windows を含めると C:\Windows\System32\drivers も禁止される。
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var systemFolders = new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.CommonDocuments,
        };

        foreach (var sf in systemFolders)
        {
            var p = Environment.GetFolderPath(sf);
            if (!string.IsNullOrEmpty(p))
                folders.Add(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return folders;
    }

    private static HashSet<string> BuildSystemCriticalExactFolders()
    {
        // 「そのフォルダ自身を出力先にすることは禁止だが、サブは許可」の対象。
        // UserProfile (C:\Users\<user>) はこれ。サブにある Desktop / Documents / Downloads /
        // Music / Pictures / Videos は正当な出力先として許可するため、サブまで禁止してはいけない。
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactFolders = new[]
        {
            Environment.SpecialFolder.UserProfile, // C:\Users\<user> ルートのみ
        };

        foreach (var sf in exactFolders)
        {
            var p = Environment.GetFolderPath(sf);
            if (!string.IsNullOrEmpty(p))
                folders.Add(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return folders;
    }

    /// <summary>
    /// パスをレキシカル正規化（Path.GetFullPath）し、可能ならシンボリックリンク/ジャンクションの
    /// 実体解決（Directory.ResolveLinkTarget）も加えた候補集合を返す。
    /// `mklink /J fake desktop` 経由での保護チェック回避を防ぐため、両方を保護判定対象にする。
    /// IsProtectedDirectory / IsSystemCriticalDirectory 双方で同じ正規化を行うため、ここに集約。
    /// 過去はそれぞれが同一ロジックをコピーしており、片方だけ修正するセキュリティ不整合の温床だった。
    /// </summary>
    private static HashSet<string> ResolveNormalizedCandidates(string path)
    {
        var normalizedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lexical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        normalizedCandidates.Add(lexical);

        try
        {
            // symlink/junction の最終ターゲットをプラットフォーム既定の上限まで追跡する。
            // .NET のドキュメント上、深さ上限は Windows が 63、Unix が 40。
            // ループ・壊れたリンク・非リンクは null を返す（IOException になるケースは catch 側で握る）。
            var resolved = Directory.ResolveLinkTarget(lexical, returnFinalTarget: true);
            if (resolved is DirectoryInfo dirInfo && !string.IsNullOrWhiteSpace(dirInfo.FullName))
            {
                normalizedCandidates.Add(dirInfo.FullName
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch
        {
            // 非対応パスや権限不足等は lexical チェックのみで判定
        }

        return normalizedCandidates;
    }

    /// <summary>
    /// 候補パス集合を、保護フォルダ集合に対して「ドライブルート単独 / 完全一致 / サブディレクトリ」で照合する。
    /// </summary>
    /// <param name="candidates">ResolveNormalizedCandidates が返す候補集合</param>
    /// <param name="protectedSet">保護対象フォルダ集合（完全一致候補）</param>
    /// <param name="matchSubdirectories">true なら `&lt;protectedFolder&gt;\subdir` も保護対象として true を返す</param>
    private static bool IsAnyCandidateProtected(
        HashSet<string> candidates,
        HashSet<string> protectedSet,
        bool matchSubdirectories)
    {
        foreach (var candidate in candidates)
        {
            // 1. ドライブのルートディレクトリチェック（C:\ 等の単独）
            var root = Path.GetPathRoot(candidate);
            if (string.Equals(root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. 完全一致
            if (protectedSet.Contains(candidate))
                return true;

            // 3. サブディレクトリ一致（IsSystemCriticalDirectory のみ有効化）
            //    `C:\Windows\System32\drivers` 等を保護するため `<protected>\` で始まるパスもブロック。
            //    一般保護（IsProtectedDirectory）は Desktop/Documents 等が含まれており、
            //    サブディレクトリ展開は正常系として許可されるべきなので false で運用。
            if (matchSubdirectories)
            {
                foreach (var protectedFolder in protectedSet)
                {
                    if (candidate.StartsWith(protectedFolder + Path.DirectorySeparatorChar,
                                             StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public static bool IsProtectedDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            var candidates = ResolveNormalizedCandidates(path);
            // 一般保護: Desktop / Documents 等の上書き・削除を拒否する目的。
            // サブディレクトリは「正常な展開先」として許可（matchSubdirectories=false）。
            return IsAnyCandidateProtected(candidates, ProtectedFolders.Value, matchSubdirectories: false);
        }
        catch
        {
            // エラーが発生した場合は安全のために保護されているとみなす
            return true;
        }
    }

    /// <summary>
    /// 「設定値として保存させない」レベルの強い保護判定。
    /// IsProtectedDirectory より範囲を狭め、Desktop / Documents / Downloads などの
    /// 一般的なユーザーコンテンツフォルダは許可する（出力先として正当な選択肢のため）。
    /// 主に settings.json 改竄耐性として、Windows / Program Files / System32 /
    /// ドライブルート / プロファイル根のような OS 構造を出力先設定として防ぐ用途。
    /// シンボリックリンク追跡は IsProtectedDirectory と同じ ResolveNormalizedCandidates を共有する。
    ///
    /// IsProtectedDirectory との差: サブディレクトリ一致を有効化している。
    /// `C:\Windows\System32\drivers` のような OS 内部パスを設定値として禁止するため。
    /// </summary>
    public static bool IsSystemCriticalDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            var candidates = ResolveNormalizedCandidates(path);
            // 1. サブディレクトリも禁止する強保護: Windows / Program Files / System32 等。
            //    `C:\Windows\System32\drivers` のような OS 内部パスも遮断する。
            if (IsAnyCandidateProtected(candidates, SystemCriticalSubdirFolders.Value, matchSubdirectories: true))
                return true;
            // 2. 完全一致のみ禁止: UserProfile 根（C:\Users\<user>）。
            //    サブの Desktop / Documents / Downloads は正当な出力先として許可するため、サブまでは禁止しない。
            if (IsAnyCandidateProtected(candidates, SystemCriticalExactFolders.Value, matchSubdirectories: false))
                return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// パスが MAX_PATH (260) を超える場合に長パスプレフィックス (<c>\\?\</c>) を付与する。
    /// Windows 10 1607+ で <c>longPathAware</c> マニフェストが有効な場合、
    /// .NET の File/Directory API は自動的に長パスを扱えるが、
    /// P/Invoke や一部サードパーティライブラリ経由ではプレフィックスが必要になる場合がある。
    /// </summary>
    internal static string EnsureLongPathPrefix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // \\?\ プレフィックスは絶対パスにのみ有効。相対パスが渡された場合は絶対パスに変換。
        string fullPath;
        try { fullPath = Path.GetFullPath(path); } catch { return path; }

        // 解決後のフルパス長で判定（入力が相対パスの場合、解決後に長くなりうる）
        if (fullPath.Length < MaxPathLength)
            return path;

        if (fullPath.StartsWith(LongPathPrefix, StringComparison.Ordinal))
            return fullPath;

        // UNC パス: \\server\share → \\?\UNC\server\share
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return UncLongPathPrefix + fullPath[2..];

        return LongPathPrefix + fullPath;
    }
}
