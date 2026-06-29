using System.Text;
using System.Text.RegularExpressions;

namespace Lhamiel.Util;

/// <summary>
/// .gitignore 互換の除外パターンを評価するマッチャ。
///
/// 対応構文:
/// <list type="bullet">
///   <item><c>#</c> から始まる行はコメント（先頭の <c>\#</c> はリテラル <c>#</c> として扱う）。</item>
///   <item>空行は無視。</item>
///   <item>末尾 <c>/</c> はディレクトリのみマッチ。</item>
///   <item>先頭 <c>/</c> はソースルート直下にアンカー。先頭以外の <c>/</c> もパス相対パターンとして扱う。</item>
///   <item><c>**</c> は 0 個以上のパス区切りを含む任意マッチ（<c>foo/**/bar</c>、<c>**/foo</c>、<c>foo/**</c>）。</item>
///   <item><c>*</c> は <c>/</c> 以外の 0 文字以上、<c>?</c> は <c>/</c> 以外の 1 文字。</item>
///   <item><c>[abc]</c> 文字クラス（gitignore 仕様に従い、先頭の <c>]</c> は文字クラスメンバ扱い）。</item>
///   <item>先頭 <c>!</c> は否定（直前まで除外されていたエントリを再包含）。<c>\!</c> はリテラル。</item>
///   <item>マッチは大小区別なし（Windows ファイルシステム互換）。</item>
/// </list>
///
/// .gitignore の挙動との対応:
/// <list type="bullet">
///   <item>
///     ディレクトリのリインクルード（<c>*.xcodeproj/*</c> + <c>!*.xcodeproj/xcshareddata/</c> のような
///     「中身を潰してから一部ディレクトリを否定で取り戻す」構文）は、<c>traversalMode: true</c> で評価したときに
///     git と同じ結果になる。この経路では各エントリを「自分自身のレベル」だけで照合し、除外の推移性
///     （親が除外なら配下も除外）は呼び出し側の DFS 枝刈り（除外ディレクトリには降りない）が担保する。
///     これにより、否定で再包含されたディレクトリの配下が正しく救われる。
///   </item>
///   <item>
///     <c>traversalMode: false</c>（既定）は単一パスを推移マッチ（<c>foo</c> は <c>foo/bar</c> にも一致）で評価する
///     フラットモード。DFS で枝刈りしない単発ファイル判定向け。
///   </item>
///   <item>パス区切りは <c>/</c> に正規化済みであることを前提とする（呼び出し側で <see cref="NormalizePath"/> を使用）。</item>
/// </list>
///
/// 階層対応:
/// <see cref="CompileLayered"/> で複数の <c>.gitignore</c> をスコープごとに合成できる。
/// 各 layer は <c>baseRelativePath</c>（source root からの相対）を持ち、その配下のパスにのみ評価される。
/// </summary>
public sealed class GitignoreMatcher
{
    private readonly Layer[] _layers;

    /// <summary>パターンが 0 件のマッチャ（常に <c>false</c> を返す）。</summary>
    public static GitignoreMatcher Empty { get; } = new([]);

    /// <summary>このマッチャに 1 つでもパターンが含まれていれば <c>true</c>。</summary>
    public bool HasRules
    {
        get
        {
            foreach (var layer in _layers)
                if (layer.Rules.Length > 0)
                    return true;
            return false;
        }
    }

    private GitignoreMatcher(Layer[] layers)
    {
        _layers = layers;
    }

    /// <summary>
    /// 行リストから単 layer の <see cref="GitignoreMatcher"/> を構築する（source root スコープ）。
    /// コメント・空行はスキップし、構文エラーのパターンも安全側でスキップする。
    /// </summary>
    public static GitignoreMatcher Compile(IEnumerable<string> lines)
    {
        var rules = CompileRules(lines);
        return rules.Length == 0 ? Empty : new GitignoreMatcher([new Layer(string.Empty, rules)]);
    }

