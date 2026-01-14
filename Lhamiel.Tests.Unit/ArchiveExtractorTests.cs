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
        var projectDir = Path.Combine(testDir, "ProjectA");
        var innerDir = Path.Combine(projectDir, "files");
        var dataDir = Path.Combine(innerDir, "data");

        Directory.CreateDirectory(dataDir);

        // テストファイルを作成
        File.WriteAllText(Path.Combine(projectDir, "readme.txt"), "Project A Readme");
        File.WriteAllText(Path.Combine(innerDir, "config.txt"), "Configuration");
        File.WriteAllText(Path.Combine(dataDir, "data.txt"), "Data content");

        var zipPath = Path.Combine(testDir, "ProjectA.zip");
        ZipFile.CreateFromDirectory(projectDir, zipPath);

        // テスト用ディレクトリを削除（ZIPに含めるためだけ）
        Directory.Delete(projectDir, true);

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
    public void GetOutputDirectory_WithDoubleFolderStructure_PreventsDoubleFolders()
    {
        // Arrange
        var tempDir = CreateTemporaryTestDirectory();
        try
        {
            // 二重フォルダ構造のZIPファイルを作成
            var zipPath = CreateTestZipWithDoubleFolder(tempDir);
            var outputDir = Path.Combine(tempDir, "output");

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert
            // 二重フォルダ防止により、outputDir が直接返されるはず
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

            // Act
            var result = ArchiveExtractor.GetOutputDirectory(zipPath, outputDir);

            // Assert
            // 複数フォルダの場合は、通常通りフォルダを作成
            Assert.Contains("ProjectB", result);
            Assert.StartsWith(outputDir, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
