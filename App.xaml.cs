using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System.Threading.Tasks;
using GGEZArchiver.Util;

namespace GGEZArchiver
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // コマンドライン引数をチェック
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (File.Exists(filePath))
                {
                    // ファイルが存在する場合、展開処理を開始
                    ExtractFile(filePath);
                    Shutdown();
                    return;
                }
                else if (Directory.Exists(filePath))
                {
                    // フォルダが存在する場合、圧縮処理を開始
                    CompressFile(filePath);
                    Shutdown();
                    return;
                }
            }

            // 設定画面を表示
            var mainWindow = new View.MainWindow();
            mainWindow.Show();
        }

        private async void ExtractFile(string filePath)
        {
            try
            {
                var settings = Settings.Load();
                var progressWindow = new View.ProgressWindow();
                progressWindow.SetFileName(filePath);
                progressWindow.Show();

                var outputDir = ArchiveExtractor.GetOutputDirectory(filePath, settings.OutputDirectory);
                
                var progress = new Progress<int>(percentage =>
                {
                    progressWindow.UpdateProgress(percentage, "ファイルを展開中...");
                });

                await ArchiveExtractor.ExtractArchiveAsync(filePath, outputDir, progress);

                progressWindow.SetCompleted($"展開が完了しました。\n出力先: {outputDir}");
                
                // 3秒後にウィンドウを閉じる
                await Task.Delay(3000);
                progressWindow.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CompressFile(string sourcePath)
        {
            try
            {
                // コマンドライン引数から圧縮形式を取得（デフォルトはzip）
                var format = "zip";
                var args = Environment.GetCommandLineArgs();
                foreach (var arg in args)
                {
                    if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = arg.Substring("--format=".Length).ToLower();
                        if (value == "zip" || value == "7z")
                        {
                            format = value;
                        }
                    }
                }

                var outputPath = ArchiveCompressor.GetCompressedFileName(sourcePath, format);

                var progressWindow = new View.CompressionProgressWindow();
                progressWindow.SetFileName(outputPath);
                progressWindow.Show();

                var progress = new Progress<int>(percentage =>
                {
                    progressWindow.UpdateProgress(percentage, "ファイルを圧縮中...");
                });

                if (format == "zip")
                {
                    await ArchiveCompressor.CompressToZipAsync(sourcePath, outputPath, progress);
                }
                else if (format == "7z")
                {
                    await ArchiveCompressor.CompressTo7zAsync(sourcePath, outputPath, progress);
                }

                progressWindow.SetCompleted($"圧縮が完了しました。\n出力先: {outputPath}");
                
                // 3秒後にウィンドウを閉じる
                await Task.Delay(3000);
                progressWindow.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"圧縮中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