    /// <summary>
    /// 複数の <c>.gitignore</c> 由来 layer を合成した <see cref="GitignoreMatcher"/> を構築する。
    /// 各 layer は <paramref name="baseRelativePath"/>（source root からの相対パス）を持ち、
    /// 評価時はその配下のパスにのみ適用される。layer の評価順は引数順（深い scope を後ろに置けば後勝ち）。
    /// </summary>
    public static GitignoreMatcher CompileLayered(IEnumerable<(string baseRelativePath, IEnumerable<string> lines)> layerSources)
    {
        var layers = new List<Layer>();
        foreach (var (basePath, lines) in layerSources)
        {
            var rules = CompileRules(lines);
            if (rules.Length == 0)
                continue;
            var normalizedBase = NormalizePath(basePath ?? string.Empty).Trim('/');
            layers.Add(new Layer(normalizedBase, rules));
        }
        return layers.Count == 0 ? Empty : new GitignoreMatcher([.. layers]);
    }

    /// <summary>
    /// 既存の <see cref="GitignoreMatcher"/> の layer をベースに、追加 layer を末尾に重ねて
    /// 新しい <see cref="GitignoreMatcher"/> を返す。base の layer が先に評価され、追加 layer が
    /// 後勝ちで評価される（gitignore のスコープ深い後勝ち仕様と整合）。
    /// <para>
    /// `ScanSourceFiles` 側で <c>.lhaignore</c> から既にコンパイル済みの matcher を保持しているケース
    /// （生 lines を持っていない経路）でも、nested <c>.gitignore</c> を後段の layer として加算するために使う。
    /// Codex P2 指摘対応: 旧実装では <c>fallbackMatcher</c> が参照されず global ルールが silent ドロップされていた。
    /// </para>
    /// </summary>
    public static GitignoreMatcher CompileLayered(
        GitignoreMatcher baseMatcher,
        IEnumerable<(string baseRelativePath, IEnumerable<string> lines)> additionalLayerSources)
    {
        ArgumentNullException.ThrowIfNull(baseMatcher);
        ArgumentNullException.ThrowIfNull(additionalLayerSources);

        // base 側は既に Layer 化されているのでそのままコピー。追加分のみ生 lines から compile する。
        var layers = new List<Layer>(baseMatcher._layers);
        foreach (var (basePath, lines) in additionalLayerSources)
        {
            var rules = CompileRules(lines);
            if (rules.Length == 0)
                continue;
            var normalizedBase = NormalizePath(basePath ?? string.Empty).Trim('/');
            layers.Add(new Layer(normalizedBase, rules));
        }
        return layers.Count == 0 ? Empty : new GitignoreMatcher([.. layers]);
    }

    private static Rule[] CompileRules(IEnumerable<string> lines)
    {
        var rules = new List<Rule>();
        foreach (var raw in lines)
        {
            var rule = ParseLine(raw);
            if (rule is not null)
                rules.Add(rule);
        }
        return [.. rules];
    }

    /// <summary>
    /// パスをマッチャ用に <c>/</c> 区切りへ正規化する（Windows の <c>\</c> を <c>/</c> に置換）。
    /// 入力が既に <c>/</c> 区切りの場合はそのまま返す。
    /// </summary>
    public static string NormalizePath(string path) =>
        path.IndexOf('\\') >= 0 ? path.Replace('\\', '/') : path;

