using System.IO.Compression;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveExtractor class unit tests
/// </summary>
public class ArchiveExtractorTests
{
    /// <summary>
    /// テスト用の一時ディレクトリを作成する
    /// </summary>
    /// <returns>一時ディレクトリのパス</returns>
    private static string CreateTemporaryTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ArchiveExtractorTests_" + Guid.NewGuid().ToString());
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
            System.Console.WriteLine("=== Test: GetOutputDirectory_WithSingleFolderInRoot_CreatesArchiveFolder ===");
            System.Console.WriteLine($"ZIP file: {zipPath}");
            System.Console.WriteLine($"ZIP file name (without extension): {zipFileName}");
            System.Console.WriteLine($"Output directory: {outputDir}");

            using (var reader = new Cube.FileSystem.SevenZip.ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                System.Console.WriteLine($"\nArchive contents ({contents.Count} items):");
                foreach (var item in contents)
                {
                    System.Console.WriteLine($"  - '{item}'");
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert: ケース1 - ルートアイテムが1つ＋フォルダ
            // GetOutputDirectory は アーカイブ名フォルダを返す（リフトアップは展開時に行う）
            var expectedPath = Path.Combine(outputDir, zipFileName);
            System.Console.WriteLine($"\n期待値 (Expected): {expectedPath}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {expectedPath == result}");
            System.Console.WriteLine("=== 仕様での説明 ===");
            System.Console.WriteLine("ケース1: ルートアイテムが1つ＋フォルダ");
            System.Console.WriteLine("GetOutputDirectory は outputDir/ProjectA を返す");
            System.Console.WriteLine("展開時に ProjectA/ の中身がリフトアップされて ProjectA/ が削除される");
            System.Console.WriteLine();

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
            System.Console.WriteLine("=== Test: GetOutputDirectory_WithMultipleFoldersInRoot_CreatesFolder ===");
            System.Console.WriteLine($"ZIP file: {zipPath}");
            System.Console.WriteLine($"ZIP file name (without extension): {zipFileName}");
            System.Console.WriteLine($"Output directory: {outputDir}");

            using (var reader = new Cube.FileSystem.SevenZip.ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                System.Console.WriteLine($"\nArchive contents ({contents.Count} items):");
                foreach (var item in contents)
                {
                    System.Console.WriteLine($"  - '{item}'");
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            var expectedPath = Path.Combine(outputDir, zipFileName);

            // Assert: ケース2 - ルートレベルに複数のアイテムがある場合
            // GetOutputDirectory はアーカイブ名フォルダを返す
            System.Console.WriteLine($"\n期待値 (Expected): {expectedPath}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {expectedPath == result}");
            System.Console.WriteLine("=== 仕様での説明 ===");
            System.Console.WriteLine("ケース2: ルートレベルに複数のアイテムがある場合");
            System.Console.WriteLine("GetOutputDirectory は outputDir/ProjectB を返す");
            System.Console.WriteLine("展開時に folder1, folder2 がそのまま保持される");
            System.Console.WriteLine();

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
            System.Console.WriteLine("=== Test: GetOutputDirectory_WithDeepNestedFolders_CreatesArchiveFolder ===");
            System.Console.WriteLine($"ZIP file: {zipPath}");
            System.Console.WriteLine($"ZIP file name (without extension): {zipFileName}");
            System.Console.WriteLine($"Output directory: {outputDir}");

            using (var reader = new Cube.FileSystem.SevenZip.ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                System.Console.WriteLine($"\nArchive contents ({contents.Count} items):");
                foreach (var item in contents)
                {
                    System.Console.WriteLine($"  - '{item}'");
                }
            }

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert: ケース1 - ルートアイテムが1つ＋フォルダ（ABC フォルダ）
            // GetOutputDirectory はアーカイブ名フォルダを返す（リフトアップは展開時に行う）
            var expectedPath = Path.Combine(outputDir, zipFileName);
            System.Console.WriteLine($"\n期待値 (Expected): {expectedPath}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {expectedPath == result}");
            System.Console.WriteLine("=== 仕様での説明 ===");
            System.Console.WriteLine("ケース1: ルートアイテムが1つ＋フォルダ");
            System.Console.WriteLine("GetOutputDirectory は outputDir/ABC を返す");
            System.Console.WriteLine("展開時に ABC/ の中身がリフトアップされて ABC/ が削除される");
            System.Console.WriteLine();

            Assert.Equal(expectedPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtractArchive_WithSingleRootFolder_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // ルートレベルに1つだけフォルダがあるZIPファイルを作成
            var zipPath = CreateTestZipWithDoubleFolder(tempDir);
            var zipFileName = Path.GetFileNameWithoutExtension(zipPath); // "ProjectA"
            var baseOutputDir = Path.Combine(tempDir, "extract_output");

            System.Console.WriteLine("=== Test: ExtractArchive_WithSingleRootFolder_CreatesArchiveFolder ===");
            System.Console.WriteLine($"ZIP file: {zipPath}");
            System.Console.WriteLine($"Base output directory: {baseOutputDir}");

            using (var reader = new Cube.FileSystem.SevenZip.ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                System.Console.WriteLine($"\nArchive contents ({contents.Count} items):");
                foreach (var item in contents.Take(5))
                {
                    System.Console.WriteLine($"  - '{item}'");
                }
                if (contents.Count > 5)
                {
                    System.Console.WriteLine($"  ... and {contents.Count - 5} more items");
                }
            }

            // Act: GetOutputDirectory を使用して正しい展開先を決定する
            var actualOutputDir = ArchiveExtractor.GetOutputDirectory(zipPath, baseOutputDir);
            System.Console.WriteLine($"\nCalculated output directory: {actualOutputDir}");

            var extractor = new ArchiveExtractor();
            extractor.ExtractArchive(zipPath, actualOutputDir);

            // Assert: 展開結果を確認
            System.Console.WriteLine($"Extracted directory: {actualOutputDir}");
            System.Console.WriteLine($"Directory exists: {Directory.Exists(actualOutputDir)}");

            var topLevelItems = Directory.GetFileSystemEntries(actualOutputDir);
            System.Console.WriteLine($"Top-level items in extracted directory ({topLevelItems.Length}):");
            foreach (var item in topLevelItems.Take(5))
            {
                var name = Path.GetFileName(item);
                var isDir = Directory.Exists(item) ? "(dir)" : "(file)";
                System.Console.WriteLine($"  - {name} {isDir}");
            }

            // ケース1: ルートアイテムが1つ＋フォルダ
            // ProjectA フォルダの中身が actualOutputDir に直接リフトアップされている
            // ProjectA/ フォルダ自体は存在しない（削除される）
            var readmePath = Path.Combine(actualOutputDir, "readme.txt");
            var filesPath = Path.Combine(actualOutputDir, "files");

            Assert.True(File.Exists(readmePath), "readme.txt should be lifted up to extracted directory");
            Assert.True(Directory.Exists(filesPath), "files folder should be lifted up to extracted directory");
            // ProjectA フォルダ自体は存在しないはず
            var projectAPath = Path.Combine(actualOutputDir, "ProjectA");
            Assert.False(Directory.Exists(projectAPath), "ProjectA folder should not exist (lifted up)");

            System.Console.WriteLine("\n✅ Test passed: Archive folder and its contents were extracted and lifted up correctly");
            System.Console.WriteLine();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExtractArchive_WithMultipleRootItems_CreatesArchiveFolder()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // ルートレベルに複数のアイテムがあるZIPファイルを作成
            var zipPath = CreateTestZipWithMultipleFolders(tempDir);
            var baseOutputDir = Path.Combine(tempDir, "extract_output");

            System.Console.WriteLine("=== Test: ExtractArchive_WithMultipleRootItems_CreatesArchiveFolder ===");
            System.Console.WriteLine($"ZIP file: {zipPath}");
            System.Console.WriteLine($"Base output directory: {baseOutputDir}");

            using (var reader = new Cube.FileSystem.SevenZip.ArchiveReader(zipPath))
            {
                var contents = reader.Items.Select(item => item.FullName).ToList();
                System.Console.WriteLine($"\nArchive contents ({contents.Count} items):");
                foreach (var item in contents)
                {
                    System.Console.WriteLine($"  - '{item}'");
                }
            }

            // Act: GetOutputDirectory を使用して正しい展開先を決定する
            var actualOutputDir = ArchiveExtractor.GetOutputDirectory(zipPath, baseOutputDir);
            System.Console.WriteLine($"\nCalculated output directory: {actualOutputDir}");

            var extractor = new ArchiveExtractor();
            extractor.ExtractArchive(zipPath, actualOutputDir);

            // Assert: 展開結果を確認
            System.Console.WriteLine($"\nExtracted directory: {actualOutputDir}");
            System.Console.WriteLine($"Directory exists: {Directory.Exists(actualOutputDir)}");

            var topLevelItems = Directory.GetFileSystemEntries(actualOutputDir);
            System.Console.WriteLine($"Top-level items in extracted directory ({topLevelItems.Length}):");
            foreach (var item in topLevelItems)
            {
                var name = Path.GetFileName(item);
                var isDir = Directory.Exists(item) ? "(dir)" : "(file)";
                System.Console.WriteLine($"  - {name} {isDir}");
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

            System.Console.WriteLine($"\n✅ Test passed: Multiple root items were correctly extracted");
            System.Console.WriteLine();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
