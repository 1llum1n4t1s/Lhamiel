using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// アーカイブ圧縮・展開の統合テスト
/// </summary>
[Collection("Sequential")]
public class ArchiveCompressionTests
{
    public ArchiveCompressionTests()
    {
        // テスト環境ではユーザー設定に依存しないよう、デフォルト値に明示的にリセット
        SettingsManager.Instance.Current.DirectoryStructureMode = DirectoryStructureMode.IncludeRoot;
    }

    /// <summary>
    /// テスト用の一時ディレクトリを作成する
    /// </summary>
    /// <returns>一時ディレクトリのパス</returns>
    private static string CreateTemporaryTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ArchiveCompressionTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// 複数の入力元（個別ファイル3つ）を圧縮し、展開後に3項目の内容が揃うか確認
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
            await ArchiveCompressor.CompressFilesAsync([file1, file2, file3], archivePath, Format.Zip, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(File.Exists(archivePath), "Zip archive with multiple files should be created");

            var extractDir = Path.Combine(testDir, "extracted");
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);
            var extracted = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.Equal(3, extracted.Length);
            for (var i = 1; i <= 3; i++)
            {
                var path = Assert.Single(extracted, f => Path.GetFileName(f) == $"file{i}.txt");
                Assert.Equal($"Content {i}", File.ReadAllText(path));
            }
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
    /// 日本語ファイル名がZIP・7z・TAR形式で正しく圧縮・展開できるか確認（UTF-8テスト）
    /// </summary>
    [Theory]
    [InlineData(".zip", Format.Zip, "ZIP")]
    [InlineData(".7z", Format.SevenZip, "7z")]
    [InlineData(".tar", Format.Tar, "TAR")]
    public async Task CompressAndExtract_WithJapaneseFilenames_PreservesEncoding(
        string extension, Format format, string formatName)
    {
        // Arrange
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = CreateJapaneseFileStructure(testDir);
            var archivePath = Path.Combine(testDir, $"日本語テスト{extension}");
            var extractDir = Path.Combine(testDir, $"extracted_{extension.TrimStart('.')}");

            Console.WriteLine($"=== {formatName}形式 日本語ファイル名テスト ===");
            Console.WriteLine($"元のディレクトリ: {sourceDir}");
            Console.WriteLine($"{formatName}ファイル: {archivePath}");

            // 拡張子と形式の対応（ParseFormat は未知値を無言で ZIP にするため取り違えを検出する）
            Assert.Equal(format, ArchiveCompressor.ParseFormat(extension.TrimStart('.')));

            // Act: 圧縮
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, format, null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath), $"{formatName} archive should be created");
            Console.WriteLine("✓ 圧縮成功");

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
            var readme = Assert.Single(extractedFiles, f => f.EndsWith("読んでください.txt", StringComparison.Ordinal));
            Assert.Equal("これは日本語のファイルです", File.ReadAllText(readme));
            Assert.True(extractedFiles.Any(f => f.Contains("設定ファイル.txt")), "日本語ファイル名 '設定ファイル.txt' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("データ.json")), "日本語ファイル名 'データ.json' が保持されているべき");
            Assert.True(extractedFiles.Any(f => f.Contains("メモ帳.txt")), "日本語ファイル名 'メモ帳.txt' が保持されているべき");

            // フォルダ名も確認
            var extractedDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories);
            Assert.True(extractedDirs.Any(d => d.Contains("日本語フォルダ") || d.Contains("サブフォルダ")), "日本語フォルダ名が保持されているべき");

            Console.WriteLine($"\n✅ {formatName}形式: 日本語ファイル名のエンコーディングが正しく保持されました");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    // GzipFormat_PreservesEncoding は TarFormat_PreservesEncoding と完全重複のため削除

    /// <summary>
    /// 空ディレクトリを含むディレクトリの圧縮→展開ラウンドトリップで空ディレクトリが保持されるか確認
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    public async Task CompressAndExtract_WithEmptyDirectories_PreservesEmptyDirs(string extension)
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = Path.Combine(testDir, "source");
            // ファイルを含むディレクトリ
            Directory.CreateDirectory(Path.Combine(sourceDir, "HasFiles"));
            File.WriteAllText(Path.Combine(sourceDir, "HasFiles", "test.txt"), "hello");
            // 完全に空のディレクトリ
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyDir"));
            // サブディレクトリのみ持つディレクトリ（全て空）
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyNested", "SubEmpty"));
            // ルートファイル
            File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "world");

            var archivePath = Path.Combine(testDir, $"empty_dirs_test{extension}");
            var extractDir = Path.Combine(testDir, "extracted");

            // 圧縮
            var format = ArchiveCompressor.ParseFormat(extension.TrimStart('.'));
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, format, null, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(archivePath));

            // 展開
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            // 空ディレクトリが展開後に存在するか確認
            var allDirs = Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories)
                .Select(d => Path.GetFileName(d))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("EmptyDir", allDirs);
            Assert.Contains("EmptyNested", allDirs);
            Assert.Contains("SubEmpty", allDirs);
            Assert.Contains("HasFiles", allDirs);

            // ファイルも正しく展開されているか確認
            var allFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            Assert.Contains(allFiles, f => f.EndsWith("test.txt"));
            Assert.Contains(allFiles, f => f.EndsWith("root.txt"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>
    /// 空ディレクトリのタイムスタンプが圧縮→展開ラウンドトリップで保持されるか確認。
    /// 空ディレクトリは共有の空マーカーディレクトリ経由で追加されるため、Add 直前の
    /// タイムスタンプコピー (TryCopyDirectoryTimestamps) が無いと全て圧縮実行時刻になる。
    /// ディレクトリごとに異なる時刻を設定し、マーカー使い回しでも各値が保持されることを確認する。
    /// </summary>
    [Fact]
    public async Task CompressAndExtract_EmptyDirectories_PreserveTimestamps()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            var sourceDir = Path.Combine(testDir, "source");
            var dirA = Path.Combine(sourceDir, "EmptyA");
            var dirB = Path.Combine(sourceDir, "EmptyB");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "x"); // 空ディレクトリ以外も混在させる
            var timeA = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var timeB = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);
            Directory.SetLastWriteTimeUtc(dirA, timeA);
            Directory.SetLastWriteTimeUtc(dirB, timeB);

            var archivePath = Path.Combine(testDir, "empty_dir_times.7z");
            var extractDir = Path.Combine(testDir, "extracted");
            var format = ArchiveCompressor.ParseFormat("7z");
            await ArchiveCompressor.CompressFilesAsync([sourceDir], archivePath, format, null, TestContext.Current.CancellationToken);
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false, TestContext.Current.CancellationToken);

            var outA = Directory.GetDirectories(extractDir, "EmptyA", SearchOption.AllDirectories).Single();
            var outB = Directory.GetDirectories(extractDir, "EmptyB", SearchOption.AllDirectories).Single();
            var actualA = Directory.GetLastWriteTimeUtc(outA);
            var actualB = Directory.GetLastWriteTimeUtc(outB);
            Assert.True(Math.Abs((actualA - timeA).TotalSeconds) < 3,
                $"EmptyA の更新日時が保持されるべき: expected={timeA:O}, actual={actualA:O}");
            Assert.True(Math.Abs((actualB - timeB).TotalSeconds) < 3,
                $"EmptyB の更新日時が保持されるべき: expected={timeB:O}, actual={actualB:O}");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
}