    /// <summary>
    /// 指定された相対パスが除外対象か判定する。
    /// </summary>
    /// <param name="relativePath">ソースルートからの相対パス（<c>/</c> 区切り推奨。先頭の <c>/</c> 不要）。</param>
    /// <param name="isDirectory">対象がディレクトリの場合は <c>true</c>。</param>
    /// <param name="singleFileMode">
    /// 単一ファイル判定用のフラグ。<c>true</c> を渡すとアンカードルール（先頭 <c>/</c> や中間 <c>/</c> を持つルール）を
    /// スキップする。<c>relativePath</c> がファイル名のみで「ルートからの相対構造」が無い場合に使う。
    /// </param>
    /// <param name="traversalMode">
    /// DFS 枝刈りと併用する走査モード。<c>true</c> のとき各エントリを「自分自身のレベル」だけで照合し、
    /// 親→子孫への推移マッチを行わない（除外の推移性は呼び出し側の DFS 枝刈りが担保する）。これにより
    /// 中間ディレクトリの否定再包含（<c>*.xcodeproj/*</c> + <c>!*.xcodeproj/xcshareddata/</c> など）が git と同じく
    /// 配下を救えるようになる。<c>false</c>（既定）は従来どおりの推移マッチ（フラットモード）。
    /// </param>
    public bool IsExcluded(string relativePath, bool isDirectory, bool singleFileMode = false, bool traversalMode = false)
    {
        if (_layers.Length == 0)
            return false;

        var normalized = NormalizePath(relativePath).TrimStart('/');
        if (normalized.Length == 0)
            return false;

        var excluded = false;
        foreach (var layer in _layers)
        {
            // この layer のスコープ内に対象パスがあるかチェック。
            // baseRelativePath="" は source root layer（.lhaignore）なので全パスをカバー。
            if (!TryGetLayerLocalPath(normalized, layer.BaseRelativePath, out var localPath))
                continue;

            foreach (var rule in layer.Rules)
            {
                // 単一ファイル判定中はアンカード（root 相対）ルールを無効化する。
                // 例えば "/build" を持つルールは、ルートからの構造が無い単独のファイル名 "build" にはマッチさせない。
                if (singleFileMode && rule.Anchored)
                    continue;

                if (traversalMode)
                {
                    // ── traversal 経路（DFS 枝刈り併用）──
                    // 各エントリを「自分自身のレベル」だけで照合する（推移マッチ無し = ExactPathRegex を使う）。
                    // 除外の推移性は呼び出し側の DFS（除外ディレクトリには降りない）が担保するため、ここで
                    // 親パターンを子孫へ波及させない。これにより git 仕様どおり、中間ディレクトリの否定再包含
                    // （`*.xcodeproj/*` + `!*.xcodeproj/xcshareddata/` 等）が配下ファイルを正しく救える。
                    // globstar ルール（`foo/**`）は `/` を跨ぐのが正しい挙動なので ExactPathRegex を持たず、通常 Regex を使う。
                    var regexForMatch = rule.ExactPathRegex ?? rule.Regex;
                    if (rule.DirectoryOnly)
                    {
                        // ディレクトリ限定ルールはディレクトリにのみ自身レベルで一致する。
                        // ファイルへの波及は DFS 枝刈りが担当するため、ここでは扱わない。
                        if (isDirectory && SafeIsMatch(regexForMatch, localPath))
                            excluded = !rule.Negated;
                    }
                    else if (SafeIsMatch(regexForMatch, localPath))
                    {
                        excluded = !rule.Negated;
                    }
                    continue;
                }

                // ── flat 経路（単一パス照合・従来挙動）──
                // MatchTimeout 超過時は ReDoS パターンとして「マッチしない」扱いに倒す（安全側）。
                // ユーザー編集の `.lhaignore` / nested `.gitignore` で catastrophic backtracking を
                // 起こすパターンが書かれても圧縮スキャンが固まらないように保護する。
                if (rule.DirectoryOnly)
                {
                    // ディレクトリ限定ルール: 対象がディレクトリならパス全体で照合する。
                    // ファイルの場合は親ディレクトリ部分のいずれかにマッチするかを試す
                    // （git の挙動: "node_modules/" は "node_modules/a.js" 配下も除外する）。
                    if (isDirectory)
                    {
                        // Codex P2 指摘対応: negated + directoryOnly のときは exact 用 regex を使う。
                        // 通常 regex は descendant にもマッチするが、Git 仕様では "!src/" は
                        // src ディレクトリそのものだけを traversable にし、src/sub 等の descendant
                        // は parent excluded の状態のまま (re-include は伝播しない)。
                        var regexToUse = rule.Negated && rule.ExactPathRegex is not null
                            ? rule.ExactPathRegex
                            : rule.Regex;
                        if (SafeIsMatch(regexToUse, localPath))
                            excluded = !rule.Negated;
                    }
                    else if (!rule.Negated)
                    {
                        // Codex P2 指摘対応: directoryOnly の negation は配下ファイルを再包含しない。
                        // Git 仕様: "!foo/" は「foo ディレクトリ自体を traversable に」するだけで、
                        // 配下のファイル "foo/bar" は ignored のまま (再包含には別途 file pattern が必要)。
                        // allow-list 形式の ignore ("*" + "!src/" 等) で、Git では src/ 配下のファイルが
                        // 依然 ignored だが、旧実装は親ディレクトリマッチで child を再包含してしまい、
                        // 意図しない build/cache ファイルが silent に archive に含まれる経路があった。
                        var lastSlash = localPath.LastIndexOf('/');
                        if (lastSlash > 0)
                        {
                            var parentDir = localPath[..lastSlash];
                            if (SafeIsMatch(rule.Regex, parentDir))
                                excluded = true;
                        }
                    }
                }
                else
                {
                    if (SafeIsMatch(rule.Regex, localPath))
                        excluded = !rule.Negated;
                }
            }
        }
        return excluded;
    }

