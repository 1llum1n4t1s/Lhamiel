using System.Text;

namespace Lhamiel.Util;

/// <summary>シェルの長い選択リストを一度だけ回収する。IPC では展開前のトークンを渡す。</summary>
internal static class ShellSelectionFile
{
    internal const string Argument = "--shell-selection";
    internal const int MaxBytes = 32 * 1024 * 1024;

    internal static string GetPath(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _))
            throw new InvalidDataException("Invalid shell selection token.");
        return Path.Combine(Path.GetTempPath(), $"Lhamiel-selection-{token}.bin");
    }

    internal static string[] Read(string token)
    {
        var path = GetPath(token);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Shell selection must not be a reparse point.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
            4096, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        if (stream.Length is < 2 or > MaxBytes || stream.Length % 2 != 0)
            throw new InvalidDataException("Invalid shell selection size.");

        // C++ 側と共通の UTF-16LE / NUL 区切り。BOM・オプションの再解釈はしない。
        using var reader = new StreamReader(stream, new UnicodeEncoding(false, false, true),
            detectEncodingFromByteOrderMarks: false);
        var text = reader.ReadToEnd();
        if (text.Length == 0 || text[^1] != '\0')
            throw new InvalidDataException("Incomplete shell selection.");
        var paths = text[..^1].Split('\0');
        if (paths.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)))
            throw new InvalidDataException("Shell selection contains an invalid path.");
        return paths;
    }
}
