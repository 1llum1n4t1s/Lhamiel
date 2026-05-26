using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using System.IO.Compression;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveExtractor class unit tests
/// </summary>
[Collection("Sequential")]
public class ArchiveExtractorTests
{
    /// <summary>
    /// テスト用の一時ディレクトリを作成する
    /// </summary>
    /// <returns>一時ディレクトリのパス</returns>
    private static string CreateTemporaryTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ArchiveExtractorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（二重フォルダあり）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithDoubleFolder(string testDir)
    {
        // テスト用の構造：ProjectA.zip 内に ProjectA フォルダがあり、その中に files があるケース
        var parentDir = Path.Combine(testDir, "temp_for_zip");
        var projectDir = Path.Combine(parentDir, "ProjectA");
        var innerDir = Path.Combine(projectDir, "files");
        var dataDir = Path.Combine(innerDir, "data");

        Directory.CreateDirectory(dataDir);

        // テストファイルを作成
        File.WriteAllText(Path.Combine(projectDir, "readme.txt"), "Project A Readme");
        File.WriteAllText(Path.Combine(innerDir, "config.txt"), "Configuration");
        File.WriteAllText(Path.Combine(dataDir, "data.txt"), "Data content");

        var zipPath = Path.Combine(testDir, "ProjectA.zip");
        // 親ディレクトリを圧縮することで、ProjectA/ フォルダを含める
        ZipFile.CreateFromDirectory(parentDir, zipPath);

        // テスト用ディレクトリを削除（ZIPに含めるためだけ）
        Directory.Delete(parentDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（複数のルートレベルフォルダあり）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithMultipleFolders(string testDir)
    {
        // テスト用の構造：ProjectB.zip のルートに複数のフォルダがあるケース
        // folder1/ と folder2/ が ZIP ルートの直下に存在する
        var parentDir = Path.Combine(testDir, "temp_for_multi_zip");
        var folder1 = Path.Combine(parentDir, "folder1");
        var folder2 = Path.Combine(parentDir, "folder2");

        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        File.WriteAllText(Path.Combine(folder1, "file1.txt"), "File 1");
        File.WriteAllText(Path.Combine(folder2, "file2.txt"), "File 2");

        var zipPath = Path.Combine(testDir, "ProjectB.zip");
        // 親ディレクトリを圧縮することで、folder1 と folder2 がルートレベルに来る
        ZipFile.CreateFromDirectory(parentDir, zipPath);

        // テスト用ディレクトリを削除
        Directory.Delete(parentDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（複数のルートレベルファイル）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithMultipleRootFiles(string testDir)
    {
        // テスト用の構造：ProjectC.zip のルートに複数のファイルがあるケース
        var projectDir = Path.Combine(testDir, "ProjectC");
        Directory.CreateDirectory(projectDir);

        File.WriteAllText(Path.Combine(projectDir, "file1.txt"), "File 1");
        File.WriteAllText(Path.Combine(projectDir, "file2.txt"), "File 2");
        File.WriteAllText(Path.Combine(projectDir, "readme.md"), "README");

        var zipPath = Path.Combine(testDir, "ProjectC.zip");
        ZipFile.CreateFromDirectory(projectDir, zipPath);

        // テスト用ディレクトリを削除
        Directory.Delete(projectDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（__MACOSXとProjectDがルートに並ぶ）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithMacOsxAndProject(string testDir)
    {
        var parentDir = Path.Combine(testDir, "temp_for_projectd_zip");
        var macosxDir = Path.Combine(parentDir, "__MACOSX");
        var projectDir = Path.Combine(parentDir, "ProjectD");
        var srcDir = Path.Combine(projectDir, "src");

        Directory.CreateDirectory(macosxDir);
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(macosxDir, ".DS_Store"), "macOS metadata");
        File.WriteAllText(Path.Combine(projectDir, "README.md"), "Project D Readme");
        File.WriteAllText(Path.Combine(srcDir, "main.txt"), "Source content");

        var zipPath = Path.Combine(testDir, "ProjectD.zip");
        ZipFile.CreateFromDirectory(parentDir, zipPath);

        Directory.Delete(parentDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（無視対象のシステムファイル desktop.ini / Thumbs.db / .DS_Store を含む）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithIgnoredSystemFiles(string testDir)
    {
        var parentDir = Path.Combine(testDir, "temp_for_ignored_zip");
        var projectDir = Path.Combine(parentDir, "ProjectE");
        var subDir = Path.Combine(projectDir, "sub");

        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(projectDir, "README.md"), "Project E Readme");
        File.WriteAllText(Path.Combine(projectDir, "desktop.ini"), "Windows folder metadata");
        File.WriteAllText(Path.Combine(projectDir, "Thumbs.db"), "Windows thumbnail cache");
        File.WriteAllText(Path.Combine(projectDir, ".DS_Store"), "macOS folder metadata");
        File.WriteAllText(Path.Combine(subDir, "data.txt"), "Data content");
        File.WriteAllText(Path.Combine(subDir, "desktop.ini"), "Subfolder desktop.ini");

        var zipPath = Path.Combine(testDir, "ProjectE.zip");
        ZipFile.CreateFromDirectory(parentDir, zipPath);

        Directory.Delete(parentDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する（再帰的な同名フォルダのネスト）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithRecursiveNestedFolders(string testDir)
    {
        // テスト用の構造：ABC.zip 内に ABC/ABC/ABC/ABC/ABC/中身/ があるケース
        var parentDir = Path.Combine(testDir, "temp_for_abc_zip");
        var level1 = Path.Combine(parentDir, "ABC");
        var level2 = Path.Combine(level1, "ABC");
        var level3 = Path.Combine(level2, "ABC");
        var level4 = Path.Combine(level3, "ABC");
        var level5 = Path.Combine(level4, "ABC");
        var contentsDir = Path.Combine(level5, "中身");

        Directory.CreateDirectory(contentsDir);

        File.WriteAllText(Path.Combine(contentsDir, "file1.txt"), "File 1");
        File.WriteAllText(Path.Combine(contentsDir, "file2.txt"), "File 2");

        var zipPath = Path.Combine(testDir, "ABC.zip");
        // 親ディレクトリを圧縮することで、ABC/ フォルダを含める
        ZipFile.CreateFromDirectory(parentDir, zipPath);

        // テスト用ディレクトリを削除
        Directory.Delete(parentDir, true);

        return zipPath;
    }

    /// <summary>
    /// デスクトップに上書き対象のフォルダがない場合、上書き確認ダイアログを表示しないこと（過去の不具合の回帰防止）
    /// </summary>
    [Fact]
    public void ShouldShowOverwriteDialog_WhenOutputPathExistsButOverwriteTargetDoesNotExist_ReturnsFalse()
    {
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // Arrange: outputPath（親フォルダ＝デスクトップ相当）は存在するが、実際の上書き対象（ProjectD）は存在しない
            var outputPath = tempDir;
            var overwriteTargetPath = Path.Combine(outputPath, "ProjectD");
            var overwriteCheckPaths = new[] { overwriteTargetPath };

            Assert.True(Directory.Exists(outputPath), "outputPath should exist (like Desktop)");
            Assert.False(Directory.Exists(overwriteTargetPath), "ProjectD should NOT exist on outputPath");

            // Act
            var result = ArchiveExtractor.ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

            // Assert: 上書き対象が存在しないためダイアログを表示すべきでない
            Assert.False(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// overwriteCheckPaths未指定でoutputPathが存在する場合、ダイアログを表示すること
    /// </summary>
    [Fact]
    public void ShouldShowOverwriteDialog_WhenOutputPathExistsAndNoOverwriteCheckPaths_ReturnsTrue()
    {
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            var outputPath = tempDir;
            Assert.True(Directory.Exists(outputPath));

            var result = ArchiveExtractor.ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths: null);

            Assert.True(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// overwriteCheckPaths内のいずれかが存在する場合、ダイアログを表示すること
    /// </summary>
    [Fact]
    public void ShouldShowOverwriteDialog_WhenOverwriteTargetExists_ReturnsTrue()
    {
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            var outputPath = tempDir;
            var existingFolder = Path.Combine(outputPath, "ExistingProject");
            Directory.CreateDirectory(existingFolder);
            var overwriteCheckPaths = new[] { existingFolder };

            var result = ArchiveExtractor.ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

            Assert.True(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsSupportedArchiveType_WithZipExtension_ReturnsTrue()
    {
        // Act & Assert
        var result = ArchiveExtractor.IsSupportedArchiveType("test.zip");
        Assert.True(result);
    }

    [Fact]
    public void IsSupportedArchiveType_WithUnsupportedExtension_ReturnsFalse()
    {
        // Act & Assert
        var result = ArchiveExtractor.IsSupportedArchiveType("test.txt");
        Assert.False(result);
    }

    [Fact]
    public void GetOutputDirectory_WithValidPath_ReturnsExpectedPath()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // テスト用ディレクトリ内にダミーファイルを作成
            var dummyFile = Path.Combine(tempDir, "dummy.txt");
            File.WriteAllText(dummyFile, "test");

            var outputDir = Path.Combine(tempDir, "output");

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(dummyFile, outputDir);

            // Assert
            // outputDir の下に dummy というフォルダが作成されるはず
            Assert.Contains("dummy", result);
            Assert.StartsWith(outputDir, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetOutputDirectory_WithSingleFolderInRoot_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // ルートレベルに1つだけフォルダがあるZIPファイルを作成
            var zipPath = CreateTestZipWithDoubleFolder(tempDir);
            var outputDir = Path.Combine(tempDir, "output");
            var zipFileName = Path.GetFileNameWithoutExtension(zipPath); // "ProjectA"

            // デバッグ: アーカイブ内容と期待値を出力
            using (var reader = new ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                foreach (var item in contents)
                {
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert: ケース1 - ルートアイテムが1つ＋フォルダ
            // GetOutputDirectory は アーカイブ名フォルダを返す（リフトアップは展開時に行う）
            var expectedPath = Path.Combine(outputDir, zipFileName);
            Assert.Equal(expectedPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetOutputDirectory_WithMultipleFoldersInRoot_CreatesFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // 複数フォルダのZIPファイルを作成
            var zipPath = CreateTestZipWithMultipleFolders(tempDir);
            var outputDir = Path.Combine(tempDir, "output");
            var zipFileName = Path.GetFileNameWithoutExtension(zipPath); // "ProjectB"

            // デバッグ: アーカイブ内容と期待値を出力
            using (var reader = new ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                foreach (var item in contents)
                {
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            var expectedPath = Path.Combine(outputDir, zipFileName);

            // Assert: ケース2 - ルートレベルに複数のアイテムがある場合
            // GetOutputDirectory はアーカイブ名フォルダを返す
            Assert.Equal(expectedPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetOutputDirectory_WithDeepNestedFolders_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // 深いネストを持つZIPファイルを作成
            var zipPath = CreateTestZipWithRecursiveNestedFolders(tempDir);
            var outputDir = Path.Combine(tempDir, "output");
            var zipFileName = Path.GetFileNameWithoutExtension(zipPath); // "ABC"

            // デバッグ: アーカイブ内容と期待値を出力
            using (var reader = new ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                foreach (var item in contents)
                {
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert: ケース1 - ルートアイテムが1つ＋フォルダ（ABC フォルダ）
            // GetOutputDirectory はアーカイブ名フォルダを返す（リフトアップは展開時に行う）
            var expectedPath = Path.Combine(outputDir, zipFileName);
            Assert.Equal(expectedPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExtractArchive_WithSingleRootFolder_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // ルートレベルに1つだけフォルダがあるZIPファイルを作成
            var zipPath = CreateTestZipWithDoubleFolder(tempDir);
            Path.GetFileNameWithoutExtension(zipPath);
            var baseOutputDir = Path.Combine(tempDir, "extract_output");

            using (var reader = new ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                foreach (var item in contents.Take(5))
                {
                }
                if (contents.Count > 5)
                {
                }
            }

            // Act: GetOutputDirectory を使用して正しい展開先を決定する
            var actualOutputDir = ArchiveExtractor.GetOutputDirectory(zipPath, baseOutputDir);
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(zipPath, actualOutputDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: 展開結果を確認
            var topLevelItems = Directory.GetFileSystemEntries(actualOutputDir);
            foreach (var item in topLevelItems.Take(5))
            {
                var name = Path.GetFileName(item);
                var isDir = Directory.Exists(item) ? "(dir)" : "(file)";
            }

            // ケース1: ルートアイテムが1つ＋フォルダ
            // ProjectA フォルダが actualOutputDir (baseOutputDir/ProjectA) の下に作成されている
            var projectAPath = Path.Combine(actualOutputDir, "ProjectA");
            var readmePath = Path.Combine(projectAPath, "readme.txt");
            var filesPath = Path.Combine(projectAPath, "files");

            Assert.True(Directory.Exists(projectAPath), "ProjectA folder should exist in output directory");
            Assert.True(File.Exists(readmePath), "readme.txt should exist in ProjectA folder");
            Assert.True(Directory.Exists(filesPath), "files folder should exist in ProjectA folder");

        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExtractArchive_WithMultipleRootItems_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // ルートレベルに複数のアイテムがあるZIPファイルを作成
            var zipPath = CreateTestZipWithMultipleFolders(tempDir);
            var baseOutputDir = Path.Combine(tempDir, "extract_output");

            using (var reader = new ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                foreach (var item in contents)
                {
                }
            }

            // Act: GetOutputDirectory を使用して正しい展開先を決定する
            var actualOutputDir = ArchiveExtractor.GetOutputDirectory(zipPath, baseOutputDir);
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(zipPath, actualOutputDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: 展開結果を確認
            var topLevelItems = Directory.GetFileSystemEntries(actualOutputDir);
            foreach (var item in topLevelItems)
            {
                var name = Path.GetFileName(item);
                var isDir = Directory.Exists(item) ? "(dir)" : "(file)";
            }

            // ケース2: ルートアイテムが複数
            // folder1 と folder2 が直接存在することを確認
            var folder1Path = Path.Combine(actualOutputDir, "folder1");
            var folder2Path = Path.Combine(actualOutputDir, "folder2");
            Assert.True(Directory.Exists(folder1Path), "folder1 should exist in output directory");
            Assert.True(Directory.Exists(folder2Path), "folder2 should exist in output directory");

            // folder1 と folder2 の中身も確認
            var file1Path = Path.Combine(folder1Path, "file1.txt");
            var file2Path = Path.Combine(folder2Path, "file2.txt");
            Assert.True(File.Exists(file1Path), "file1.txt should exist in folder1");
            Assert.True(File.Exists(file2Path), "file2.txt should exist in folder2");

        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExtractArchive_WithMacOsxFolder_ExcludesMacOsxFromOutput()
    {
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            var zipPath = CreateTestZipWithMacOsxAndProject(tempDir);
            var baseOutputDir = Path.Combine(tempDir, "extract_output");
            var outputPath = baseOutputDir;
            IReadOnlyList<string> overwriteCheckPaths = [Path.Combine(outputPath, "ProjectD")];

            await ArchiveExtractor.ExtractArchive(zipPath, outputPath, null, null, false, TestContext.Current.CancellationToken, overwriteCheckPaths);

            var projectDPath = Path.Combine(outputPath, "ProjectD");
            var macOsxPath = Path.Combine(outputPath, "__MACOSX");

            Assert.True(Directory.Exists(projectDPath), "ProjectD folder should exist in output");
            Assert.False(Directory.Exists(macOsxPath), "__MACOSX folder must not be extracted to output");
            Assert.True(File.Exists(Path.Combine(projectDPath, "README.md")), "ProjectD/README.md should exist");
            Assert.True(Directory.Exists(Path.Combine(projectDPath, "src")), "ProjectD/src should exist");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// 無視対象のシステムファイル（desktop.ini / Thumbs.db / .DS_Store）が展開結果に含まれないこと
    /// </summary>
    [Fact]
    public async Task ExtractArchive_WithIgnoredSystemFiles_ExcludesDesktopIniThumbsDbAndDS_StoreFromOutput()
    {
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            var zipPath = CreateTestZipWithIgnoredSystemFiles(tempDir);
            var baseOutputDir = Path.Combine(tempDir, "extract_output");
            var outputPath = baseOutputDir;

            await ArchiveExtractor.ExtractArchive(zipPath, outputPath, null, null, false, TestContext.Current.CancellationToken);

            var projectEPath = Path.Combine(outputPath, "ProjectE");
            var subPath = Path.Combine(projectEPath, "sub");

            Assert.True(Directory.Exists(projectEPath), "ProjectE folder should exist in output (single-root extract to baseOutputDir)");
            Assert.True(File.Exists(Path.Combine(projectEPath, "README.md")), "ProjectE/README.md should exist");
            Assert.True(Directory.Exists(subPath), "ProjectE/sub should exist");
            Assert.True(File.Exists(Path.Combine(subPath, "data.txt")), "ProjectE/sub/data.txt should exist");

            var allFiles = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories);
            var fileNames = allFiles.Select(Path.GetFileName).ToList();
            Assert.DoesNotContain(fileNames, n => string.Equals(n, "desktop.ini", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(fileNames, n => string.Equals(n, "Thumbs.db", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(fileNames, n => string.Equals(n, ".DS_Store", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// 圧縮終了直後の自プロセスロックや Defender 瞬間ロックを擬装。
    /// `LockedFileRetryPolicy` 経由のリトライで救済され、最終的に正しく展開できることを検証する
    /// （以前は <c>SHARING_VIOLATION (0x80070020)</c> で即 throw して展開できなかった）。
    /// </summary>
    [Fact]
    public async Task ExtractArchive_WhenArchiveBrieflyLockedAtOpen_RetriesAndSucceeds()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 1. 単純な ZIP を作成
            var zipPath = Path.Combine(testDir, "locked.zip");
            using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = z.CreateEntry("hello.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("hello locked world");
            }

            // 2. ZIP を排他ロック (`FileShare.None`)
            var holder = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.None);

            // 3. 300ms 後にロック解除する別タスク（リトライ機構の最初の待機 200ms より後、合計待機 600ms 以内）。
            //    CancellationToken.None を使う + finally で holder を確実に Dispose して、テストキャンセル時の
            //    ファイルハンドルリークを防ぐ。
            var unlocker = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, CancellationToken.None);
                }
                finally
                {
                    holder.Dispose();
                }
            });

            try
            {
                // 4. ExtractArchive を呼ぶ。OpenArchiveReaderWithRetry の 200ms→400ms バックオフで
                //    途中でロックが外れ、2 回目か 3 回目の試行で reader 生成に成功するはず。
                var outputDir = Path.Combine(testDir, "extracted");
                await ArchiveExtractor.ExtractArchive(zipPath, outputDir, cancellationToken: TestContext.Current.CancellationToken);

                await unlocker;

                // 5. 結果: hello.txt が中身込みで展開されていること
                var outputFile = Path.Combine(outputDir, "hello.txt");
                Assert.True(File.Exists(outputFile), $"展開ファイルが見つからない: {outputFile}");
                Assert.Equal("hello locked world", File.ReadAllText(outputFile));
            }
            finally
            {
                // 上で例外が出て unlocker に到達しないケースでも holder を必ず解放する。
                holder.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
}