    /// <summary>
    /// <paramref name="normalized"/> が <paramref name="basePath"/> のスコープ内なら、
    /// base からの相対パスを <paramref name="localPath"/> に格納して <c>true</c> を返す。
    /// base が空文字なら無条件に <c>true</c>。
    /// </summary>
    private static bool TryGetLayerLocalPath(string normalized, string basePath, out string localPath)
    {
        if (basePath.Length == 0)
        {
            localPath = normalized;
            return true;
        }

        // normalized が basePath そのもの、または basePath + "/" で始まる場合のみスコープ内。
        if (normalized.Length == basePath.Length
            && normalized.Equals(basePath, StringComparison.OrdinalIgnoreCase))
        {
            // 対象 == base ディレクトリ自体。layer 内の相対パスは「自分自身」 = 空相当だが、
            // gitignore の慣習上 base ディレクトリ自体をその base 内ルールで除外する意味はあまりない。
            // IsExcluded 側の "Length == 0 で false" ガードに合わせて空を返さず false を返す。
            localPath = string.Empty;
            return false;
        }
        if (normalized.Length > basePath.Length
            && normalized.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
            && normalized[basePath.Length] == '/')
        {
            localPath = normalized[(basePath.Length + 1)..];
            return true;
        }

        localPath = string.Empty;
        return false;
    }

    private static Rule? ParseLine(string raw)
    {
        // 末尾の空白は無視（gitignore 仕様では \ でエスケープ可だが、UI から入る用途では不要）
        var line = raw.TrimEnd(' ', '\t', '\r');
        if (line.Length == 0)
            return null;

        var i = 0;

        // コメント判定（\# でエスケープすればリテラル）
        if (line[0] == '#')
            return null;

        var negated = false;
        if (line[0] == '!')
        {
            negated = true;
            i = 1;
            if (i >= line.Length)
                return null;
        }
        else if (line[0] == '\\' && line.Length >= 2 && (line[1] == '#' || line[1] == '!'))
        {
            // エスケープされた # または !
            i = 1;
        }

        var body = line[i..];
        if (body.Length == 0)
            return null;

        var directoryOnly = false;
        if (body[^1] == '/')
        {
            directoryOnly = true;
            body = body[..^1];
            if (body.Length == 0)
                return null;
        }

        var anchored = false;
        if (body[0] == '/')
        {
            anchored = true;
            body = body[1..];
            if (body.Length == 0)
                return null;
        }
        else if (body.Contains('/'))
        {
            // 中間に / があるパターンもアンカーされた相対パスとして扱う（gitignore 仕様）
            anchored = true;
        }

        // パターン末尾が "/**" で終わる場合は配下のみマッチさせる（パターンが指す "foo" ファイル自体は除外しない）。
        // 例: "foo/**" は "foo/bar" にマッチするが "foo" 単体ファイルにはマッチしない。
        // バリエーション: "**" 単体 → 空 body にして「任意のパス全部にマッチ」と等価にする。
        var endsWithDoubleStar = false;
        if (body == "**")
        {
            body = string.Empty;
            endsWithDoubleStar = true;
        }
        else if (body.EndsWith("/**"))
        {
            body = body[..^3];
            endsWithDoubleStar = true;
        }

        var regex = TryBuildRegex(body, anchored, endsWithDoubleStar, exactMatch: false);
        if (regex is null)
            return null;

        // 各エントリを「自分自身のレベル」だけで照合する exact 用 regex（末尾 $）を生成する。
        // traversalMode（DFS 枝刈り併用）で親→子孫への推移マッチを断ち、中間ディレクトリの否定再包含を
        // 正しく機能させるために、globstar 以外の全ルールで作る。flat 経路の negated + directoryOnly でも
        // 従来どおりこの regex で「対象ディレクトリそのものだけ再包含」する。
        // globstar（`foo/**`）は `/` を跨ぐのが正しいので exact 版は作らず、traversal でも通常 Regex を使う。
        Regex? exactRegex = null;
        if (!endsWithDoubleStar)
        {
            exactRegex = TryBuildRegex(body, anchored, endsWithDoubleStar, exactMatch: true);
        }

        return new Rule(regex, exactRegex, negated, directoryOnly, anchored);
    }

