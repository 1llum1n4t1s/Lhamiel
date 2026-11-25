using Lhamiel.Util;
using System.Security;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveExtractor の静的ユーティリティメソッドに対する嫌がらせテスト
/// </summary>
public class ArchiveExtractorAdversarialTests
{
    [Fact]
    public void EnumerateExtractionTreeSafely_DirectorySymlinkIsRejectedWithoutFollowingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), $"Lhamiel-ReparseTest-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(source, "link");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "outside.txt");
        File.WriteAllText(outsideFile, "preserve");
        try
        {
            DirectoryJunction.Create(link, outside);
            Assert.Throws<SecurityException>(() =>
                ArchiveExtractor.EnumerateExtractionTreeSafely(source));
            Assert.Equal("preserve", File.ReadAllText(outsideFile));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ==============================
    // 🗡️ 境界値 — GetBaseOutputDirectory
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// outputToSameDirectory=true でアーカイブパスにディレクトリがない → defaultOutputDir にフォールバック
    /// </summary>
    [Fact]
    public void GetBaseOutputDirectory_RootArchive_SameDirectory_FallsBackToDefault()
    {
        var result = ArchiveExtractor.GetBaseOutputDirectory("test.zip", @"C:\output", outputToSameDirectory: true);
        // Path.GetDirectoryName("test.zip") = "" → outputToSameDirectory で "" 採用
        // → IsNullOrWhiteSpace("") = true → defaultOutputDir にフォールバック
        Assert.Equal(@"C:\output", result);
    }

    // ==============================
    // 🗡️ 境界値 — SupportedExtensions
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// SupportedExtensions が文書化された 19 形式と完全一致する（展開・関連付けの公開互換）。
    /// 先頭ドット・小文字の書式も同時に固定される。
    /// </summary>
    [Fact]
    public void SupportedExtensions_ExactlyMatchesDocumentedFormats()
    {
        string[] expected =
        [
            ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz",
            ".xz", ".txz", ".rar", ".lzh", ".lha", ".cab", ".arj", ".z", ".tz",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), ArchiveExtractor.SupportedExtensions.Order(StringComparer.Ordinal));
    }

    // ==============================
    // 🗡️ 境界値・極端入力 — GetArchiveBaseName
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// .tar.gz / .tar.xz / .tar.bz2 / .tar.lzma の複合拡張子 → 両方除去
    /// </summary>
    [Theory]
    [InlineData("data.tar.gz", "data")]
    [InlineData("backup.tar.xz", "backup")]
    [InlineData("archive.tar.bz2", "archive")]
    [InlineData("files.tar.lzma", "files")]
    public void GetArchiveBaseName_TarCompound_RemovesBothExtensions(string fileName, string expected)
    {
        Assert.Equal(expected, ArchiveExtractor.GetArchiveBaseName(fileName));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// foo.rar.zip のようなアーカイブ拡張子の重複 → 最外のみ除去（foo.rar を返す）
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_NestedArchiveExtension_RemovesOnlyOutermost()
    {
        Assert.Equal("foo.rar", ArchiveExtractor.GetArchiveBaseName("foo.rar.zip"));
    }

    [Fact]
    public void DirectExtraction_SelfExtractingExecutableIsAcceptedAndExtensionIsRemoved()
    {
        Assert.True(ArchiveExtractor.IsSupportedDirectExtractionType("setup.exe"));
        Assert.Equal("setup", ArchiveExtractor.GetArchiveBaseName("setup.exe"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("setup.exe"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 未知の拡張子 → そのまま返す
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_UnknownExtension_ReturnsAsIs()
    {
        Assert.Equal("readme.txt", ArchiveExtractor.GetArchiveBaseName("readme.txt"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドットだけのファイル名 → 安全な代替名を返す
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_DotOnly_UsesSafeFallback()
    {
        Assert.Equal("archive", ArchiveExtractor.GetArchiveBaseName("."));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 空文字列 → 安全な代替名を返す
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_EmptyString_UsesSafeFallback()
    {
        Assert.Equal("archive", ArchiveExtractor.GetArchiveBaseName(""));
    }

    /// <summary>
    /// @adversarial @category security @severity high
    /// 特殊名・予約名から作る出力フォルダは基準自身や親、デバイス名へ解決させない。
    /// </summary>
    [Theory]
    [InlineData("...zip")]
    [InlineData("..tar.gz")]
    [InlineData("...tar.gz")]
    [InlineData("CON.zip")]
    [InlineData("CON.rules.zip")]
    [InlineData("name..zip")]
    public void GetArchiveBaseName_UnsafeOutputName_UsesSafeFallback(string archiveName)
    {
        Assert.Equal("archive", ArchiveExtractor.GetArchiveBaseName(archiveName));
    }

    /// <summary>
    /// @adversarial @category security @severity high
    /// アーカイブ由来の出力先は常に基準ディレクトリの子になる。
    /// </summary>
    [Theory]
    [InlineData("...zip")]
    [InlineData("..tar.gz")]
    [InlineData("...tar.gz")]
    [InlineData("CON.zip")]
    [InlineData(".zip")]
    public void ResolveArchiveOutputDirectory_UnsafeArchiveName_StaysInsideBase(string archiveName)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"Lhamiel-OutputBase-{Guid.NewGuid():N}");
        var result = ArchiveExtractor.ResolveArchiveOutputDirectory(baseDirectory, archiveName);

        Assert.Equal(Path.Combine(Path.GetFullPath(baseDirectory), "archive"), result);
        Assert.NotEqual(Path.GetFullPath(baseDirectory), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// .gz のみ（.tar なし） → gz除去のみ、内側は非tarなのでそのまま
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_GzOnly_RemovesGzExtension()
    {
        Assert.Equal("data", ArchiveExtractor.GetArchiveBaseName("data.gz"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドット多数のファイル名 → 最外アーカイブ拡張子のみ除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_MultipleDotsInName_RemovesOnlyArchiveExtension()
    {
        Assert.Equal("my.project.v2.1", ArchiveExtractor.GetArchiveBaseName("my.project.v2.1.zip"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// 大文字拡張子 → 大文字小文字無視で処理
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_UpperCaseExtension_CaseInsensitive()
    {
        Assert.Equal("DATA", ArchiveExtractor.GetArchiveBaseName("DATA.TAR.GZ"));
    }

    // ==============================
    // 🗡️ 境界値・極端入力 — ShouldSkipFolderCreation
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// ルートフォルダがアーカイブ名と一致し、ルートファイルなし → スキップ
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_RootMatchesArchiveName_ShouldSkip()
    {
        using var tempDir = new TempDirectory();
        var zipPath = CreateSimpleTestZip(tempDir.Path, "TestProject", rootFolderName: "TestProject");
        var info = ArchiveExtractor.GetArchiveStructureInfo(zipPath);
        Assert.True(info.ShouldSkipFolderCreation);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// ルートフォルダがアーカイブ名と不一致 → スキップしない
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_RootDiffersFromArchiveName_ShouldNotSkip()
    {
        using var tempDir = new TempDirectory();
        var zipPath = CreateSimpleTestZip(tempDir.Path, "Archive", rootFolderName: "DifferentName");
        var info = ArchiveExtractor.GetArchiveStructureInfo(zipPath);
        Assert.False(info.ShouldSkipFolderCreation);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// ルートフォルダ一致だがルートファイルも存在 → スキップしない（ファイル衝突防止）
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_RootMatchesButHasRootFiles_ShouldNotSkip()
    {
        using var tempDir = new TempDirectory();
        var zipPath = Path.Combine(tempDir.Path, "Project.zip");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create))
        {
            // ルートフォルダ + ルートファイル
            var entry1 = zip.CreateEntry("Project/src/main.cs");
            using (var w = new StreamWriter(entry1.Open())) w.Write("code");
            var entry2 = zip.CreateEntry("LICENSE");
            using (var w = new StreamWriter(entry2.Open())) w.Write("MIT");
        }
        var info = ArchiveExtractor.GetArchiveStructureInfo(zipPath);
        Assert.False(info.ShouldSkipFolderCreation);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 複数ルートフォルダ → スキップしない
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_MultipleRootFolders_ShouldNotSkip()
    {
        using var tempDir = new TempDirectory();
        var zipPath = Path.Combine(tempDir.Path, "bundle.zip");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("src/main.cs");
            using (var w = new StreamWriter(e1.Open())) w.Write("code");
            var e2 = zip.CreateEntry("docs/readme.md");
            using (var w = new StreamWriter(e2.Open())) w.Write("doc");
        }
        var info = ArchiveExtractor.GetArchiveStructureInfo(zipPath);
        Assert.False(info.ShouldSkipFolderCreation);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ルートフォルダ名の大文字小文字がアーカイブ名と異なる → スキップ（case-insensitive）
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_CaseInsensitiveMatch_ShouldSkip()
    {
        using var tempDir = new TempDirectory();
        var zipPath = Path.Combine(tempDir.Path, "myproject.zip");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("MyProject/file.txt");
            using (var w = new StreamWriter(e.Open())) w.Write("data");
        }
        var info = ArchiveExtractor.GetArchiveStructureInfo(zipPath);
        Assert.True(info.ShouldSkipFolderCreation);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 存在しないファイル → ShouldSkipFolderCreation=false
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_NonExistentFile_ReturnsFalse()
    {
        var info = ArchiveExtractor.GetArchiveStructureInfo(@"C:\nonexistent\fake.zip");
        Assert.False(info.ShouldSkipFolderCreation);
        Assert.Null(info.SingleRootItemName);
    }

    // ==============================
    // ヘルパー
    // ==============================

    /// <summary>
    /// テスト用の簡易ZIPファイルを作成する
    /// </summary>
    private static string CreateSimpleTestZip(string dir, string archiveName, string rootFolderName)
    {
        var zipPath = Path.Combine(dir, $"{archiveName}.zip");
        using var zip = new System.IO.Compression.ZipArchive(File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry($"{rootFolderName}/file.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test content");
        return zipPath;
    }

    /// <summary>
    /// テスト用の一時ディレクトリ（Dispose で自動削除）
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Lhamiel_test_{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { /* テストクリーンアップ */ }
        }
    }
}
