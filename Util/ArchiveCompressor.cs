using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;
using System.IO;

namespace GGEZArchiver.Util
{
    public class ArchiveCompressor
    {
        public static async Task CompressToZipAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(sourcePath))
                {
                    // フォルダの場合
                    ZipFile.CreateFromDirectory(sourcePath, outputPath);
                    progress?.Report(100);
                }
                else if (File.Exists(sourcePath))
                {
                    // ファイルの場合
                    using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                    var entry = archive.CreateEntry(Path.GetFileName(sourcePath));
                    using var sourceStream = File.OpenRead(sourcePath);
                    using var entryStream = entry.Open();
                    sourceStream.CopyTo(entryStream);
                    progress?.Report(100);
                }
            });
        }

        public static async Task CompressTo7zAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Create(ArchiveType.SevenZip);
                
                if (Directory.Exists(sourcePath))
                {
                    // フォルダの場合
                    AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
                }
                else if (File.Exists(sourcePath))
                {
                    // ファイルの場合
                    archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
                }

                archive.SaveTo(outputPath, null);
                progress?.Report(100);
            });
        }

        private static void AddDirectoryToArchive(IWritableArchive archive, string directoryPath, string basePath)
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            foreach (var file in files)
            {
                var relativePath = file.Substring(basePath.Length).TrimStart('\\', '/');
                archive.AddEntry(relativePath, file);
                processedFiles++;
                
                // 進行状況を報告（簡易版）
                if (processedFiles % 10 == 0 || processedFiles == totalFiles)
                {
                    var percentage = (int)((double)processedFiles / totalFiles * 100);
                    // 進行状況の報告は実装が複雑なため、ここでは省略
                }
            }
        }

        public static string GetCompressedFileName(string sourcePath, string extension)
        {
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var directory = Path.GetDirectoryName(sourcePath);
            
            // 同名ファイルが存在する場合は番号を付ける
            var counter = 1;
            var outputPath = Path.Combine(directory ?? "", $"{fileName}.{extension}");
            
            while (File.Exists(outputPath))
            {
                outputPath = Path.Combine(directory ?? "", $"{fileName}_{counter}.{extension}");
                counter++;
            }
            
            return outputPath;
        }

        public static bool IsSupportedCompressionType(string extension)
        {
            var supportedTypes = new[] { "zip", "7z" };
            return supportedTypes.Contains(extension.ToLowerInvariant());
        }
    }
} 