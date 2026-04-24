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

    // 最大パス長（Windowsのデフォルト制限）
    private const int MaxPathLength = 260;
    private const int MaxDirectoryLength = 248;
    private const int MaxFilenameLength = 255;

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
    // 保護フォルダのキャッシュ（初回アクセス時に構築）
    private static readonly Lazy<HashSet<string>> ProtectedFolders = new(BuildProtectedFolders);

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

    public static bool IsProtectedDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            // レキシカル正規化（Path.GetFullPath）と、可能ならシンボリックリンク/ジャンクションの
            // 実体解決（Directory.ResolveLinkTarget）の両方でチェックすることで、
            // `mklink /J fake desktop` 経由での保護チェック回避を防ぐ。
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

            foreach (var candidate in normalizedCandidates)
            {
                // 1. ドライブのルートディレクトリをチェック
                var root = Path.GetPathRoot(candidate);
                if (string.Equals(root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                  candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 2. 特殊なフォルダ（シェルフォルダ）をチェック
                if (ProtectedFolders.Value.Contains(candidate))
                    return true;
            }
            return false;
        }
        catch
        {
            // エラーが発生した場合は安全のために保護されているとみなす
            return true;
        }
    }
}
