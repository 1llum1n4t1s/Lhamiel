using System.Windows;
using System.IO;
using Lhamiel.Util;

namespace Lhamiel;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// アプリケーション起動時の処理
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);
            
            // 起動ログを出力
            Logger.LogStartup(e.Args);
        
            // コマンドライン引数をチェック
            if (e.Args.Length > 0)
        {
                // ファイルが指定されている場合は展開処理を実行
                ProcessCommandLineFile(e.Args[0]);
            }
            else
            {
                // 引数がない場合はメインウィンドウを表示
                var mainWindow = new View.MainWindow();
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            // ログファイルに直接書き込み（Loggerが使えない場合のため）
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] アプリケーション起動エラー: {ex}\n");
            
            MessageBox.Show($"アプリケーションの起動に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    /// <summary>
    /// コマンドラインで指定されたファイルの展開処理を実行
    /// </summary>
    /// <param name="filePath">展開するファイルのパス</param>
    private async void ProcessCommandLineFile(string filePath)
    {
        try
        {
            Logger.Log($"コマンドラインから展開処理を開始: {filePath}");
            
            // 設定を読み込み
            var settings = Settings.Load();
            var outputDir = settings.ExtractionOutputDirectory;
            var outputToSameDirectory = settings.ExtractionOutputToSameDirectory;
            
            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("展開");
            progressWindow.Show();

            // 共通化された展開処理を実行
            var success = await ArchiveProcessor.ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow);
            
            if (success)
            {
                // アプリケーションを終了
                Shutdown();
            }
            else
            {
                // エラーが発生した場合はアプリケーションを終了
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("コマンドライン展開処理でエラーが発生", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// アプリケーション終了時の処理
    /// </summary>
    /// <param name="e">終了イベント引数</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"アプリケーション終了: 終了コード = {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
