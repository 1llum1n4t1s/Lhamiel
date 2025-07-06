using System.Windows;
using System.IO;
using System.Threading.Tasks;
using GGEZArchiver.Util;
using SevenZip;

namespace GGEZArchiver
{
    /// <summary>
    /// アプリケーションのメインクラス
    /// アプリケーションの起動処理とコマンドライン引数の処理を担当
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// アプリケーション起動時の処理
        /// SevenZipSharpのDLL初期化とコマンドライン引数の処理を行う
        /// </summary>
        /// <param name="e">起動イベントの引数</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            // SevenZipSharpのDLLパス初期化
            InitializeSevenZipLibrary();

            base.OnStartup(e);

            // コマンドライン引数をチェック
            ProcessCommandLineArguments(e.Args);

            // 設定画面を表示
            ShowMainWindow();
        }

        /// <summary>
        /// SevenZipSharpライブラリのDLLパスを初期化する
        /// 7z.Libsパッケージで配置されたDLLを自動検出して設定する
        /// </summary>
        private void InitializeSevenZipLibrary()
        {
            var exeDir = System.AppDomain.CurrentDomain.BaseDirectory;
            var sevenZipDllPath = Path.Combine(exeDir, "7z.dll");
            if (File.Exists(sevenZipDllPath))
            {
                SevenZipBase.SetLibraryPath(sevenZipDllPath);
            }
            else
            {
                // 7z.LibsのDLL配置先を探索（x64/x86対応）
                var x64Path = Path.Combine(exeDir, "x64", "7z.dll");
                var x86Path = Path.Combine(exeDir, "x86", "7z.dll");
                if (File.Exists(x64Path))
                    SevenZipBase.SetLibraryPath(x64Path);
                else if (File.Exists(x86Path))
                    SevenZipBase.SetLibraryPath(x86Path);
            }
        }

        /// <summary>
        /// コマンドライン引数を処理する
        /// ファイルまたはフォルダが指定された場合、自動的に展開・圧縮処理を実行する
        /// 対応圧縮ファイル形式以外のファイルは圧縮処理を行う
        /// </summary>
        /// <param name="args">コマンドライン引数</param>
        private void ProcessCommandLineArguments(string[] args)
        {
            if (args.Length > 0)
            {
                var filePath = args[0];
                if (File.Exists(filePath))
                {
                    if (ArchiveExtractor.IsSupportedArchiveType(filePath))
                    {
                        // 対応圧縮ファイル形式の場合は展開処理
                        ExtractFile(filePath);
                    }
                    else
                    {
                        // 対応圧縮ファイル形式以外のファイルは圧縮処理
                        CompressItem(filePath);
                    }
                    Shutdown();
                    return;
                }
                else if (Directory.Exists(filePath))
                {
                    // フォルダが存在する場合、圧縮処理を開始
                    CompressItem(filePath);
                    Shutdown();
                    return;
                }
            }
        }

        /// <summary>
        /// メインウィンドウを表示する
        /// </summary>
        private void ShowMainWindow()
        {
            var mainWindow = new View.MainWindow();
            mainWindow.Show();
        }

        /// <summary>
        /// 指定されたファイルを展開する
        /// 保存された設定に基づいて出力先を決定し、展開処理を実行する
        /// </summary>
        /// <param name="filePath">展開するファイルのパス</param>
        private async void ExtractFile(string filePath)
        {
            try
            {
                var settings = Settings.Load();
                var progressWindow = new View.ProgressWindow("展開");
                progressWindow.SetFileName(filePath);
                progressWindow.Show();

                var outputDir = ArchiveExtractor.GetOutputDirectory(filePath, settings.ExtractionOutputDirectory, settings.ExtractionOutputToSameDirectory);
                
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

        /// <summary>
        /// 指定されたフォルダを圧縮する
        /// 保存された設定に基づいて圧縮形式を決定し、圧縮処理を実行する
        /// </summary>
        /// <param name="sourcePath">圧縮するフォルダのパス</param>
        private async void CompressItem(string sourcePath)
        {
            try
            {
                // 保存された設定から圧縮形式と出力ディレクトリを取得
                var settings = Settings.Load();
                var format = settings.CompressionFormat;
                var outputPath = ArchiveCompressor.GetCompressedFileName(sourcePath, format, settings.CompressionOutputDirectory, settings.CompressionOutputToSameDirectory);

                var progressWindow = new View.ProgressWindow("圧縮");
                progressWindow.SetFileName(outputPath);
                progressWindow.Show();

                var progress = new Progress<int>(percentage =>
                {
                    progressWindow.UpdateProgress(percentage, "ファイルを圧縮中...");
                });

                // 圧縮形式に応じて適切な圧縮メソッドを呼び出す
                await CompressWithFormat(sourcePath, outputPath, format, progress);

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

        /// <summary>
        /// 指定された圧縮形式でファイルを圧縮する
        /// </summary>
        /// <param name="sourcePath">圧縮するファイルのパス</param>
        /// <param name="outputPath">出力ファイルのパス</param>
        /// <param name="format">圧縮形式</param>
        /// <param name="progress">進行状況を報告するオブジェクト</param>
        private async Task CompressWithFormat(string sourcePath, string outputPath, string format, IProgress<int> progress)
        {
            await ArchiveCompressor.CompressAsync(sourcePath, outputPath, format, progress);
        }
    }
}