    private static Regex? TryBuildRegex(string pattern, bool anchored, bool endsWithDoubleStar, bool exactMatch)
    {
        var sb = new StringBuilder();
        sb.Append('^');
        if (!anchored)
        {
            // パスのどのセグメントでもマッチ可能（先頭、または直前が /）
            sb.Append("(?:.*/)?");
        }

        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // gitignore 仕様: ** が globstar（パス区切りを跨ぐ任意マッチ）として扱われるのは
                    // 以下の特定形式のみ:
                    //   (1) **/ で始まる (leading **)
                    //   (2) /** で終わる (trailing /** — ParseLine 側で endsWithDoubleStar に変換済み)
                    //   (3) /**/ の形 (middle **)
                    // それ以外（例: "ab**cd", "/ab**cd", "foo**bar", "**foo", "foo**"）は
                    // 通常の連続 * と同等に扱う（git-scm.com/docs/gitignore "Other consecutive asterisks
                    // are considered regular asterisks"）。
                    // Codex P2 指摘対応: 旧実装は前後の境界をチェックせず常に globstar として処理し、
                    // "ab**cd" が "ab/cd" にマッチする等、Git と乖離した結果になっていた。
                    var prevIsBoundary = i == 0 || pattern[i - 1] == '/';
                    var nextIsBoundary = i + 2 >= pattern.Length || pattern[i + 2] == '/';

                    if (prevIsBoundary && nextIsBoundary)
                    {
                        // ** : 0 個以上のパスセグメント。
                        // gitignore 仕様: ** は path 区切りを跨ぐが、隣接する path component を
                        // マージしない。例: "foo/**/bar" は "foo/bar" / "foo/x/bar" にマッチするが
                        // "foobar" にはマッチしない (Codex P2 #5 指摘対応)。
                        // よって前の '/' は sb から削除せず保持し、後続の '/' のみ消費する形にする:
                        //   "foo/**/bar" → "foo/(?:.*/)?bar" (前 '/' 保持、後 '/' 消費)
                        //   "**/foo"    → "(?:.*/)?foo"     (i==0 なので sb 末尾は '/' でない)
                        // 旧実装は前の '/' を sb.Length-1 で削除し (?:.*/)? が optional な形で
                        // 出力されたため、"foo/**/bar" の regex が "foo(?:.*/)?bar..." となり
                        // 区切りを失った "foobar" にも誤マッチしていた。
                        sb.Append("(?:.*/)?");
                        i += 2;

                        // 直後の '/' を消費 ((?:.*/)? が末尾 '/' を含むので二重スラッシュを避ける)
                        if (i < pattern.Length && pattern[i] == '/')
                            i++;
                    }
                    else
                    {
                        // gitignore 仕様外の ** = 連続する * 2 つ ≡ 単一の * と等価 ([^/]*)。
                        // path 区切りを跨がない通常ワイルドカードとして扱う。
                        sb.Append("[^/]*");
                        i += 2;
                    }
                }
                else
                {
                    // * : / 以外の 0 文字以上
                    sb.Append("[^/]*");
                    i++;
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (c == '[')
            {
                // 文字クラスは閉じ ] まで転写するが、gitignore のネゲート構文 [!...] は
                // .NET regex の [^...] に変換する (Codex P2 #4 指摘対応)。
                // gitignore 仕様 (POSIX fnmatch): 文字クラス先頭の '!' は否定。'^' も否定扱いされる
                // 実装が多いが gitignore の正式仕様は '!'。.NET regex は '^' だけ否定として認識する
                // ので、'!' を見たら '^' に変換する。
                // また gitignore 仕様に従い、文字クラス先頭の ']' は閉じ括弧ではなくメンバ扱い。
                var contentStart = i + 1;
                var negated = contentStart < pattern.Length && pattern[contentStart] == '!';
                if (negated)
                    contentStart++;

                // 文字クラス内容の先頭が ] ならそれもメンバ扱いとして 1 文字進める
                var searchFrom = (contentStart < pattern.Length && pattern[contentStart] == ']')
                    ? contentStart + 1
                    : contentStart;
                // 文字クラスの終端 ']' を探す。gitignore 仕様ではクラス内の '\' が次の 1 文字を
                // エスケープするため、'\]' は終端ではなくメンバの ']' として扱う。単純な
                // IndexOf(']') だと '[a\]b]'（エスケープされた ] を含むクラス）で最初の '\]' を
                // 終端と誤認し、classBody が末尾バックスラッシュ 'a\' になって不正な subtraction
                // regex '[a\-[/]]' を生成 → 何にもマッチしない fail-open 除外漏れになる。
                // '\' でエスケープされた次の 1 文字を読み飛ばして真の終端を探す。
                var end = -1;
                for (var j = searchFrom; j < pattern.Length; j++)
                {
                    if (pattern[j] == '\\' && j + 1 < pattern.Length)
                    {
                        j++; // エスケープされた次の 1 文字は終端判定から除外
                        continue;
                    }
                    if (pattern[j] == ']')
                    {
                        end = j;
                        break;
                    }
                }
                if (end < 0)
                    return null;

                // gitignore (POSIX FNM_PATHNAME) の文字クラスは暗黙に path 区切り '/' を
                // メンバとして扱わない。.NET の文字クラスにそのまま転写すると '/' を含む
                // ケースでパス区切りを跨ぐ誤マッチを起こすため、negated / positive 両方で
                // '/' を文字クラスから除外する。
                // - negated: [!abc] → [^abc/] (元から '/' を除外 + 必要なら追加)
                // - positive: [ab/] → [ab] ('/' を classBody から除去)
                // 例: "foo[ab/]bar" は Git では foo/bar にマッチしない (FNM_PATHNAME)。
                // Codex P2 #4 (negated 側) で対応済 → 今回 P2 で positive 側も同等に修正。
                var classBody = pattern[contentStart..end]; // ']' を含まない中身

                if (negated)
                {
                    // [!abc] → [^abc/] へ変換。
                    // negation は補集合なので、'/' を class に追加するだけで除外できる。
                    // range が '/' (0x2F) を含む場合 (例: [!.-0]) も [^.-0/] と書けば
                    // '/' は補集合の対象になり結果的にマッチ対象から外れる。
                    sb.Append("[^");
                    // 末尾のリテラル '-' をエスケープする。`[!a-]` → `[^a-/]` だと
                    // `a`(0x61) から `/`(0x2F) への降順レンジと解釈され ArgumentException →
                    // ルールが丸ごと破棄され「除外したいファイルが黙って含まれる」(fail-open)。
                    sb.Append(EscapeTrailingDash(classBody));
                    if (!classBody.Contains('/'))
                        sb.Append('/');
                    sb.Append(']');
                }
                else
                {
                    // positive class: gitignore (FNM_PATHNAME) は文字クラス内に '/' を
                    // マッチさせない。リテラルな '/' (Codex P2 で対応済) に加え、range が
                    // '/' (0x2F) を範囲に含むケース (例: [.-0] = 0x2E-0x30) も除外する必要がある。
                    // .NET regex の character class subtraction `[base-[/]]` で確実に '/' を引く。
                    // Codex P2 指摘 #6 (range slash) 対応。
                    var clean = classBody.Contains('/')
                        ? classBody.Replace("/", string.Empty)
                        : classBody;
                    if (clean.Length == 0)
                    {
                        // [/] や [//] のような '/' のみのクラスは仕様外で何にもマッチしない。
                        // 空クラスは .NET regex で構文エラーなので rule 全体を破棄する。
                        return null;
                    }

                    if (clean.Contains(']'))
                    {
                        // ']' を含む class (先頭 ']' メンバ扱い) は subtraction の base group
                        // として曖昧になる可能性があるため、subtraction を使わず Replace のみで
                        // 対応する (range に '/' が潜む + ']' 先頭メンバの組み合わせは稀)。
                        sb.Append('[');
                        sb.Append(clean);
                        sb.Append(']');
                    }
                    else
                    {
                        // 標準ケース: subtraction で確実に '/' を除外
                        // 例: [abc] → [abc-[/]], [.-0] → [.-0-[/]] (range に潜む '/' も除外)
                        // 末尾のリテラル '-' はエスケープする。`[a-]` → `[a--[/]]` だと
                        // `a`(0x61) から `-`(0x2D) への降順レンジと解釈され ArgumentException →
                        // ルールが丸ごと破棄され除外漏れ (fail-open) になるため。
                        sb.Append('[');
                        sb.Append(EscapeTrailingDash(clean));
                        sb.Append("-[/]]");
                    }
                }
                i = end + 1;
            }
            else if (c == '\\' && i + 1 < pattern.Length)
            {
                // 次の 1 文字をリテラル化
                AppendEscaped(sb, pattern[i + 1]);
                i += 2;
            }
            else
            {
                AppendEscaped(sb, c);
                i++;
            }
        }

