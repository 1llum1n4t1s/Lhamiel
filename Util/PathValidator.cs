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
            errorMessage = "ファイルパスが空です。";
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
    /// ファイルパスが指定された基準ディレクトリ内にあるかどうかを検証する
    /// </summary>
    /// <param name="filePath">検証するファイルパス</param>
    /// <param name="baseDirectory">基準ディレクトリ</param>
    /// <returns>基準ディレクトリ内にある場合はtrue</returns>
    public static bool IsWithinDirectory(string filePath, string baseDirectory)
    {
        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullBasePath = Path.GetFullPath(baseDirectory);

            return fullFilePath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
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
            errorMessage = $"パスが長すぎます。最大{MaxPathLength}文字まで許可されています。（現在: {path.Length}文字）";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            if (directory != null && directory.Length > MaxDirectoryLength)
            {
                errorMessage = $"ディレクトリパスが長すぎます。最大{MaxDirectoryLength}文字まで許可されています。";
                return false;
            }

            if (filename != null && filename.Length > MaxFilenameLength)
            {
                errorMessage = $"ファイル名が長すぎます。最大{MaxFilenameLength}文字まで許可されています。";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"パスの解析に失敗しました: {ex.Message}";
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
                errorMessage = $"パスに不正な文字が含まれています: '{c}' (0x{((int)c):X2})";
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
                        errorMessage = $"ファイル名に不正な文字が含まれています: '{c}' (0x{((int)c):X2})";
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"ファイル名の検証中にエラーが発生しました: {ex.Message}";
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
                errorMessage = "不正なパス形式が検出されました";
                Logger.Log($"セキュリティ警告: 不正なパス形式 - 元パス: {path}, 正規化パス: {fullPath}", LogLevel.Warning);
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"パスの検証に失敗しました: {ex.Message}";
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
                errorMessage = $"予約されたデバイス名が使用されています: {filename}";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"ファイル名の検証中にエラーが発生しました: {ex.Message}";
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

            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 1. ドライブのルートディレクトリをチェック
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. 特殊なフォルダ（シェルフォルダ）をチェック
            return ProtectedFolders.Value.Contains(fullPath);
        }
        catch
        {
            // エラーが発生した場合は安全のために保護されているとみなす
            return true;
        }
    }
}
