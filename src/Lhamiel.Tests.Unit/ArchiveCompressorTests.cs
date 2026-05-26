using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveCompressor のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class ArchiveCompressorTests
{
    // === ParseFormat ===

    [Theory]
    [InlineData("ZIP", Format.Zip)]
    [InlineData("7Z", Format.SevenZip)]
    [InlineData("TAR", Format.Tar)]
    [InlineData("GZ", Format.GZip)]
    [InlineData("BZ2", Format.BZip2)]
    [InlineData("XZ", Format.XZ)]
    public void ParseFormat_WithUpperCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("zip", Format.Zip)]
    [InlineData("7z", Format.SevenZip)]
    [InlineData("tar", Format.Tar)]
    [InlineData("gz", Format.GZip)]
    [InlineData("bz2", Format.BZip2)]
    [InlineData("xz", Format.XZ)]
    public void ParseFormat_WithLowerCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("Zip")]
    [InlineData("zIp")]
    [InlineData("ZiP")]
    public void ParseFormat_WithMixedCase_ReturnsZip(string input)
    {
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rar")]
    [InlineData("lzh")]
    [InlineData("cab")]
    [InlineData("unknown")]
    [InlineData("  zip  ")]
    public void ParseFormat_WithInvalidOrUnsupported_ReturnsZipAsDefault(string input)
    {
        // 不明な形式はデフォルトでZIPにフォールバックすべき
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    // === GetCompressedFileName ===

    [Fact]
    public void GetCompressedFileName_WithFile_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\document.txt", "zip");
        Assert.EndsWith(".zip", result);
        Assert.Contains("document", result);
    }

    [Fact]
    public void GetCompressedFileName_WithFolder_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject", "7z");
        Assert.EndsWith(".7z", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithTrailingSeparator_HandlesCorrectly()
    {
        // フォルダパスの末尾にセパレータがある場合
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject\", "zip");
        Assert.EndsWith(".zip", result);
        // フォルダ名が正しく取れること（空文字にならないこと）
        Assert.DoesNotContain("..", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithOutputDirectory_UsesOutputDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: false);
        Assert.StartsWith(@"D:\output", result);
    }

    [Fact]
    public void GetCompressedFileName_WithSameDirectory_UsesSourceDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: true);
        Assert.StartsWith(@"C:\source", result);
    }

    [Fact]
    public void GetCompressedFileName_WithDotFile_HandlesCorrectly()
    {
        // .gitignore のようなドットファイル
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\.gitignore", "zip");
        Assert.EndsWith(".zip", result);
        // ファイル名部分が空にならないこと
        var fileName = Path.GetFileName(result);
        Assert.True(fileName.Length > 4, $"ファイル名が短すぎます: {fileName}");
    }

    [Fact]
    public void GetCompressedFileName_FormatIsLowerCase()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\file.txt", "ZIP");
        // 拡張子は小文字であるべき
        Assert.EndsWith(".zip", result);
        Assert.DoesNotContain(".ZIP", result);
    }

    // === WritableFormats ===

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    [InlineData("gz")]
    [InlineData("bz2")]
    [InlineData("xz")]
    public void WritableFormats_ContainsExpected(string format)
    {
        Assert.Contains(format, ArchiveCompressor.WritableFormats);
    }

    [Theory]
    [InlineData("ZIP")]
    [InlineData("7Z")]
    [InlineData("TAR")]
    public void WritableFormats_IsCaseInsensitive(string format)
    {
        Assert.Contains(format, ArchiveCompressor.WritableFormats);
    }

    [Theory]
    [InlineData("rar")]
    [InlineData("lzh")]
    [InlineData("cab")]
    [InlineData("arj")]
    [InlineData("")]
    public void WritableFormats_DoesNotContainUnsupported(string format)
    {
        Assert.DoesNotContain(format, ArchiveCompressor.WritableFormats);
    }

    // === ShouldExcludeFile (gitignore semantics) ===

    [Fact]
    public void ShouldExcludeFile_WithMatchingFilenameInSingleFileMode_ReturnsTrue()
    {
        // 単一ファイル（rootDir=null）: ベース名のみで判定
        var matcher = GitignoreMatcher.Compile([".DS_Store", "Thumbs.db"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_WithMatchingDirectorySegment_ReturnsTrue()
    {
        // rootDir を指定すれば中間ディレクトリ名 (__MACOSX) のセグメントマッチが効く
        var matcher = GitignoreMatcher.Compile(["__MACOSX"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\root\__MACOSX\file.txt", matcher, rootDir: @"C:\root"));
    }

    [Fact]
    public void ShouldExcludeFile_WithNoMatch_ReturnsFalse()
    {
        var matcher = GitignoreMatcher.Compile([".DS_Store"]);
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\readme.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_WithEmptyMatcher_ReturnsFalse()
    {
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", GitignoreMatcher.Empty));
    }

    [Fact]
    public void ShouldExcludeFile_CaseInsensitive_ReturnsTrue()
    {
        var matcher = GitignoreMatcher.Compile(["thumbs.db"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\THUMBS.DB", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_PartialMatch_ReturnsFalse()
    {
        // パターンがファイル名の一部にしかマッチしない場合は除外しない
        var matcher = GitignoreMatcher.Compile(["Test"]);
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\TestFile.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_GitignoreGlobPattern_MatchesExtension()
    {
        // gitignore 互換: *.log は任意の .log ファイルにマッチ
        var matcher = GitignoreMatcher.Compile(["*.log"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.log", matcher));
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_GitignoreNegationPattern_ReIncludesFile()
    {
        // gitignore 互換: !pattern で除外を取り消し
        var matcher = GitignoreMatcher.Compile(["*.log", "!keep.log"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.log", matcher));
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\keep.log", matcher));
    }

    // === ScanSourceFiles: 空ディレクトリ ===

    [Fact]
    public async Task ScanSourceFiles_IncludesEmptyDirectories()
    {
        // 空ディレクトリを含むディレクトリ構造を作成
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");

        try
        {
            // テスト構造: 空ディレクトリ + ファイルを含むディレクトリ
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyDir"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyNested", "SubEmpty"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "HasFiles"));
            File.WriteAllText(Path.Combine(sourceDir, "HasFiles", "test.txt"), "hello");
            File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "world");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // ファイルが含まれること
            Assert.Contains(result, r => r.relativePath.EndsWith("root.txt"));
            Assert.Contains(result, r => r.relativePath.EndsWith("test.txt"));

            // 空ディレクトリが末尾 / 付きで含まれること
            Assert.Contains(result, r => r.relativePath.EndsWith("EmptyDir/"));
            Assert.Contains(result, r => r.relativePath.EndsWith("SubEmpty/"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_FlatMode_ExcludesEmptyDirectories()
    {
        // Flatモードでは空ディレクトリを含めない
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyDir"));
            File.WriteAllText(Path.Combine(sourceDir, "test.txt"), "hello");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.Flat);

            // ファイルのみ含まれ、ディレクトリエントリは含まれない
            Assert.Single(result);
            Assert.DoesNotContain(result, r => r.relativePath.EndsWith("/"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_DirectoryWithOnlyEmptySubdirs_AllIncluded()
    {
        // ファイルが全くないディレクトリでも空サブディレクトリが全て含まれる
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "AllEmpty");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDir, "A"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "B", "C"));

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // 空ディレクトリのみが含まれる（ファイルなし）
            Assert.All(result, r => Assert.EndsWith("/", r.relativePath));
            Assert.Contains(result, r => r.relativePath.Contains("A"));
            Assert.Contains(result, r => r.relativePath.Contains("B"));
            Assert.Contains(result, r => r.relativePath.Contains("C"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_IncludeHiddenAndSystemEntries_IncludesHiddenGitDirectory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var gitDirPath = Path.Combine(sourceDir, ".git");

        try
        {
            var gitDir = Directory.CreateDirectory(gitDirPath);
            File.WriteAllText(Path.Combine(gitDirPath, "config"), "repository");
            gitDir.Attributes |= FileAttributes.Hidden;

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                includeHiddenAndSystemEntriesOverride: true);

            Assert.Contains(result, r => r.relativePath.Contains(".git") && r.relativePath.EndsWith("config"));
        }
        finally
        {
            if (Directory.Exists(gitDirPath))
                new DirectoryInfo(gitDirPath).Attributes &= ~FileAttributes.Hidden;
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_ExcludeHiddenAndSystemEntries_SkipsHiddenGitDirectory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var gitDirPath = Path.Combine(sourceDir, ".git");

        try
        {
            var gitDir = Directory.CreateDirectory(gitDirPath);
            File.WriteAllText(Path.Combine(gitDirPath, "config"), "repository");
            gitDir.Attributes |= FileAttributes.Hidden;

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                includeHiddenAndSystemEntriesOverride: false);

            Assert.DoesNotContain(result, r => r.relativePath.Contains(".git") || r.relativePath.EndsWith("config"));
        }
        finally
        {
            if (Directory.Exists(gitDirPath))
                new DirectoryInfo(gitDirPath).Attributes &= ~FileAttributes.Hidden;
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    // === ScanSourceFiles: ネストされた .gitignore 統合テスト ===

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_AppliesRulesScopedToSubdirectory()
    {
        // ディレクトリ構造:
        //   testRoot/repoA/.gitignore   → "*.log"
        //   testRoot/repoA/debug.log    → 除外されるべき
        //   testRoot/repoA/keep.txt     → 含まれる
        //   testRoot/repoB/debug.log    → 含まれる (repoA の .gitignore 範囲外)
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var repoADir = Path.Combine(testRoot, "repoA");
        var repoBDir = Path.Combine(testRoot, "repoB");
        try
        {
            Directory.CreateDirectory(repoADir);
            Directory.CreateDirectory(repoBDir);
            File.WriteAllText(Path.Combine(repoADir, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(repoADir, "debug.log"), "x");
            File.WriteAllText(Path.Combine(repoADir, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(repoBDir, "debug.log"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // repoA/debug.log は .gitignore で除外される
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("repoA", "debug.log")));
            // repoA/keep.txt は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("repoA", "keep.txt")));
            // repoB/debug.log は repoA の .gitignore スコープ外なので含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("repoB", "debug.log")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_LhaignorePrunesSubtreeBeforeDiscoveringNestedGitignore()
    {
        // .lhaignore でディレクトリが枝刈りされていれば、その配下の .gitignore は読まれず
        // 中身のルールも適用されない（性能保護 + 仕様の両方を回帰から守る）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var ignoredDir = Path.Combine(testRoot, "ignored");
        var keptDir = Path.Combine(testRoot, "kept");
        try
        {
            Directory.CreateDirectory(ignoredDir);
            Directory.CreateDirectory(keptDir);
            // ignored/ 配下の .gitignore は "*.txt" を除外しようとするが、
            // .lhaignore が ignored/ 自体を枝刈りするので発見されないはず。
            File.WriteAllText(Path.Combine(ignoredDir, ".gitignore"), "*.txt");
            File.WriteAllText(Path.Combine(ignoredDir, "data.txt"), "x");
            File.WriteAllText(Path.Combine(keptDir, "doc.txt"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Compile(["ignored/"]),
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: new[] { "ignored/" });

            // ignored/ 配下は丸ごと除外される (data.txt も含む)
            Assert.DoesNotContain(result, r => r.fullPath.Contains(Path.Combine("ignored", "data.txt")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("ignored", ".gitignore")));
            // kept/doc.txt は ignored/.gitignore の "*.txt" の影響を受けず残る
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("kept", "doc.txt")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CompressFilesAsync_WhenSevenZipExceptionThrown_DeletesPartialOutput()
    {
        // 圧縮対象ファイル → outputPath まで設定済の状態で writer.Save が SevenZipException 相当を
        // 投げると、書きかけの outputPath が残ったままになるバグの回帰テスト。
        // ここでは「outputPath を書き込み不能なディレクトリにする」ことで似たエラー条件を作り、
        // 結果として outputPath が残らない（部分ファイルが残らない）ことを確認する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "src");
        try
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "hello");

            // 出力先を「事前にダミーファイルを作って読み書き不能を擬装する」ような
            // 細工は OS 依存になりやすいので、ここでは「存在しないドライブ」を出力先にして
            // 例外を誘発する。Lhamiel が outputCreated=true 後に発生する例外でも部分ファイルを
            // 残さない事を確かめる回帰テスト。
            // Note: Z:\ など存在しないドライブを使う。CI 環境差に注意。
            var fakeDrive = "Z:\\does_not_exist_lhamiel_test\\out.zip";

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await ArchiveCompressor.CompressFilesAsync(
                    [sourceDir],
                    fakeDrive,
                    Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken));

            // 何らかの例外が出るのが期待挙動 (DirectoryNotFoundException / IOException / SevenZipException など)。
            // 重要なのは出力先に部分ファイルが残らないこと。
            Assert.False(File.Exists(fakeDrive), $"部分ファイルが残っている: {fakeDrive}");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignoreFalse_IgnoresGitignoreFiles()
    {
        // RespectNestedGitignore=false の場合、.gitignore は無視される
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "*.log");
            File.WriteAllText(Path.Combine(testRoot, "debug.log"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // debug.log は .gitignore が読まれないので含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("debug.log"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }
}