        if (endsWithDoubleStar)
        {
            // "foo/**" は "foo/..." 配下のみマッチ。"foo" 単体ファイルにはマッチさせない。
            // body が空（パターンが "**" や "/**"）の場合は、現在の正規表現プレフィックスでマッチ可能な
            // 任意の非空パスにマッチさせる。
            if (pattern.Length == 0)
                sb.Append(".+$");
            else
                sb.Append("/.+$");
        }
        else if (exactMatch)
        {
            // exactMatch: 対象 path そのものだけマッチ (descendant は巻き込まない)。
            // negated + directoryOnly の rule で「対象ディレクトリそのものだけ再包含」用。
            sb.Append('$');
        }
        else
        {
            // パターン本体が完結している。ディレクトリの場合は配下にも波及させたいので "(?:/.*)?$" を付与。
            sb.Append("(?:/.*)?$");
        }

        try
        {
            // NOTE: RegexOptions.Compiled は Native AOT 非対応のため使用禁止。
            // MatchTimeout: ユーザー編集の .lhaignore / nested .gitignore から構築される regex に
            // `(?:.*/)?` 多段ネスト等が含まれると catastrophic backtracking で CPU 100% スピンする経路がある。
            // 100ms の上限を設けて、超過したパターンは IsMatch 側で RegexMatchTimeoutException として
            // catch して「マッチしない」扱いに倒す（安全側）。RTK レビュー #A2-004 / #C1-002 対応。
            return new Regex(
                sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="Regex.IsMatch(string)"/> を <see cref="RegexMatchTimeoutException"/> 安全に実行する。
    /// タイムアウト時は「マッチしない」を返して圧縮スキャンを継続させる（安全側に倒す）。
    /// </summary>
    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // パターンが ReDoS を引き起こした事実をユーザー診断のためログに残す（過剰ログ防止のため Warning）。
            Logger.Log(
                $"Gitignore パターンの正規表現マッチがタイムアウトしました（ReDoS 防御）: pattern='{regex}', input='{input}', {ex.Message}",
                LogLevel.Warning);
            return false;
        }
    }

