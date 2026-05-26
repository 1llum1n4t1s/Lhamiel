namespace Lhamiel.Tests.Unit;

/// <summary>
/// テスト用の一時ディレクトリを <c>using</c> スコープで自動削除するヘルパ。
/// <para>
/// 各テストファイルが個別にコピペ実装していた
/// <c>Path.Combine(Path.GetTempPath(), "..._" + Guid.NewGuid())</c> +
/// try/finally Directory.Delete パターンを統一する。
/// RTK レビュー #B2-005 対応。
/// </para>
/// </summary>
/// <example>
/// <code>
/// using var temp = TestDirectory.Create("MyTest");
/// File.WriteAllText(Path.Combine(temp.Path, "foo.txt"), "x");
/// // ...
/// // Dispose 時に temp.Path 配下が再帰削除される
/// </code>
/// </example>
internal sealed class TestDirectory : IDisposable
{
    /// <summary>テスト用一時ディレクトリの絶対パス。</summary>
    public string Path { get; }

    private bool _disposed;

    private TestDirectory(string path)
    {
        Path = path;
    }

    /// <summary>
    /// 一意な名前を持つ一時ディレクトリを作成して返す。
    /// </summary>
    /// <param name="prefix">フォルダ名のプレフィックス（テスト名等）</param>
    /// <returns>using で囲んで自動削除させる <see cref="TestDirectory"/></returns>
    public static TestDirectory Create(string prefix = "LhamielTest")
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestDirectory(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // テスト終了時の cleanup なので best-effort。
            // テストランナハング時に %TEMP% に残骸が出る可能性はあるが、
            // OS 側の %TEMP% cleanup や Lhamiel.TempCleanup で吸収される。
        }
    }
}
