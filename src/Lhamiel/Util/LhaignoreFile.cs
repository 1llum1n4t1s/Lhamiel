using System.Text;

namespace Lhamiel.Util;

/// <summary>
/// <c>%LocalAppData%\Lhamiel\.lhaignore</c> ファイルの読み書きを担当する。
///
/// .lhaignore は .gitignore 互換のテキストファイルで、圧縮時の除外パターンを記述する。
/// UI から追加・削除すると当ファイルが直接書き換えられる。
/// 「除外設定ファイルを開く」ボタンでユーザーが直接編集することも想定しているため、
/// 既存のコメント・空行は UI 操作で可能な限り保持する。
/// </summary>
internal static class LhaignoreFile
{
    /// <summary>.lhaignore のフルパス（<c>settings.json</c> と同じディレクトリ）。</summary>
    public static string FilePath { get; } = Path.Combine(Settings.AppDataDirectory, ".lhaignore");

    /// <summary>UI 操作で書き出すヘッダーコメント。</summary>
    public const string HeaderComment =
        "# Lhamiel 圧縮時の除外パターン (.gitignore 互換)\r\n" +
        "# - 1 行 1 パターン。# 始まりはコメント。\r\n" +
        "# - 末尾 / でディレクトリ限定 (例: node_modules/)\r\n" +
        "# - 先頭 / でソースルート相対にアンカー (例: /build)\r\n" +
        "# - *, ?, **, [abc] のグロブが使用可能 (例: *.log, **/cache, [Tt]humbs.db)\r\n" +
        "# - 先頭 ! で否定 (除外対象から再包含)\r\n" +
        "\r\n";

    /// <summary>
    /// 既定の除外パターン（ヘッダーコメント付きの完全な .lhaignore 本文）。
    /// </summary>
    public static string CreateDefaultContent()
    {
        var sb = new StringBuilder(HeaderComment);
        foreach (var p in ArchiveExtractor.IgnoredSystemFiles)
            sb.AppendLine(p);
        foreach (var p in ArchiveExtractor.IgnoredSystemDirectories)
            sb.AppendLine(p + "/");
        return sb.ToString();
    }