    /// <summary>
    /// 正規表現メタ文字のみエスケープする。<see cref="Regex.Escape(string)"/> は string アロケが必要なので
    /// パフォーマンス・コード意図の両面で個別判定にする。
    /// </summary>
    private static void AppendEscaped(StringBuilder sb, char c)
    {
        if (c is '.' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '^' or '$' or '+' or '|' or '\\')
            sb.Append('\\');
        sb.Append(c);
    }

    /// <summary>
    /// 文字クラス本体が末尾にリテラルの <c>-</c> を持つ場合、<c>\-</c> にエスケープして返す。
    /// 後続に subtraction（<c>-[/]</c>）や <c>/</c> を連結すると、末尾 <c>-</c> が
    /// 降順レンジ（例 <c>a--[/]</c> = a→-、<c>a-/</c> = a→/）と解釈されて
    /// .NET regex パーサが <see cref="ArgumentException"/> を投げ、ルールが丸ごと破棄されて
    /// 除外漏れ（fail-open）になるのを防ぐ。既に <c>\-</c> でエスケープ済みの場合は二重化しない。
    /// </summary>
    private static string EscapeTrailingDash(string classBody)
    {
        if (classBody.Length == 0 || classBody[^1] != '-')
            return classBody;
        // 末尾 '-' の直前に連続する '\' の個数を数え、奇数のときだけ「既にエスケープ済み」と判定。
        // 偶数のときは '\\' (リテラルバックスラッシュ) の連続でダッシュは未エスケープなので
        // エスケープを足す。例: '\\-' は backslashCount=2 → 偶数 → ダッシュをエスケープ → '\\\-'。
        // 単一 '\' で classBody[^2] のみ見る旧実装はこの偶数連続を誤判定していた。
        var backslashCount = 0;
        for (var i = classBody.Length - 2; i >= 0 && classBody[i] == '\\'; i--)
            backslashCount++;
        if (backslashCount % 2 == 1)
            return classBody;
        return classBody[..^1] + "\\-";
    }

