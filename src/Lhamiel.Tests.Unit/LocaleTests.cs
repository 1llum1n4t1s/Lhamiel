using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ロケール関連のテスト
/// </summary>
public class LocaleTests
{
    [Fact]
    public void SupportedLocales_AllHaveDisplayNames()
    {
        // SupportedLocales（重複なし）と LocaleDisplayNames のキー集合が一致し、表示名がすべて非空であること。
        // （旧: 表示名の定義漏れ・余剰・件数・重複の個別検査を集約）
        Assert.Equal(App.SupportedLocales.Length, App.SupportedLocales.Distinct().Count());
        Assert.Equal(
            App.SupportedLocales.OrderBy(l => l, StringComparer.Ordinal),
            App.LocaleDisplayNames.Keys.OrderBy(l => l, StringComparer.Ordinal));
        foreach (var (locale, name) in App.LocaleDisplayNames)
        {
            Assert.False(string.IsNullOrWhiteSpace(name), $"ロケール '{locale}' の表示名が空です");
        }
    }

    // ═══════════════════════════════════════════════════
    // ローカライズキー整合性テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// ロケールファイルのディレクトリを取得する。
    /// テスト出力へコピーした製品のロケールファイルを読む。
    /// </summary>
    private static string GetLocalesDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Locales");
        if (Directory.Exists(dir)) return dir;
        throw new DirectoryNotFoundException("Locales ディレクトリが見つかりません");
    }

    /// <summary>
    /// axaml ファイルから x:Key 属性の値をすべて取得する。
    /// </summary>
    private static HashSet<string> ExtractKeys(string axamlPath)
    {
        var doc = XDocument.Load(axamlPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return doc.Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .Where(k => k != null)
            .ToHashSet()!;
    }


    [Fact]
    public void AllLocales_HaveExactlySameKeys()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        Assert.True(files.Length >= 2, "ロケールファイルが2件未満です");

        // 基準: 全ファイルのキーの和集合
        var allKeys = new HashSet<string>();
        var keysByFile = new Dictionary<string, HashSet<string>>();
        foreach (var file in files)
        {
            var keys = ExtractKeys(file);
            keysByFile[Path.GetFileName(file)] = keys;
            allKeys.UnionWith(keys);
        }

        var errors = new List<string>();
        foreach (var (fileName, keys) in keysByFile)
        {
            var missing = allKeys.Except(keys).OrderBy(k => k).ToList();
            if (missing.Count > 0)
                errors.Add($"  {fileName}: 不足 {missing.Count}件 → {string.Join(", ", missing)}");
        }

        Assert.True(errors.Count == 0,
            $"ロケール間でキーが不一致です:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void AllLocales_HaveNoEmptyValues()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var errors = new List<string>();
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            var emptyKeys = doc.Descendants()
                .Where(e =>
                {
                    var key = e.Attribute(x + "Key")?.Value;
                    if (key == null) return false;
                    var value = e.Value;
                    return string.IsNullOrWhiteSpace(value);
                })
                .Select(e => e.Attribute(x + "Key")!.Value)
                .ToList();

            if (emptyKeys.Count > 0)
                errors.Add($"  {Path.GetFileName(file)}: 空値 {emptyKeys.Count}件 → {string.Join(", ", emptyKeys)}");
        }

        Assert.True(errors.Count == 0,
            $"空のローカライズ値があります:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void AllLocales_HaveNoDuplicateKeys()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var errors = new List<string>();
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            var keys = doc.Descendants()
                .Select(e => e.Attribute(x + "Key")?.Value)
                .Where(k => k != null)
                .ToList();

            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key!).ToList();
            if (duplicates.Count > 0)
                errors.Add($"  {Path.GetFileName(file)}: 重複 → {string.Join(", ", duplicates)}");
        }

        Assert.True(errors.Count == 0,
            $"重複キーがあります:\n{string.Join("\n", errors)}");
    }

    // ═══════════════════════════════════════════════════
    // 書式プレースホルダーの整合性（全キー）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// string.Format の書式項目 <c>{n}</c> / <c>{n,幅}</c> / <c>{n:書式}</c> から引数番号 n を取り出す。
    /// エスケープされた波括弧 <c>{{</c> / <c>}}</c> は事前に取り除いてから照合する。
    /// </summary>
    private static readonly Regex FormatItemPattern =
        new(@"\{(\d+)(?:\s*,\s*-?\d+)?(?::[^{}]*)?\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// ローカライズ値に含まれる書式項目の引数番号の集合を返す。
    /// </summary>
    private static SortedSet<int> ExtractPlaceholderIndexes(string value)
    {
        var unescaped = value.Replace("{{", string.Empty, StringComparison.Ordinal)
            .Replace("}}", string.Empty, StringComparison.Ordinal);
        return new SortedSet<int>(FormatItemPattern.Matches(unescaped)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// axaml ファイルから x:Key → 値 の対応を取得する。
    /// </summary>
    private static Dictionary<string, string> ExtractKeyValues(string axamlPath)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in XDocument.Load(axamlPath).Descendants())
        {
            var key = element.Attribute(x + "Key")?.Value;
            if (key != null)
                result[key] = element.Value;
        }
        return result;
    }

    /// <summary>
    /// 全キーについて、各ロケールの書式プレースホルダー番号の集合が基準ロケール ja_JP と一致すること。
    /// 番号が欠けると引数が表示されず、余分な番号は App.Text の string.Format で FormatException になる。
    /// キーの欠落自体は <see cref="AllLocales_HaveExactlySameKeys"/> が検出するため、ここでは両方にあるキーだけを比べる。
    /// 書式指定子（:N0 等）はロケールごとに変えてよいので、引数番号だけを比較する。
    /// </summary>
    [Fact]
    public void AllLocales_PlaceholderIndexesMatchJaJp()
    {
        var localesDir = GetLocalesDirectory();
        var baseline = ExtractKeyValues(Path.Combine(localesDir, "ja_JP.axaml"));
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        Assert.True(files.Length >= 2, "ロケールファイルが2件未満です");

        // 基準側にプレースホルダーを持つキーが 1 件もなければ抽出処理の誤りとみなす
        Assert.Contains(baseline.Values, v => ExtractPlaceholderIndexes(v).Count > 0);

        // LZH 項目上限メッセージはパスと上限の 2 引数（{0}, {1}）を持つ。各ロケールの一致は下で検査する。
        Assert.Equal([0, 1], ExtractPlaceholderIndexes(baseline["Text.Error.LzhEntryTooLarge"]));

        var errors = new List<string>();
        foreach (var file in files)
        {
            foreach (var (key, value) in ExtractKeyValues(file))
            {
                if (!baseline.TryGetValue(key, out var baselineValue))
                    continue;
                var expected = ExtractPlaceholderIndexes(baselineValue);
                var actual = ExtractPlaceholderIndexes(value);
                if (!expected.SetEquals(actual))
                {
                    errors.Add($"  {Path.GetFileName(file)}: {key} → ja_JP {{{string.Join(",", expected)}}} / " +
                               $"このロケール {{{string.Join(",", actual)}}}");
                }
            }
        }

        Assert.True(errors.Count == 0,
            $"プレースホルダー番号が ja_JP と一致しないキーがあります:\n{string.Join("\n", errors)}");
    }

    // ═══════════════════════════════════════════════════
    // ソースから参照されるキーの存在確認
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// リポジトリルート（Lhamiel.slnx と src/Lhamiel/Lhamiel.csproj を持つディレクトリ）を求める。
    /// テストは --artifacts-path 等でリポジトリ外の出力先から実行されることがあるため、
    /// まずコンパイル時のこのソースファイルの位置（CallerFilePath）から上方向に探し、
    /// PathMap 等でその位置が実在しない場合はテスト出力先と作業ディレクトリから探す。
    /// 見つからなければ例外で失敗させる（スキップはしない）。
    /// </summary>
    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var starts = new[]
        {
            string.IsNullOrEmpty(sourceFilePath) ? null : Path.GetDirectoryName(sourceFilePath),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };
        foreach (var start in starts)
        {
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
                continue;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Lhamiel.slnx"))
                    && File.Exists(Path.Combine(dir.FullName, "src", "Lhamiel", "Lhamiel.csproj")))
                {
                    return dir.FullName;
                }
            }
        }
        throw new DirectoryNotFoundException(
            $"リポジトリルート（Lhamiel.slnx）が見つかりません。source={sourceFilePath}, base={AppContext.BaseDirectory}, cwd={Environment.CurrentDirectory}");
    }

    /// <summary>
    /// 製品プロジェクト src/Lhamiel 配下のソースファイルを列挙する（bin / obj の生成物は除外）。
    /// </summary>
    private static IEnumerable<string> EnumerateProductSources(string searchPattern)
    {
        var productDir = Path.Combine(GetRepositoryRoot(), "src", "Lhamiel");
        var excludedDirs = new[]
        {
            Path.Combine(productDir, "bin") + Path.DirectorySeparatorChar,
            Path.Combine(productDir, "obj") + Path.DirectorySeparatorChar,
        };
        return Directory.EnumerateFiles(productDir, searchPattern, SearchOption.AllDirectories)
            .Where(f => !excludedDirs.Any(d => f.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>
    /// C# の <c>App.Text("…")</c> の第 1 引数が文字列リテラルの呼び出し。App.Text は "Text." を自動付加する。
    /// 補間文字列・連結・三項演算子・変数などで動的に組み立てるキーは静的に解決できないため対象外。
    /// </summary>
    private static readonly Regex AppTextLiteralPattern =
        new(@"\bApp\.Text\(\s*""((?:[^""\\]|\\.)+)""", RegexOptions.CultureInvariant);

    /// <summary>
    /// XAML の <c>{DynamicResource Text.*}</c>（<c>ResourceKey=</c> 指定を含む）参照。
    /// </summary>
    private static readonly Regex XamlDynamicTextPattern =
        new(@"\{DynamicResource\s+(?:ResourceKey\s*=\s*)?(Text\.[^\s},]+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// ソースの <c>App.Text("…")</c> リテラルと XAML の <c>DynamicResource Text.*</c> が参照するキーが
    /// 基準ロケール ja_JP.axaml に存在すること。存在しないキーは実行時に未翻訳のキー名表示や例外になる。
    /// 動的に組み立てるキー（補間・連結・三項演算子・プレフィックス変数）は対象外。
    /// </summary>
    [Fact]
    public void SourceReferencedTextKeys_ExistInJaJp()
    {
        var jaJpKeys = ExtractKeys(Path.Combine(GetLocalesDirectory(), "ja_JP.axaml"));
        var localesDir = Path.Combine(GetRepositoryRoot(), "src", "Lhamiel", "Resources", "Locales")
            + Path.DirectorySeparatorChar;

        // キー → 参照元ファイル（相対パス）の対応
        var csharpRefs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateProductSources("*.cs"))
        {
            foreach (Match m in AppTextLiteralPattern.Matches(File.ReadAllText(file)))
            {
                var key = "Text." + m.Groups[1].Value;
                if (!csharpRefs.TryGetValue(key, out var set))
                    csharpRefs[key] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(Path.GetFileName(file));
            }
        }

        var xamlRefs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateProductSources("*.axaml"))
        {
            // ロケール定義そのものは参照元ではないので除外
            if (file.StartsWith(localesDir, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (Match m in XamlDynamicTextPattern.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!xamlRefs.TryGetValue(key, out var set))
                    xamlRefs[key] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(Path.GetFileName(file));
            }
        }

        // 抽出件数の下限: 正規表現やパス解決の誤りで 0 件になり、検査が素通りするのを防ぐ。
        // （2026-11 時点で C# 約 150 件・XAML 約 140 件の異なるキーを参照している）
        Assert.True(csharpRefs.Count >= 100, $"App.Text のリテラルキーの抽出件数が少なすぎます: {csharpRefs.Count}");
        Assert.True(xamlRefs.Count >= 100, $"XAML の DynamicResource Text.* の抽出件数が少なすぎます: {xamlRefs.Count}");

        var missing = csharpRefs.Concat(xamlRefs)
            .Where(kv => !jaJpKeys.Contains(kv.Key))
            .Select(kv => $"  {kv.Key} ← {string.Join(", ", kv.Value)}")
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"ja_JP.axaml に存在しないキーを参照しています:\n{string.Join("\n", missing)}");
    }
}
