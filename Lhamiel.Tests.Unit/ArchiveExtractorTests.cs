using System;
using System.Collections.Generic;
using System.IO;
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
    /// テスト用のZIPファイルを作成する（複数フォルダあり）
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipWithMultipleFolders(string testDir)
    {
        // テスト用の構造：ProjectB.zip 内に複数のフォルダがあるケース
        var folder1 = Path.Combine(testDir, "ProjectB", "folder1");
        var folder2 = Path.Combine(testDir, "ProjectB", "folder2");

        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        File.WriteAllText(Path.Combine(folder1, "file1.txt"), "File 1");
        File.WriteAllText(Path.Combine(folder2, "file2.txt"), "File 2");

        var zipPath = Path.Combine(testDir, "ProjectB.zip");
        ZipFile.CreateFromDirectory(Path.Combine(testDir, "ProjectB"), zipPath);

        // テスト用ディレクトリを削除
        Directory.Delete(Path.Combine(testDir, "ProjectB"), true);

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
    public void GetOutputDirectory_WithSingleFolderInRoot_PreventsDoubleFolders()
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
            System.Console.WriteLine("=== Test: GetOutputDirectory_WithSingleFolderInRoot_PreventsDoubleFolders ===");
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

            // Assert: ルートレベルに1つだけアイテムがある場合、二重フォルダ防止が必要
            System.Console.WriteLine($"\n期待値 (Expected): {outputDir}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {outputDir == result}");
            System.Console.WriteLine("=== 新仕様での説明 ===");
            System.Console.WriteLine("ルートレベルに 'ProjectA' フォルダが1つだけある場合、");
            System.Console.WriteLine("二重フォルダ防止により outputDir が直接返される");
            System.Console.WriteLine("（展開時は ProjectA の中身が outputDir に配置される）");
            System.Console.WriteLine();

            Assert.Equal(outputDir, result);
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

            // Assert: ルートレベルに2つ以上のアイテムがある場合、アーカイブ名のフォルダを作成
            System.Console.WriteLine($"\n期待値 (Expected): {expectedPath}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {expectedPath == result}");
            System.Console.WriteLine("=== 新仕様での説明 ===");
            System.Console.WriteLine("ルートレベルに複数のアイテムがある場合、");
            System.Console.WriteLine("アーカイブ名のフォルダ（ProjectB）を作成する");
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
    public void GetOutputDirectory_WithDeepNestedFolders_PreventsDoubleFolders()
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
            System.Console.WriteLine("=== Test: GetOutputDirectory_WithDeepNestedFolders_PreventsDoubleFolders ===");
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

            // Assert: ルートレベルに1つだけアイテムがある場合
            System.Console.WriteLine($"\n期待値 (Expected): {outputDir}");
            System.Console.WriteLine($"結果 (Actual):   {result}");
            System.Console.WriteLine($"一致: {outputDir == result}");
            System.Console.WriteLine("=== 新仕様での説明 ===");
            System.Console.WriteLine("ルートレベルに 'ABC' フォルダが1つだけある場合、");
            System.Console.WriteLine("二重フォルダ防止により outputDir が直接返される");
            System.Console.WriteLine("（展開時は ABC の中身（ABC/ABC/ABC/...）が outputDir に配置される）");
            System.Console.WriteLine();

            Assert.Equal(outputDir, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