    /// <param name="Regex">通常マッチ用の regex (末尾 <c>(?:/.*)?$</c> 付きで descendant も巻き込む)。flat 経路で使用。</param>
    /// <param name="ExactPathRegex">
    /// globstar 以外のルールで生成される、対象パスそのものだけを exact match する regex (末尾 <c>$</c> のみ、
    /// descendant を巻き込まない)。<c>traversalMode</c>（DFS 枝刈り併用）で各エントリを自身レベルだけ照合し、
    /// 親パターンの子孫への推移マッチを断つために使う。これにより git 仕様どおり、中間ディレクトリの否定再包含
    /// （<c>*.xcodeproj/*</c> + <c>!*.xcodeproj/xcshareddata/</c> 等）で配下ファイルが正しく救われる。
    /// <para>
    /// flat 経路の negated + directoryOnly でも「対象ディレクトリそのものだけ再包含」に流用する（Codex P2 指摘対応:
    /// <c>*</c> + <c>!src/</c> の allow-list 構成で、再包含された <c>src</c> 配下の <c>src/sub</c> 等が枝刈りされ続け、
    /// 配下ファイルがアーカイブに入らないようにする）。globstar（<c>foo/**</c>）は <c>/</c> を跨ぐのが正しいので
    /// 生成されず（<c>null</c>）、traversal でも通常 Regex を使う。
    /// </para>
    /// </param>
    private sealed record Rule(Regex Regex, Regex? ExactPathRegex, bool Negated, bool DirectoryOnly, bool Anchored);

    /// <summary>
    /// 1 つの <c>.gitignore</c> ファイル（または <c>.lhaignore</c>）のスコープを表す layer。
    /// <see cref="BaseRelativePath"/> は source root からの相対パス（<c>/</c> 正規化済み・前後 <c>/</c> なし）。
    /// 空文字なら source root スコープを意味する。
    /// </summary>
    private sealed record Layer(string BaseRelativePath, Rule[] Rules);
}
