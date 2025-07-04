using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;
using System.IO;

namespace GGEZArchiver.Util
{
    public class ArchiveExtractor
    {
        public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<int>? progress = null)
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            switch (extension)
            {
                case ".zip":
                    await ExtractZipAsync(archivePath, outputPath, progress);
                    break;
                case ".7z":
                    await Extract7zAsync(archivePath, outputPath, progress);
                    break;
                case ".lzh":
                    await ExtractLzhAsync(archivePath, outputPath, progress);
                    break;
                case ".cab":
                    await ExtractCabAsync(archivePath, outputPath, progress);
                    break;
                default:
                    throw new NotSupportedException($"サポートされていないファイル形式です: {extension}");
            }
        }

        private static async Task ExtractZipAsync(string archivePath, string outputPath, IProgress<int>? progress)
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entries = archive.Entries.ToList();
                var totalEntries = entries.Count;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entryPath = Path.Combine(outputPath, entry.FullName);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(entryPath, true);
                    }

                    progress?.Report((i + 1) * 100 / totalEntries);
                }
            });
        }

        private static async Task Extract7zAsync(string archivePath, string outputPath, IProgress<int>? progress)
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Open(archivePath);
                var entries = archive.Entries.ToList();
                var totalEntries = entries.Count;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entryPath = Path.Combine(outputPath, entry.Key);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    if (!entry.IsDirectory)
                    {
                        entry.WriteToFile(entryPath);
                    }

                    progress?.Report((i + 1) * 100 / totalEntries);
                }
            });
        }

        private static async Task ExtractLzhAsync(string archivePath, string outputPath, IProgress<int>? progress)
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Open(archivePath);
                var entries = archive.Entries.ToList();
                var totalEntries = entries.Count;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entryPath = Path.Combine(outputPath, entry.Key);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    if (!entry.IsDirectory)
                    {
                        entry.WriteToFile(entryPath);
                    }

                    progress?.Report((i + 1) * 100 / totalEntries);
                }
            });
        }

        private static async Task ExtractCabAsync(string archivePath, string outputPath, IProgress<int>? progress)
        {
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Open(archivePath);
                var entries = archive.Entries.ToList();
                var totalEntries = entries.Count;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entryPath = Path.Combine(outputPath, entry.Key);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    if (!entry.IsDirectory)
                    {
                        entry.WriteToFile(entryPath);
                    }

                    progress?.Report((i + 1) * 100 / totalEntries);
                }
            });
        }

        public static string GetOutputDirectory(string archivePath, string baseOutputPath)
        {
            var archiveName = Path.GetFileNameWithoutExtension(archivePath);
            var outputDir = Path.Combine(baseOutputPath, archiveName);

            // 二重フォルダを防ぐための処理
            if (Directory.Exists(outputDir))
            {
                var files = Directory.GetFiles(outputDir);
                var subDirs = Directory.GetDirectories(outputDir);

                // 出力先にファイルやフォルダが1つしかない場合、そのフォルダ名がアーカイブ名と同じかチェック
                if (files.Length == 0 && subDirs.Length == 1)
                {
                    var subDirName = Path.GetFileName(subDirs[0]);
                    if (subDirName == archiveName)
                    {
                        // 既に二重フォルダ構造になっている場合は、そのサブフォルダを使用
                        return subDirs[0];
                    }
                }
            }

            return outputDir;
        }
    }
} 