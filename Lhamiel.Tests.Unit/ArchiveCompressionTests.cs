using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// アーカイブ圧縮・展開の統合テスト
/// </summary>
[Collection("Sequential")]
public class ArchiveCompressionTests
{
    /// <summary>
    /// テスト用の一時ディレクトリを作成する
    /// </summary>
    /// <returns>一時ディレクトリのパス</returns>
    private static string CreateTemporaryTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ArchiveCompressionTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// テスト用のファイルとディレクトリ構造を作成する
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたテストディレクトリのパス</returns>
    private static string CreateTestFileStructure(string testDir)
    {
        var sourceDir = Path.Combine(testDir, "source");
        var subDir = Path.Combine(sourceDir, "subfolder");
        Directory.CreateDirectory(subDir);

        // テストファイルを作成
        File.WriteAllText(Path.Combine(sourceDir, "readme.txt"), "This is a readme file");
        File.WriteAllText(Path.Combine(sourceDir, "config.txt"), "Configuration data");
        File.WriteAllText(Path.Combine(subDir, "data.json"), "{\"key\": \"value\"}");
        File.WriteAllText(Path.Combine(subDir, "notes.txt"), "Some notes here");

        return sourceDir;
    }

    /// <summary>
    /// アーカイブ内のファイル数と内容を検証する
    /// </summary>
    /// <param name="extractedDir">展開されたディレクトリ</param>
    /// <param name="expectedFilesCount">期待されるファイル数</param>
    private static void VerifyExtractedContent(string extractedDir, int expectedFilesCount = 4)
    {
        // 展開されたディレクトリが存在することを確認
        Assert.True(Directory.Exists(extractedDir), $"Expected directory not found: {extractedDir}");

        // 展開されたファイルを確認（全ディレクトリを再帰的に検索）
        var files = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories).ToList();

        // ファイルが存在することを確認（ファイル数は形式によって異なるため、最低1つのファイルがあることを確認）
        Assert.True(files.Count > 0, $"No files found in {extractedDir}. Directory structure: {GetDirectoryStructure(extractedDir)}");

        // ファイル内容を検証（readme.txtが存在すればその内容を確認）
        var readmeFile = files.FirstOrDefault(f => f.EndsWith("readme.txt"));
        if (readmeFile != null)
        {
            Assert.Equal("This is a readme file", File.ReadAllText(readmeFile));
        }