    /// <summary>
    /// .lhaignore が無ければ作成する。<paramref name="legacyPatterns"/> が指定されていれば
    /// その内容で初期化する（settings.json からの一回限りのマイグレーション用途）。
    /// </summary>
    /// <returns>ファイルを新規作成した場合 true。</returns>
    public static bool EnsureExists(IEnumerable<string>? legacyPatterns = null)
    {
        try
        {
            Directory.CreateDirectory(Settings.AppDataDirectory);
            if (File.Exists(FilePath))
                return false;

            string content;
            if (legacyPatterns is null)
            {
                content = CreateDefaultContent();
            }
            else
            {
                var sb = new StringBuilder(HeaderComment);
                foreach (var p in legacyPatterns)
                {
                    var trimmed = (p ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                        continue;
                    // Codex P2 指摘対応: 旧 ExcludedFilePatterns はリテラルなパス成分名だったが、
                    // .lhaignore は gitignore 仕様で評価されるため、`*` / `?` / `[` / `]` / `\` /
                    // 先頭 `!` / 先頭 `#` を含むリテラル名はメタ文字として再解釈されてしまう。
                    // 例: 旧設定で `foo[1].txt` を除外していたユーザは、移行後 character class
                    // 扱いとなりリテラル `foo[1].txt` がマッチしなくなる。escape して仕様変更を防ぐ。
                    sb.AppendLine(EscapeGitignoreLiteral(trimmed));
                }
                content = sb.ToString();
            }
            File.WriteAllText(FilePath, content, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            try { Logger.Log($".lhaignore の初期化に失敗しました: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
            return false;
        }
    }

    /// <summary>
    /// 旧 ExcludedFilePatterns のリテラル文字列を gitignore で「メタ文字を含まないリテラル」と
    /// して扱われる形にエスケープする。
    /// <para>
    /// gitignore メタ文字: <c>*</c> / <c>?</c> / <c>[</c> / <c>]</c> / <c>\</c> をバックスラッシュ
    /// でエスケープ。先頭 <c>!</c>（否定）と先頭 <c>#</c>（コメント）も同様にエスケープする。
    /// パス区切り <c>/</c> はリテラルでも gitignore でも path 構造を表すので escape しない
    /// （旧 ExcludedFilePatterns は basename 想定なので通常 <c>/</c> は含まないが、含まれていた
    /// 場合はそのまま path 相対 anchored 扱いとして転写する）。
    /// </para>
    /// </summary>
    internal static string EscapeGitignoreLiteral(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        var sb = new StringBuilder(input.Length + 4);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            // 先頭の '!' / '#' は gitignore で特別な意味を持つので escape
            if (i == 0 && (c == '!' || c == '#'))
            {
                sb.Append('\\');
                sb.Append(c);
                continue;
            }
            // wildcard / character class / escape 文字
            if (c is '*' or '?' or '[' or ']' or '\\')
            {
                sb.Append('\\');
                sb.Append(c);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// .lhaignore の生の行リストを返す。ファイルが無い場合は空配列。
    /// </summary>
    public static string[] ReadLines()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllLines(FilePath, Encoding.UTF8) : [];
        }
        catch (Exception ex)
        {
            try { Logger.Log($".lhaignore の読み込みに失敗しました: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
            return [];
        }
    }

    /// <summary>
    /// UI 表示用に、コメント・空行を除いたパターン本体だけを返す。
    /// 重複・前後空白は除去するが、否定（<c>!</c> 始まり）はそのまま表示する。
    /// </summary>
    public static List<string> ReadPatterns()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ReadLines())
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (seen.Add(line))
                result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// 現在の .lhaignore から <see cref="GitignoreMatcher"/> を組み立てる。
    /// </summary>
    public static GitignoreMatcher LoadMatcher() => GitignoreMatcher.Compile(ReadLines());

    /// <summary>
    /// パターンをファイル末尾に追記する。既に同じパターンが（コメント以外で）あれば追記しない。
    /// </summary>
    public static void AppendPattern(string pattern)
    {
        var normalized = (pattern ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return;

        try
        {
            EnsureExists();
            // EnsureExists 完了後はファイルが存在する前提で読み込む。読み取り直前で別プロセスが消した
            // 場合は FileNotFoundException が飛ぶが、catch で受けて Logger に警告を出すので安全側に倒す。
            var lines = new List<string>(File.ReadAllLines(FilePath, Encoding.UTF8));

            foreach (var raw in lines)
            {
                var existing = raw.Trim();
                if (existing.Length == 0 || existing[0] == '#')
                    continue;
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            // 末尾に空行が無ければ 1 行入れて視認性を確保（ヘッダーコメント直後を除く）
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add(string.Empty);
            lines.Add(normalized);

            WriteLinesAtomically(lines);
        }
        catch (Exception ex)
        {
            try { Logger.Log($".lhaignore への追記に失敗しました: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
        }
    }

    /// <summary>
    /// パターンに完全一致する行を削除する（コメント・空行は保持）。
    /// </summary>
    public static void RemovePattern(string pattern)
    {
        var normalized = (pattern ?? string.Empty).Trim();
        if (normalized.Length == 0 || !File.Exists(FilePath))
            return;

        try
        {
            var lines = File.ReadAllLines(FilePath, Encoding.UTF8).ToList();
            var changed = false;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var existing = lines[i].Trim();
                if (existing.Length == 0 || existing[0] == '#')
                    continue;
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed)
                WriteLinesAtomically(lines);
        }
        catch (Exception ex)
        {
            try { Logger.Log($".lhaignore からの削除に失敗しました: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
        }
    }

    /// <summary>
    /// .lhaignore をデフォルト内容で上書きする。
    /// </summary>
    public static void ResetToDefaults()
    {
        try
        {
            WriteAtomically(CreateDefaultContent());
        }
        catch (Exception ex)
        {
            try { Logger.Log($".lhaignore の初期化に失敗しました: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
        }
    }

    /// <summary>
    /// 文字列をそのまま <c>.lhaignore</c> へ原子的に書き込む（tmp + rename）。
    /// </summary>
    private static void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Settings.AppDataDirectory);
        // 並行更新の安全性のため一意な temp 名を使う。固定 `.tmp` だと、別の UI 操作や
        // 外部エディタの保存と temp ファイルが衝突して片方の更新を潰すリスクがある。
        var tmpPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmpPath, content, Encoding.UTF8);
            File.Move(tmpPath, FilePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>
    /// 行リストを <see cref="WriteAtomically"/> 経由で <c>.lhaignore</c> に書き出す。
    /// 書込み中にプロセスが落ちても <c>.lhaignore</c> は旧内容のまま残るのでデータロスを防げる。
    /// 圧縮実行毎の読み直しと UI の追加・削除が同時に走るケースで「途中状態の空ファイル」を読まれない保証も得られる。
    /// </summary>
    private static void WriteLinesAtomically(IList<string> lines)
    {
        // 最終行にも改行を付ける（UNIX/Windows ツールとの互換性のため CRLF）
        var content = string.Join("\r\n", lines) + "\r\n";
        WriteAtomically(content);
    }
}
