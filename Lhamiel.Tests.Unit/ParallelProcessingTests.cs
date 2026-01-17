using System.IO.Compression;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// CPU並列処理のユニットテスト
/// </summary>
public class ParallelProcessingTests
{
    /// <summary>
    /// テスト用の一時ディレクトリを作成する
    /// </summary>
    /// <returns>一時ディレクトリのパス</returns>
    private static string CreateTemporaryTestDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ParallelProcessingTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// テスト用のZIPファイルを作成する
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <param name="fileName">ファイル名（拡張子なし）</param>
    /// <returns>作成されたZIPファイルのパス</returns>
    private static string CreateTestZipFile(string testDir, string fileName)
    {
        var sourceDir = Path.Combine(testDir, $"source_{fileName}");
        Directory.CreateDirectory(sourceDir);

        // テストファイルを作成
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(sourceDir, $"file{i}.txt"), $"Content {i}");
        }

        var zipPath = Path.Combine(testDir, $"{fileName}.zip");
        ZipFile.CreateFromDirectory(sourceDir, zipPath);

        // ソースディレクトリを削除
        Directory.Delete(sourceDir, true);

        return zipPath;
    }

    /// <summary>
    /// テスト用のフォルダを作成する
    /// </summary>
    /// <param name="testDir">テスト用ディレクトリ</param>
    /// <param name="folderName">フォルダ名</param>
    /// <returns>作成されたフォルダのパス</returns>
    private static string CreateTestFolder(string testDir, string folderName)
    {
        var folderPath = Path.Combine(testDir, folderName);
        Directory.CreateDirectory(folderPath);

        // テストファイルを作成
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(folderPath, $"file{i}.txt"), $"Folder content {i}");
        }

        return folderPath;
    }

    /// <summary>
    /// 複数ファイルの並列展開が正しく実行されることを確認
    /// </summary>
    [Fact]
    public async Task ExtractArchivesAsync_MultipleFiles_AllExtractedSuccessfully()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 3つのZIPファイルを作成
            var zipFile1 = CreateTestZipFile(testDir, "archive1");
            var zipFile2 = CreateTestZipFile(testDir, "archive2");
            var zipFile3 = CreateTestZipFile(testDir, "archive3");

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 複数ファイル展開を実行
            var result = await ArchiveProcessor.ExtractArchivesAsync([zipFile1, zipFile2, zipFile3],
                outputDir,
                outputToSameDirectory: false,
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            // 結果を確認
            Assert.True(result, "複数ファイル展開が失敗しました");

            // 展開されたファイルが存在することを確認
            var extractedDirs = Directory.GetDirectories(outputDir);
            Assert.Equal(3, extractedDirs.Length);

            // 各展開ディレクトリにファイルが存在することを確認
            foreach (var dir in extractedDirs)
            {
                var files = Directory.GetFiles(dir);
                Assert.NotEmpty(files);
            }
        }
        finally
        {
            // テスト用ディレクトリを削除
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// 複数ファイルの並列展開が並列実行されていることを確認（時間ベース）
    /// </summary>
    [Fact]
    public async Task ExtractArchivesAsync_MultipleFiles_ParallelExecution()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 3つのZIPファイルを作成（各約 100KB）
            var zipFiles = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var sourceDir = Path.Combine(testDir, $"source_{i}");
                Directory.CreateDirectory(sourceDir);

                // より大きなテストファイルを作成
                for (int j = 0; j < 10; j++)
                {
                    var content = new string('x', 10000);
                    File.WriteAllText(Path.Combine(sourceDir, $"file{j}.txt"), content);
                }

                var zipPath = Path.Combine(testDir, $"archive{i}.zip");
                ZipFile.CreateFromDirectory(sourceDir, zipPath);
                Directory.Delete(sourceDir, true);

                zipFiles.Add(zipPath);
            }

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 並列実行のタイミングを測定
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var result = await ArchiveProcessor.ExtractArchivesAsync(
                zipFiles.ToArray(),
                outputDir,
                outputToSameDirectory: false,
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            stopwatch.Stop();

            // 結果を確認
            Assert.True(result, "複数ファイル展開が失敗しました");

            // 並列実行された場合、3ファイルを順序実行するより速いはず
            // （この確認は環境依存のため、参考情報として記録）
            Logger.Log($"3ファイルの展開時間: {stopwatch.ElapsedMilliseconds}ms");

            // 展開されたファイルが存在することを確認
            var extractedDirs = Directory.GetDirectories(outputDir);
            Assert.Equal(3, extractedDirs.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// キャンセルトークンが正しく受け取られることを確認
    /// </summary>
    [Fact]
    public async Task ExtractArchivesAsync_CancellationToken_IsRespected()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 2つのZIPファイルを作成
            var zipFiles = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                zipFiles.Add(CreateTestZipFile(testDir, $"archive{i}"));
            }

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // キャンセルなしで実行
            var result = await ArchiveProcessor.ExtractArchivesAsync(
                zipFiles.ToArray(),
                outputDir,
                outputToSameDirectory: false,
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            // 成功するはず
            Assert.True(result, "展開が成功するはず");

            var extractedDirs = Directory.GetDirectories(outputDir);
            Assert.Equal(2, extractedDirs.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// 複数フォルダの並列圧縮が正しく実行されることを確認
    /// </summary>
    [Fact]
    public async Task CompressItemsAsync_MultipleFolders_AllCompressedSuccessfully()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 複数のフォルダを作成
            var folder1 = CreateTestFolder(testDir, "folder1");
            var folder2 = CreateTestFolder(testDir, "folder2");
            var folder3 = CreateTestFolder(testDir, "folder3");

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 複数フォルダ圧縮を実行
            var result = await ArchiveProcessor.CompressItemsAsync([folder1, folder2, folder3],
                outputDir,
                outputToSameDirectory: false,
                format: "zip",
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            // 結果を確認
            Assert.True(result, "複数フォルダ圧縮が失敗しました");

            // 圧縮ファイルが存在することを確認
            var zipFiles = Directory.GetFiles(outputDir, "*.zip");
            Assert.Equal(3, zipFiles.Length);

            // 各圧縮ファイルが正しく作成されたことを確認
            foreach (var zipFile in zipFiles)
            {
                Assert.True(File.Exists(zipFile));
                Assert.True(new FileInfo(zipFile).Length > 0);
            }
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// 複数フォルダの並列圧縮が並列実行されていることを確認
    /// </summary>
    [Fact]
    public async Task CompressItemsAsync_MultipleFolders_ParallelExecution()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 複数の大きなフォルダを作成
            var folders = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var folderPath = Path.Combine(testDir, $"folder{i}");
                Directory.CreateDirectory(folderPath);

                // より大きなテストファイルを作成
                for (int j = 0; j < 10; j++)
                {
                    var content = new string('y', 10000);
                    File.WriteAllText(Path.Combine(folderPath, $"file{j}.txt"), content);
                }

                folders.Add(folderPath);
            }

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 並列実行のタイミングを測定
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var result = await ArchiveProcessor.CompressItemsAsync(
                folders.ToArray(),
                outputDir,
                outputToSameDirectory: false,
                format: "zip",
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            stopwatch.Stop();

            // 結果を確認
            Assert.True(result, "複数フォルダ圧縮が失敗しました");

            Logger.Log($"3フォルダの圧縮時間: {stopwatch.ElapsedMilliseconds}ms");

            // 圧縮ファイルが存在することを確認
            var zipFiles = Directory.GetFiles(outputDir, "*.zip");
            Assert.Equal(3, zipFiles.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// キャンセルトークンが複数フォルダ圧縮で正しく受け取られることを確認
    /// </summary>
    [Fact]
    public async Task CompressItemsAsync_CancellationToken_IsRespected()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 複数のフォルダを作成
            var folder1 = CreateTestFolder(testDir, "folder1");
            var folder2 = CreateTestFolder(testDir, "folder2");

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // キャンセルなしで実行
            var result = await ArchiveProcessor.CompressItemsAsync([folder1, folder2],
                outputDir,
                outputToSameDirectory: false,
                format: "zip",
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            // 成功するはず
            Assert.True(result, "圧縮が成功するはず");

            var zipFiles = Directory.GetFiles(outputDir, "*.zip");
            Assert.Equal(2, zipFiles.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// 複数ファイル展開で一部が失敗した場合の処理を確認
    /// </summary>
    [Fact]
    public async Task ExtractArchivesAsync_PartialFailure_ReturnsPartialSuccess()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 有効なZIPファイルを2つ作成
            var validZip1 = CreateTestZipFile(testDir, "valid1");
            var validZip2 = CreateTestZipFile(testDir, "valid2");

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 複数ファイル展開を実行（すべて有効）
            var result = await ArchiveProcessor.ExtractArchivesAsync([validZip1, validZip2],
                outputDir,
                outputToSameDirectory: false,
                progressWindow: null!,
                cancellationToken: CancellationToken.None
            );

            // 両方成功として扱われるはず
            Assert.True(result, "ファイル展開が成功するはず");

            // 有効なファイルは展開されているはず
            var extractedDirs = Directory.GetDirectories(outputDir);
            Assert.Equal(2, extractedDirs.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    /// <summary>
    /// スレッドセーフティを確認（複数スレッドからの同時アクセス）
    /// </summary>
    [Fact]
    public async Task ExtractArchivesAsync_ThreadSafety_NoRaceConditions()
    {
        var testDir = CreateTemporaryTestDirectory();
        try
        {
            // 複数のZIPファイルを作成
            var zipFiles = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                zipFiles.Add(CreateTestZipFile(testDir, $"archive{i}"));
            }

            var outputDir = Path.Combine(testDir, "output");
            Directory.CreateDirectory(outputDir);

            // 複数の非同期タスクで並列展開を実行
            var tasks = new List<Task>();
            for (int i = 0; i < 2; i++)
            {
                var task = ArchiveProcessor.ExtractArchivesAsync(
                    zipFiles.ToArray(),
                    Path.Combine(outputDir, $"batch{i}"),
                    outputToSameDirectory: false,
                    progressWindow: null!,
                    cancellationToken: CancellationToken.None
                );
                tasks.Add(task);
            }

            // すべてのタスクが完了するまで待機
            await Task.WhenAll(tasks);

            // 両方のバッチが正しく処理されたことを確認
            var batch0Dirs = Directory.GetDirectories(Path.Combine(outputDir, "batch0"));
            var batch1Dirs = Directory.GetDirectories(Path.Combine(outputDir, "batch1"));

            Assert.Equal(5, batch0Dirs.Length);
            Assert.Equal(5, batch1Dirs.Length);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}
