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
    public async Task ScanSourceFiles_NestedGitignore_XcodeDirectoryReinclude_KeepsSharedFiles()
    {
        // バグ B の end-to-end 回帰テスト。標準 Xcode .gitignore（`*.xcodeproj/*` で潰し、
        // `!*.xcodeproj/xcshareddata/` 等のディレクトリ否定で共有メタを再包含）を実 DFS に通し、
        // git と同じく「再包含ディレクトリ配下の共有ファイルが含まれる」ことを保証する。
        // 構造（writingtoolsjp/macos/ を再現）。すべて実 git の挙動（git check-ignore で検証済）と一致する:
        //   testRoot/macos/.gitignore
        //   testRoot/macos/app.xcodeproj/project.pbxproj                                  → 含む（直接ファイル否定）
        //   testRoot/macos/app.xcodeproj/xcshareddata/xcschemes/App.xcscheme              → 含む（★修正点: dir 否定再包含）
        //   testRoot/macos/app.xcodeproj/xcshareddata/WorkspaceSettings.xcsettings        → 除外（再包含 dir 配下でも個別 re-exclude が効く）
        //   testRoot/macos/app.xcodeproj/project.xcworkspace/contents.xcworkspacedata     → 含む（★修正点: dir 否定再包含 + file 否定）
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var macos = Path.Combine(testRoot, "macos");
        var proj = Path.Combine(macos, "app.xcodeproj");
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "xcshareddata", "xcschemes"));
            Directory.CreateDirectory(Path.Combine(proj, "project.xcworkspace"));
            File.WriteAllText(Path.Combine(macos, ".gitignore"), string.Join('\n',
            [
                "*.xcworkspace",
                "*.xcodeproj/*",
                "!*.xcodeproj/project.pbxproj",
                "!*.xcodeproj/xcshareddata/",
                "!*.xcodeproj/project.xcworkspace/",
                "!*.xcworkspace/contents.xcworkspacedata",
                "**/xcshareddata/WorkspaceSettings.xcsettings",
            ]));
            File.WriteAllText(Path.Combine(proj, "project.pbxproj"), "x");
            File.WriteAllText(Path.Combine(proj, "xcshareddata", "xcschemes", "App.xcscheme"), "x");
            File.WriteAllText(Path.Combine(proj, "xcshareddata", "WorkspaceSettings.xcsettings"), "x");
            File.WriteAllText(Path.Combine(proj, "project.xcworkspace", "contents.xcworkspacedata"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // 直接ファイル否定（従来も OK）
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("app.xcodeproj", "project.pbxproj")));
            // ★ バグ B 修正: ディレクトリ否定再包含の配下が含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("xcschemes", "App.xcscheme")));
            Assert.Contains(result, r => r.fullPath.EndsWith(Path.Combine("project.xcworkspace", "contents.xcworkspacedata")));
            // 再包含ディレクトリ配下でも、個別 re-exclude パターンに当たるファイルは除外されたまま（git と一致）
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("WorkspaceSettings.xcsettings"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_NestedGitignore_AllowListStarBangSrc_StillExcludesSubtree()
    {
        // Codex P2 の end-to-end 回帰防止。`*` + `!src/` の allow-list で、再包含された src の
        // 配下サブディレクトリ src/sub は枝刈りされ、その配下ファイルがアーカイブに混入しないことを保証する
        // （git と一致: src/keep.txt も src/sub/file.txt も top.txt も全て除外）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var proj = Path.Combine(testRoot, "proj");
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "src", "sub"));
            File.WriteAllText(Path.Combine(proj, ".gitignore"), string.Join('\n', ["*", "!src/"]));
            File.WriteAllText(Path.Combine(proj, "top.txt"), "x");
            File.WriteAllText(Path.Combine(proj, "src", "keep.txt"), "x");
            File.WriteAllText(Path.Combine(proj, "src", "sub", "file.txt"), "x");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // ★ Codex P2: 枝刈りされた src/sub 配下は混入しない
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("sub", "file.txt")));
            // src 直下のファイルも top.txt も `*` で除外されたまま
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith(Path.Combine("src", "keep.txt")));
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("top.txt"));
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
        // Windows 固有: 「存在しないドライブレター」を出力先にして例外を誘発する。
        // 非 Windows ではドライブレターの概念が無いのでスキップ。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows のドライブレターに依存するテスト");

        // 圧縮対象ファイル → outputPath まで設定済の状態で writer.Save が SevenZipException 相当を
        // 投げると、書きかけの outputPath が残ったままになるバグの回帰テスト。
        // 結果として outputPath が残らない（部分ファイルが残らない）ことを確認する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "src");
        try
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "hello");

            // 存在しないドライブレターを動的に探す。Z:\ などをハードコードすると、テスト環境で
            // 実際にそのドライブが存在すると例外が出なくなり flake になる。
            var fakeDrive = FindUnusedDriveLetter();
            if (fakeDrive is null)
            {
                Assert.Skip("利用可能な未使用ドライブレターが見つからない");
                return;
            }
            var fakePath = $"{fakeDrive}:\\does_not_exist_lhamiel_test\\out.zip";

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await ArchiveCompressor.CompressFilesAsync(
                    [sourceDir],
                    fakePath,
                    Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken));

            // 何らかの例外が出るのが期待挙動 (DirectoryNotFoundException / IOException / SevenZipException など)。
            // 重要なのは出力先に部分ファイルが残らないこと。
            Assert.False(File.Exists(fakePath), $"部分ファイルが残っている: {fakePath}");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    /// <summary>
    /// テスト用に「現在マウントされていないドライブレター」を 1 つ返す。見つからなければ null。
    /// D〜Z を逆順 (Z 寄り) で探す（A〜C はシステム予約寄りで避ける）。
    /// </summary>
    private static char? FindUnusedDriveLetter()
    {
        var inUse = DriveInfo.GetDrives()
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToHashSet();
        for (var c = 'Z'; c >= 'D'; c--)
        {
            if (!inUse.Contains(c))
                return c;
        }
        return null;
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_FallbackMatcherRulesArePreserved()
    {
        // Codex P2 指摘対応 (#3305241279): respectNestedGitignore=true かつ globalIgnoreLines=null
        // のとき、呼び出し元から渡された matcher (fallbackMatcher) のルールが silent ドロップされる
        // 経路があった。修正後は fallbackMatcher が base layer として必ず保持されることを確認する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            // .lhaignore 由来の matcher を直接構築 (生 lines は ScanSourceFiles に渡さない)
            var fallbackMatcher = GitignoreMatcher.Compile(["*.tmp"]);

            // テストファイル: tmp/log の混在
            File.WriteAllText(Path.Combine(testRoot, "data.tmp"), "should be excluded");
            File.WriteAllText(Path.Combine(testRoot, "data.log"), "should be included");

            // globalIgnoreLines = null だが、fallbackMatcher の "*.tmp" は効くべき
            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                fallbackMatcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: null); // ← Codex 指摘の null パス

            // *.tmp は fallbackMatcher で除外される
            Assert.DoesNotContain(result, r => r.fullPath.EndsWith("data.tmp"));
            // *.log は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("data.log"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_RespectNestedGitignore_RootGitignorePrunesNestedDiscovery()
    {
        // RTK レビュー Codex P2 対応: DiscoverGitignoreFiles が root の .gitignore を読み込んだ後、
        // サブディレクトリ走査でもそのルールを枝刈りに使うことを確認する。
        // 具体的には、root .gitignore で "vendor/" を除外している場合、vendor/ 配下の nested .gitignore
        // は読まれず、vendor/ 配下のファイルも圧縮対象から除外される。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(testRoot);
            // root .gitignore で vendor/ を除外
            File.WriteAllText(Path.Combine(testRoot, ".gitignore"), "vendor/\n");

            // vendor/ 配下にダミーファイルと、さらに別のルールを持つ nested .gitignore を配置
            // 期待: vendor/ ごと枝刈りされるので、nested .gitignore は読み込まれず、vendor/*.txt も含まれない
            var vendorDir = Path.Combine(testRoot, "vendor");
            Directory.CreateDirectory(vendorDir);
            File.WriteAllText(Path.Combine(vendorDir, ".gitignore"), "!keep.txt\n");
            File.WriteAllText(Path.Combine(vendorDir, "lib.txt"), "x");
            File.WriteAllText(Path.Combine(vendorDir, "keep.txt"), "x");

            // 一方、root 配下の通常ファイルは含まれる
            File.WriteAllText(Path.Combine(testRoot, "app.txt"), "x");

            // respectNestedGitignore=true の場合は globalIgnoreLines も必須
            // (BuildLayeredMatcherForSource の条件: respectNestedGitignore && globalIgnoreLines is not null)
            var result = await ArchiveCompressor.ScanSourceFiles(
                [testRoot],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true,
                globalIgnoreLines: Array.Empty<string>());

            // app.txt は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("app.txt"));
            // vendor/ 配下は root .gitignore で除外されて含まれない（nested .gitignore の !keep.txt も到達しない）
            Assert.DoesNotContain(result, r => r.fullPath.Contains("vendor"));
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

    [Fact]
    public async Task ScanSourceFiles_SourceRootBasenameMatchesAnchoredPattern_DoesNotEmptyArchive()
    {
        // ユーザーが「build」という名前のフォルダを圧縮対象として明示的に指定したとき、
        // `.lhaignore` / `.gitignore` の anchored パターン `/build` でルート自身が
        // 除外されて空アーカイブになる回帰を防ぐ（Codex P1 指摘 2026-05-26）。
        // gitignore セマンティクスでは `/build` は親基準でルート直下にマッチするものであり、
        // ユーザーが明示指定したソースルート自身を意味しない。
        var parent = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var buildDir = Path.Combine(parent, "build");
        try
        {
            Directory.CreateDirectory(buildDir);
            File.WriteAllText(Path.Combine(buildDir, "artifact.txt"), "x");
            File.WriteAllText(Path.Combine(buildDir, "log.txt"), "x");

            // anchored パターン `/build` を含む matcher
            var matcher = GitignoreMatcher.Compile(["/build"]);

            var result = await ArchiveCompressor.ScanSourceFiles(
                [buildDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // ルート basename が "build" でも、anchored `/build` でルート自身は除外されない
            Assert.Contains(result, r => r.fullPath.EndsWith("artifact.txt"));
            Assert.Contains(result, r => r.fullPath.EndsWith("log.txt"));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_SourceRootBasenameMatchesDirectoryPattern_DoesNotEmptyArchive()
    {
        // ユーザーが「node_modules」という名前のフォルダを圧縮対象として明示指定したとき、
        // `node_modules/` パターンで空にならず、配下ファイルが正しく含まれることを保証する。
        // ignore ルールは子エントリに適用されるべきで、ユーザー明示指定のルートを覆さない。
        var parent = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var nodeModulesDir = Path.Combine(parent, "node_modules");
        try
        {
            Directory.CreateDirectory(nodeModulesDir);
            File.WriteAllText(Path.Combine(nodeModulesDir, "package.json"), "{}");

            var matcher = GitignoreMatcher.Compile(["node_modules/"]);

            var result = await ArchiveCompressor.ScanSourceFiles(
                [nodeModulesDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: false);

            // ユーザー明示指定の node_modules ルート配下は含まれる
            Assert.Contains(result, r => r.fullPath.EndsWith("package.json"));
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }

    // === 生成アーカイブの内容検証（スキャン出力ではなく成果物を ArchiveReader.Items で確認） ===

    [Fact]
    public async Task CompressFilesAsync_EmptyDirectoryWithExcludedFiles_DoesNotReviveExcludedFilesInArchive()
    {
        // RTK レビュー #1 (EMPTY-DIR-LEAK) 回帰テスト。
        // スキャンで「空」と判定されたディレクトリ（実体には除外ファイルだけが残る）を
        // writer.Add(realDir, "rel/") で渡すと、ライブラリの AddRecursive がフィルタなしに再走査して
        // 除外ファイルを復活させていた。空マーカー経由の追加でこれが起きないことを、
        // スキャン出力ではなく「生成アーカイブの ArchiveReader.Items」レベルで検証する（#16 SCAN-TEST-GAP）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var logsDir = Path.Combine(sourceDir, "logs");
        try
        {
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(Path.Combine(sourceDir, "keep.txt"), "keep me");
            // logs/ の中身は除外対象 (*.log) のみ → スキャン的には「空ディレクトリ」になる。
            File.WriteAllText(Path.Combine(logsDir, "secret.log"), "should NOT be in archive");

            var matcher = GitignoreMatcher.Compile(["*.log"]);

            // スキャンで解決済みリストを得る（*.log は除外、logs/ は空ディレクトリエントリとして残る）。
            var resolved = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // サニティ: スキャン段階で secret.log は除外され、logs/ は空ディレクトリとして残る。
            Assert.DoesNotContain(resolved, r => r.relativePath.EndsWith("secret.log"));
            Assert.Contains(resolved, r => r.relativePath.EndsWith("logs/"));
            Assert.Contains(resolved, r => r.relativePath.EndsWith("keep.txt"));

            // 解決済みリストをそのまま渡して圧縮（内部 .lhaignore 読み込みをバイパスし、上の matcher 結果で固定）。
            var zipPath = Path.Combine(testRoot, "out.zip");
            await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], zipPath, Format.Zip,
                new Progress<ProgressInfo>(),
                TestContext.Current.CancellationToken,
                resolvedFiles: resolved);

            Assert.True(File.Exists(zipPath), "アーカイブが生成されていない");

            // 生成アーカイブの中身を検証する（ここが本丸: 成果物に除外ファイルが復活していないこと）。
            using var reader = new ArchiveReader(zipPath);
            var names = reader.Items.Select(i => (i.FullName ?? string.Empty).Replace('\\', '/')).ToList();

            // 除外した secret.log がアーカイブに復活していないこと（#1 の回帰防止）。
            Assert.DoesNotContain(names, n => n.EndsWith("secret.log"));
            // 含めるべき keep.txt は存在すること。
            Assert.Contains(names, n => n.EndsWith("keep.txt"));
            // 空ディレクトリ logs/ のエントリは保持されること。
            Assert.Contains(names, n => n.TrimEnd('/').EndsWith("logs"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }
}
