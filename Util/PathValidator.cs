using System.IO;

namespace Lhamiel.Util;

/// <summary>
/// ファイルパスの検証を行うユーティリティクラス
/// </summary>
public static class PathValidator
{
    // Windows予約デバイス名
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

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

        var invalidChars = Path.GetInvalidPathChars();
        foreach (var c in path)
        {
            if (invalidChars.Contains(c))
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
                var invalidFileNameChars = Path.GetInvalidFileNameChars();
                foreach (var c in filename)
                {
                    if (invalidFileNameChars.Contains(c))
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
            // ★ 修正: 文字列としての ".." チェックは削除（Report..v1.txt などの正当なファイル名を誤検知するため）
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

            var upperFilename = filename.ToUpperInvariant();
            if (ReservedDeviceNames.Contains(upperFilename))
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
    /// パスを安全に正規化する
    /// </summary>
    /// <param name="path">正規化するパス</param>
    /// <param name="normalizedPath">正規化されたパス</param>
    /// <returns>正規化に成功した場合はtrue</returns>
    public static bool TryNormalizePath(string path, out string? normalizedPath)
    {
        normalizedPath = null;

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"パスの正規化に失敗: {path} - {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    /// <summary>
    /// ファイルパスが安全かどうかを包括的に検証する
    /// </summary>
    /// <param name="filePath">検証するファイルパス</param>
    /// <param name="baseDirectory">基準ディレクトリ（指定した場合、この配下かどうかもチェック）</param>
    /// <returns>安全な場合はtrue</returns>
    public static bool IsSafeFilePath(string filePath, string? baseDirectory = null)
    {
        if (!IsValidFilePath(filePath, out var errorMessage))
        {
            Logger.Log($"ファイルパス検証失敗: {errorMessage}", LogLevel.Warning);
            return false;
        }

        if (baseDirectory != null && !IsWithinDirectory(filePath, baseDirectory))
        {
            Logger.Log($"ファイルパスが基準ディレクトリ外です: {filePath}", LogLevel.Warning);
            return false;
        }

        return true;
    }
}
