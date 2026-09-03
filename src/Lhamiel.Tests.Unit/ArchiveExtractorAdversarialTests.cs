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
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/d", "/c", "mklink", "/J", link, outside },
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
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
    // 🗡️ 境界値・極端入力 — IsSupportedArchiveType
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空文字列を渡してもクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_EmptyString_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(""));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子なしのファイルパス → false
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_NoExtension_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("Makefile"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(@"C:\path\to\LICENSE"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドットだけの拡張子 → false
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_DotOnly_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("file."));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 大文字・混在ケースの拡張子がサポートされる（大文字小文字無視）
    /// </summary>
    [Theory]
    [InlineData("archive.ZIP")]
    [InlineData("archive.Zip")]
    [InlineData("archive.7Z")]
    [InlineData("archive.Tar")]
    [InlineData("archive.RAR")]
    [InlineData("archive.LZH")]
    public void IsSupportedArchiveType_CaseInsensitive_ReturnsTrue(string path)
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 二重拡張子（.tar.gz）→ 外側の .gz がサポート対象
    /// </summary>
    [Theory]
    [InlineData("archive.tar.gz", true)]   // .gz はサポート
    [InlineData("archive.tar.bz2", true)]  // .bz2 はサポート
    [InlineData("archive.tar.xz", true)]   // .xz はサポート
    [InlineData("archive.tar.txt", false)] // .txt は非サポート
    public void IsSupportedArchiveType_DoubleExtension_ChecksOuterExtension(string path, bool expected)
    {
        Assert.Equal(expected, ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子がアーカイブっぽいが非サポートの形式 → false
    /// </summary>
    [Theory]
    [InlineData("file.iso")]
    [InlineData("file.dmg")]
    [InlineData("file.pkg")]
    [InlineData("file.deb")]
    [InlineData("file.rpm")]
    [InlineData("file.msi")]
    public void IsSupportedArchiveType_UnsupportedArchiveFormats_ReturnsFalse(string path)
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// Windows 予約名 + アーカイブ拡張子 → サポート対象（拡張子のみで判定）
    /// </summary>
    [Theory]
    [InlineData("CON.zip")]
    [InlineData("NUL.7z")]
    [InlineData("COM1.tar")]
    public void IsSupportedArchiveType_WindowsReservedNames_ReturnsTrue(string path)
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 超長パス（260文字超）でもクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_VeryLongPath_DoesNotCrash()
    {
        var longDir = new string('a', 300);
        var path = $@"C:\{longDir}\archive.zip";
        // Path.GetExtension は長いパスでもクラッシュしない
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    // ==============================
    // 🗡️ 境界値 — AreAllSupportedArchives
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空のコレクション → true（vacuous truth: すべての要素が条件を満たす）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_EmptyCollection_ReturnsTrue()
    {
        Assert.True(ArchiveExtractor.AreAllSupportedArchives([]));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 存在しないファイルパスのみ → false（File.Exists が false）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_NonExistentFiles_ReturnsFalse()
    {
        var paths = new[] { @"C:\nonexistent\fake.zip", @"C:\nonexistent\fake.7z" };
        Assert.False(ArchiveExtractor.AreAllSupportedArchives(paths));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ディレクトリパス（.zip 拡張子付き）→ false（File.Exists が false）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_DirectoryPath_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fake_archive.zip");
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.False(ArchiveExtractor.AreAllSupportedArchives([tempDir]));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    // ==============================
    // 🗡️ 境界値 — GetOutputDirectory / GetBaseOutputDirectory
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// アーカイブ名が拡張子のみ（.zip）でも、基準ディレクトリ自身へ直接展開しない
    /// </summary>
    [Fact]
    public void GetOutputDirectory_ExtensionOnlyFilename_UsesSafeFallbackFolderName()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\.zip", @"C:\output");
        Assert.Equal(Path.Combine(@"C:\output", "archive"), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// デフォルト出力ディレクトリが空白文字のみ → アーカイブの親ディレクトリにフォールバック
    /// </summary>
    [Fact]
    public void GetBaseOutputDirectory_WhitespaceOnlyDefault_FallsBackToArchiveDir()
    {
        var result = ArchiveExtractor.GetBaseOutputDirectory(@"C:\archives\test.zip", "   ");
        Assert.Equal(@"C:\archives", result);
    }

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

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 二重拡張子ファイルの出力ディレクトリ名（archive.tar.gz → archive）
    /// </summary>
    [Fact]
    public void GetOutputDirectory_DoubleExtension_StripsAllArchiveExtensions()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\archive.tar.gz", @"C:\output");
        // 複合アーカイブ拡張子を全て除去: "archive.tar.gz" → "archive"
        Assert.Equal(Path.Combine(@"C:\output", "archive"), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 日本語ファイル名のアーカイブ → 出力ディレクトリ名も日本語
    /// </summary>
    [Fact]
    public void GetOutputDirectory_JapaneseFilename_PreservedInOutput()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\日本語アーカイブ.zip", @"C:\output");
        Assert.Equal(Path.Combine(@"C:\output", "日本語アーカイブ"), result);
    }

    // ==============================
    // 🎭 型パンチ — IsSupportedArchiveType
    // ==============================

    /// <summary>
    /// @adversarial @category type @severity medium
    /// Unicode 制御文字を含むパスでクラッシュしない
    /// </summary>
    [Theory]
    [InlineData("file\u200B.zip")]      // ゼロ幅スペース
    [InlineData("file\u202E.zip")]      // RTL override
    [InlineData("\uFEFFfile.zip")]      // BOM
    public void IsSupportedArchiveType_UnicodeControlChars_DoesNotCrash(string path)
    {
        // クラッシュしないこと（結果は true/false どちらでも OK）
        var _ = ArchiveExtractor.IsSupportedArchiveType(path);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 絵文字を含むファイル名でクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_EmojiFilename_DoesNotCrash()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType("📦アーカイブ.zip"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("📦アーカイブ.txt"));
    }

    // ==============================
    // 🗡️ 境界値 — SupportedExtensions / IgnoredSystemFiles
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// SupportedExtensions に全ての文書化された形式が含まれている（ドキュメントとの整合性）
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".tar")]
    [InlineData(".gz")]
    [InlineData(".tgz")]
    [InlineData(".bz2")]
    [InlineData(".tbz2")]
    [InlineData(".tbz")]
    [InlineData(".lzma")]
    [InlineData(".tlz")]
    [InlineData(".xz")]
    [InlineData(".txz")]
    [InlineData(".rar")]
    [InlineData(".lzh")]
    [InlineData(".cab")]
    [InlineData(".arj")]
    [InlineData(".z")]
    [InlineData(".tz")]
    public void SupportedExtensions_ContainsAllDocumentedFormats(string ext)
    {
        Assert.Contains(ext, ArchiveExtractor.SupportedExtensions);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// IgnoredSystemFiles がケースインセンシティブに動作する
    /// </summary>
    [Theory]
    [InlineData("desktop.ini")]
    [InlineData("DESKTOP.INI")]
    [InlineData("Desktop.INI")]
    [InlineData("Thumbs.db")]
    [InlineData("THUMBS.DB")]
    [InlineData(".DS_Store")]
    [InlineData(".ds_store")]
    public void IgnoredSystemFiles_CaseInsensitive(string fileName)
    {
        Assert.Contains(fileName, ArchiveExtractor.IgnoredSystemFiles);
    }

    // ==============================
    // 🗡️ 境界値・極端入力 — GetArchiveBaseName
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 単純なzip拡張子 → 拡張子のみ除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_SimpleZip_RemovesExtension()
    {
        Assert.Equal("project", ArchiveExtractor.GetArchiveBaseName(@"C:\dir\project.zip"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// .tar.gz 複合拡張子 → 両方除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_TarGz_RemovesBothExtensions()
    {
        Assert.Equal("data", ArchiveExtractor.GetArchiveBaseName("data.tar.gz"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// .tar.xz 複合拡張子 → 両方除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_TarXz_RemovesBothExtensions()
    {
        Assert.Equal("backup", ArchiveExtractor.GetArchiveBaseName("backup.tar.xz"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// .tar.bz2 複合拡張子 → 両方除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_TarBz2_RemovesBothExtensions()
    {
        Assert.Equal("archive", ArchiveExtractor.GetArchiveBaseName("archive.tar.bz2"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// .tar.lzma 複合拡張子 → 両方除去
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_TarLzma_RemovesBothExtensions()
    {
        Assert.Equal("files", ArchiveExtractor.GetArchiveBaseName("files.tar.lzma"));
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

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// foo.zip.zip → 最外のみ除去（foo.zip を返す）
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_DuplicateZipExtension_RemovesOnlyOutermost()
    {
        Assert.Equal("foo.zip", ArchiveExtractor.GetArchiveBaseName("foo.zip.zip"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子なしのファイル → そのまま返す
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_NoExtension_ReturnsAsIs()
    {
        Assert.Equal("noext", ArchiveExtractor.GetArchiveBaseName("noext"));
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
    public void ResolveArchiveOutputDirectory_UnsafeArchiveName_StaysInsideBase(string archiveName)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"Lhamiel-OutputBase-{Guid.NewGuid():N}");
        var result = ArchiveExtractor.ResolveArchiveOutputDirectory(baseDirectory, archiveName);

        Assert.Equal(Path.Combine(Path.GetFullPath(baseDirectory), "archive"), result);
        Assert.NotEqual(Path.GetFullPath(baseDirectory), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// .tar のみ（圧縮なし） → tar を返す（.tar はアーカイブ拡張子）
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_TarOnly_RemovesTarExtension()
    {
        Assert.Equal("data", ArchiveExtractor.GetArchiveBaseName("data.tar"));
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
    /// 日本語ファイル名 + .tar.gz → 日本語部分を保持
    /// </summary>
    [Fact]
    public void GetArchiveBaseName_JapaneseWithTarGz_PreservesJapanese()
    {
        Assert.Equal("日本語データ", ArchiveExtractor.GetArchiveBaseName("日本語データ.tar.gz"));
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

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// .tar.gz の場合、アーカイブ名は "data"（tar.gzを両方除去）で比較されること
    /// </summary>
    [Fact]
    public void GetArchiveStructureInfo_TarGz_UsesBaseNameForComparison()
    {
        using var tempDir = new TempDirectory();
        // data.tar.gz のアーカイブ名は "data"
        // ルートフォルダが "data" なら ShouldSkipFolderCreation=true
        var zipPath = Path.Combine(tempDir.Path, "data.tar.gz");
        // 注: ZipArchive で .tar.gz を作れないので、.zip で代替テスト
        // 実際の .tar.gz テストは統合テストで行う
        var actualZipPath = Path.Combine(tempDir.Path, "data.zip");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(actualZipPath), System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("data/file.txt");
            using (var w = new StreamWriter(e.Open())) w.Write("content");
        }
        var info = ArchiveExtractor.GetArchiveStructureInfo(actualZipPath);
        Assert.True(info.ShouldSkipFolderCreation);
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