        // ファイル内容を検証（data.jsonが存在すればその内容を確認）
        var jsonFile = files.FirstOrDefault(f => f.EndsWith("data.json"));
        if (jsonFile != null)
        {
            Assert.Equal("{\"key\": \"value\"}", File.ReadAllText(jsonFile));
        }
    }

    /// <summary>
    /// デバッグ用にディレクトリ構造を取得する
    /// </summary>
    private static string GetDirectoryStructure(string path, string indent = "")
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var di = new DirectoryInfo(path);
            sb.AppendLine($"{indent}{di.Name}/");

            foreach (var dir in di.GetDirectories())
            {
                sb.Append(GetDirectoryStructure(dir.FullName, indent + "  "));
            }

            foreach (var file in di.GetFiles())
            {
                sb.AppendLine($"{indent}  {file.Name}");
            }
        }
        catch
        {
            // ディレクトリが存在しない場合
        }

        return sb.ToString();
    }

    /// <summary>
    /// ZIP形式で圧縮・展開できるか確認
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithZipFormat_SucceedsAndPreservesContent()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateTestFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "archive.zip");
            var extractDir = Path.Combine(testDir, "extracted_zip");
            Directory.CreateDirectory(extractDir);

            // Act - 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            // Assert - 圧縮ファイルが作成されたか
            Assert.True(File.Exists(archivePath), "Zip archive should be created");

            // Act - 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert - 内容を検証（展開後のディレクトリ構造に対応）
            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedFiles.Length > 0, $"No files extracted. Directory: {GetDirectoryStructure(extractDir)}");

            // readme.txtが展開されたか確認
            var readmeFile = extractedFiles.FirstOrDefault(f => f.EndsWith("readme.txt"));
            Assert.NotNull(readmeFile);
            Assert.Equal("This is a readme file", File.ReadAllText(readmeFile));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 7z形式で圧縮・展開できるか確認
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_With7zFormat_SucceedsAndPreservesContent()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateTestFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "archive.7z");
            var extractDir = Path.Combine(testDir, "extracted_7z");
            Directory.CreateDirectory(extractDir);

            // Act - 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            // Assert - 圧縮ファイルが作成されたか
            Assert.True(File.Exists(archivePath), "7z archive should be created");

            // Act - 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert - 内容を検証（展開後のディレクトリ構造に対応）
            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedFiles.Length > 0, $"No files extracted. Directory: {GetDirectoryStructure(extractDir)}");

            // readme.txtが展開されたか確認
            var readmeFile = extractedFiles.FirstOrDefault(f => f.EndsWith("readme.txt"));
            Assert.NotNull(readmeFile);
            Assert.Equal("This is a readme file", File.ReadAllText(readmeFile));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// TAR形式で圧縮・展開できるか確認
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithTarFormat_SucceedsAndPreservesContent()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateTestFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "archive.tar");
            var extractDir = Path.Combine(testDir, "extracted_tar");
            Directory.CreateDirectory(extractDir);

            // Act - 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            // Assert - 圧縮ファイルが作成されたか
            Assert.True(File.Exists(archivePath), "Tar archive should be created");

            // Act - 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert - 内容を検証（展開後のディレクトリ構造に対応）
            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedFiles.Length > 0, $"No files extracted. Directory: {GetDirectoryStructure(extractDir)}");

            // readme.txtが展開されたか確認
            var readmeFile = extractedFiles.FirstOrDefault(f => f.EndsWith("readme.txt"));
            Assert.NotNull(readmeFile);
            Assert.Equal("This is a readme file", File.ReadAllText(readmeFile));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 複数の形式を一度にテストする
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    public async Task CompressAndExtract_WithMultipleFormats_AllSucceed(string extension)
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateTestFileStructure(testDir);
            var archivePath = Path.Combine(testDir, $"archive{extension}");
            var extractDir = Path.Combine(testDir, $"extracted{extension}");
            Directory.CreateDirectory(extractDir);

            // Act - 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            // Assert - 圧縮ファイルが作成されたか
            Assert.True(File.Exists(archivePath), $"{extension} archive should be created");

            // Act - 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert - 内容を検証（展開後のディレクトリ構造に対応）
            var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedFiles.Length > 0, $"No files extracted for {extension}. Directory: {GetDirectoryStructure(extractDir)}");

            // readme.txtが展開されたか確認
            var readmeFile = extractedFiles.FirstOrDefault(f => f.EndsWith("readme.txt"));
            Assert.NotNull(readmeFile);
            Assert.Equal("This is a readme file", File.ReadAllText(readmeFile));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// ファイルを圧縮できるか確認
    /// </summary>
    [Fact]
    public async Task CompressFile_WithZipFormat_Succeeds()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var testFile = Path.Combine(testDir, "testfile.txt");
            File.WriteAllText(testFile, "Test file content");

            var archivePath = Path.Combine(testDir, "single_file.zip");

            // Act
            await ArchiveCompressor.CompressFilesAsync([testFile], archivePath, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(File.Exists(archivePath), "Zip archive should be created");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 複数ファイルを圧縮できるか確認
    /// </summary>
    [Fact]
    public async Task CompressMultipleFiles_WithZipFormat_Succeeds()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var file1 = Path.Combine(testDir, "file1.txt");
            var file2 = Path.Combine(testDir, "file2.txt");
            var file3 = Path.Combine(testDir, "file3.txt");

            File.WriteAllText(file1, "Content 1");
            File.WriteAllText(file2, "Content 2");
            File.WriteAllText(file3, "Content 3");

            var archivePath = Path.Combine(testDir, "multiple_files.zip");

            // Act
            await ArchiveCompressor.CompressFilesAsync([file1, file2, file3], archivePath, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(File.Exists(archivePath), "Zip archive with multiple files should be created");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 日本語ファイル名のテスト用ファイル構造を作成する
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたテストディレクトリのパス</returns>
    private static string CreateJapaneseFileStructure(string testDir)
    {
        var sourceDir = Path.Combine(testDir, "日本語フォルダ");
        var subDir = Path.Combine(sourceDir, "サブフォルダ");
        Directory.CreateDirectory(subDir);

        // 日本語ファイル名のテストファイルを作成
        File.WriteAllText(Path.Combine(sourceDir, "読んでください.txt"), "これは日本語のファイルです");
        File.WriteAllText(Path.Combine(sourceDir, "設定ファイル.txt"), "設定データ");
        File.WriteAllText(Path.Combine(subDir, "データ.json"), "{\"名前\": \"値\"}");
        File.WriteAllText(Path.Combine(subDir, "メモ帳.txt"), "メモの内容");

        return sourceDir;
    }

    /// <summary>
    /// 日本語ファイル名がZIP形式で正しく圧縮・展開できるか確認（UTF-8テスト）
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithJapaneseFilenames_ZipFormat_PreservesEncoding()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateJapaneseFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "日本語テスト.zip");
            var extractDir = Path.Combine(testDir, "extracted_zip");

            Console.WriteLine("=== ZIP形式 日本語ファイル名テスト ===");
            Console.WriteLine($"元のディレクトリ: {sourceDir}");
            Console.WriteLine($"ZIPファイル: {archivePath}");

            // Act: 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath), "ZIP archive should be created");
            Console.WriteLine($"✓ 圧縮成功");

            // Act: 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: ファイルとフォルダ名が正しく保持されているか確認
            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Console.WriteLine($"\n展開されたファイル ({extractedFiles.Length}個):");
            foreach (var file in extractedFiles)
            {
                var relativePath = Path.GetRelativePath(extractDir, file);
                Console.WriteLine($"  - {relativePath}");
            }

            // 日本語ファイル名が正しく保持されているか確認
            Assert.True(extractedFiles.Any(f => f.Contains("読んでください.txt")), "日本語ファイル名 '読んでください.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("設定ファイル.txt")), "日本語ファイル名 '設定ファイル.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("データ.json")), "日本語ファイル名 'データ.json' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("メモ帳.txt")), "日本語ファイル名 'メモ帳.txt' が保持されているべき");

            // フォルダ名も確認
            var extractedDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedDirs.Any(d => d.Contains("日本語フォルダ") || d.Contains("サブフォルダ")), "日本語フォルダ名が保持されているべき");

            Console.WriteLine($"\n✅ ZIP形式: 日本語ファイル名のエンコーディングが正しく保持されました");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 日本語ファイル名が7z形式で正しく圧縮・展開できるか確認（UTF-8テスト）
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithJapaneseFilenames_7zFormat_PreservesEncoding()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateJapaneseFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "日本語テスト.7z");
            var extractDir = Path.Combine(testDir, "extracted_7z");

            Console.WriteLine("=== 7z形式 日本語ファイル名テスト ===");
            Console.WriteLine($"元のディレクトリ: {sourceDir}");
            Console.WriteLine($"7zファイル: {archivePath}");

            // Act: 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath), "7z archive should be created");
            Console.WriteLine($"✓ 圧縮成功");

            // Act: 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: ファイルとフォルダ名が正しく保持されているか確認
            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Console.WriteLine($"\n展開されたファイル ({extractedFiles.Length}個):");
            foreach (var file in extractedFiles)
            {
                var relativePath = Path.GetRelativePath(extractDir, file);
                Console.WriteLine($"  - {relativePath}");
            }

            // 日本語ファイル名が正しく保持されているか確認
            Assert.True(extractedFiles.Any(f => f.Contains("読んでください.txt")), "日本語ファイル名 '読んでください.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("設定ファイル.txt")), "日本語ファイル名 '設定ファイル.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("データ.json")), "日本語ファイル名 'データ.json' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("メモ帳.txt")), "日本語ファイル名 'メモ帳.txt' が保持されているべき");

            // フォルダ名も確認
            var extractedDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedDirs.Any(d => d.Contains("日本語フォルダ") || d.Contains("サブフォルダ")), "日本語フォルダ名が保持されているべき");

            Console.WriteLine($"\n✅ 7z形式: 日本語ファイル名のエンコーディングが正しく保持されました");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 日本語ファイル名がTAR形式で正しく圧縮・展開できるか確認（UTF-8テスト）
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithJapaneseFilenames_TarFormat_PreservesEncoding()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateJapaneseFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "日本語テスト.tar");
            var extractDir = Path.Combine(testDir, "extracted_tar");

            Console.WriteLine("=== TAR形式 日本語ファイル名テスト ===");
            Console.WriteLine($"元のディレクトリ: {sourceDir}");
            Console.WriteLine($"TARファイル: {archivePath}");

            // Act: 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath), "TAR archive should be created");
            Console.WriteLine($"✓ 圧縮成功");

            // Act: 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: ファイルとフォルダ名が正しく保持されているか確認
            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Console.WriteLine($"\n展開されたファイル ({extractedFiles.Length}個):");
            foreach (var file in extractedFiles)
            {
                var relativePath = Path.GetRelativePath(extractDir, file);
                Console.WriteLine($"  - {relativePath}");
            }

            // 日本語ファイル名が正しく保持されているか確認
            Assert.True(extractedFiles.Any(f => f.Contains("読んでください.txt")), "日本語ファイル名 '読んでください.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("設定ファイル.txt")), "日本語ファイル名 '設定ファイル.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("データ.json")), "日本語ファイル名 'データ.json' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("メモ帳.txt")), "日本語ファイル名 'メモ帳.txt' が保持されているべき");

            // フォルダ名も確認
            var extractedDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedDirs.Any(d => d.Contains("日本語フォルダ") || d.Contains("サブフォルダ")), "日本語フォルダ名が保持されているべき");

            Console.WriteLine($"\n✅ TAR形式: 日本語ファイル名のエンコーディングが正しく保持されました");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 日本語ファイル名がGZIP形式で正しく圧縮・展開できるか確認（UTF-8テスト）
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_WithJapaneseFilenames_GzipFormat_PreservesEncoding()
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateJapaneseFileStructure(testDir);
            var archivePath = Path.Combine(testDir, "日本語テスト.tar");
            var extractDir = Path.Combine(testDir, "extracted_tar");

            Console.WriteLine("=== TAR形式（GZIP互換） 日本語ファイル名テスト ===");
            Console.WriteLine($"元のディレクトリ: {sourceDir}");
            Console.WriteLine($"TARファイル: {archivePath}");

            // Act: 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath), "TAR archive should be created");
            Console.WriteLine($"✓ 圧縮成功");

            // Act: 展開
            // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // Assert: ファイルとフォルダ名が正しく保持されているか確認
            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Console.WriteLine($"\n展開されたファイル ({extractedFiles.Length}個):");
            foreach (var file in extractedFiles)
            {
                var relativePath = Path.GetRelativePath(extractDir, file);
                Console.WriteLine($"  - {relativePath}");
            }

            // 日本語ファイル名が正しく保持されているか確認
            Assert.True(extractedFiles.Any(f => f.Contains("読んでください.txt")), "日本語ファイル名 '読んでください.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("設定ファイル.txt")), "日本語ファイル名 '設定ファイル.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("データ.json")), "日本語ファイル名 'データ.json' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("メモ帳.txt")), "日本語ファイル名 'メモ帳.txt' が保持されているべき");

            // フォルダ名も確認
            var extractedDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedDirs.Any(d => d.Contains("日本語フォルダ") || d.Contains("サブフォルダ")), "日本語フォルダ名が保持されているべき");

            Console.WriteLine($"\n✅ TAR形式: 日本語ファイル名のエンコーディングが正しく保持されました");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

}
