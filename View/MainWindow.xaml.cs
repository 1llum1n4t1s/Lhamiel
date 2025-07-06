using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using GGEZArchiver.Util;
using System.Threading.Tasks;

namespace GGEZArchiver.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 現在の設定オブジェクト
        /// アプリケーション全体で使用される設定を管理
        /// </summary>
        private Settings _settings;

        /// <summary>
        /// メインウィンドウのコンストラクタ
        /// 設定の読み込みとUIの初期化を行う
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        /// <summary>
        /// 設定を読み込んでUIに反映する
        /// 保存された設定ファイルから設定を読み込み、各コントロールに設定値を適用
        /// ファイル関連付けは実際のWindowsの状態から読み込む
        /// </summary>
        private void LoadSettings()
        {
            _settings = Settings.Load();
            ExtractionOutputPathTextBox.Text = _settings.ExtractionOutputDirectory;
            CompressionOutputPathTextBox.Text = _settings.CompressionOutputDirectory;
            CompressionFormatComboBox.SelectedItem = _settings.CompressionFormat;
            
            // 出力先パターンの設定を反映
            ExtractionOutputToDirectoryRadio.IsChecked = !_settings.ExtractionOutputToSameDirectory;
            ExtractionOutputToSameDirectoryRadio.IsChecked = _settings.ExtractionOutputToSameDirectory;
            CompressionOutputToDirectoryRadio.IsChecked = !_settings.CompressionOutputToSameDirectory;
            CompressionOutputToSameDirectoryRadio.IsChecked = _settings.CompressionOutputToSameDirectory;
            
            // ファイル関連付けの状態を実際のWindowsの状態から読み込む
            var associationStatus = FileAssociation.GetCurrentAssociationStatus();
            
            // 各チェックボックスの状態を実際の関連付け状態に設定
            ZipCheckBox.IsChecked = associationStatus.GetValueOrDefault(".zip", false);
            SevenZipCheckBox.IsChecked = associationStatus.GetValueOrDefault(".7z", false);
            TarCheckBox.IsChecked = associationStatus.GetValueOrDefault(".tar", false);
            GzCheckBox.IsChecked = associationStatus.GetValueOrDefault(".gz", false);
            Bz2CheckBox.IsChecked = associationStatus.GetValueOrDefault(".bz2", false);
            LzmaCheckBox.IsChecked = associationStatus.GetValueOrDefault(".lzma", false);
            XzCheckBox.IsChecked = associationStatus.GetValueOrDefault(".xz", false);
            RarCheckBox.IsChecked = associationStatus.GetValueOrDefault(".rar", false);
            LzhCheckBox.IsChecked = associationStatus.GetValueOrDefault(".lzh", false);
            CabCheckBox.IsChecked = associationStatus.GetValueOrDefault(".cab", false);
            ArjCheckBox.IsChecked = associationStatus.GetValueOrDefault(".arj", false);
            ZCheckBox.IsChecked = associationStatus.GetValueOrDefault(".z", false);
        }

        /// <summary>
        /// 展開用出力ディレクトリ選択ボタンのクリックイベントハンドラー
        /// フォルダ選択ダイアログを表示して展開用出力ディレクトリを変更する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void ExtractionBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "展開用出力ディレクトリを選択してください",
                SelectedPath = _settings.ExtractionOutputDirectory
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.ExtractionOutputDirectory = dialog.SelectedPath;
                ExtractionOutputPathTextBox.Text = _settings.ExtractionOutputDirectory;
                SaveSettings();
            }
        }

        /// <summary>
        /// 圧縮用出力ディレクトリ選択ボタンのクリックイベントハンドラー
        /// フォルダ選択ダイアログを表示して圧縮用出力ディレクトリを変更する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void CompressionBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "圧縮用出力ディレクトリを選択してください",
                SelectedPath = _settings.CompressionOutputDirectory
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.CompressionOutputDirectory = dialog.SelectedPath;
                CompressionOutputPathTextBox.Text = _settings.CompressionOutputDirectory;
                SaveSettings();
            }
        }

        /// <summary>
        /// ファイル関連付けチェックボックスの変更イベントハンドラー
        /// チェックボックスの状態に応じて実際のファイル関連付けを設定/解除する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void FileAssociation_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                var isChecked = checkBox.IsChecked == true;
                string extension = "";
                
                // チェックボックス名から拡張子を特定
                if (checkBox == ZipCheckBox) extension = ".zip";
                else if (checkBox == SevenZipCheckBox) extension = ".7z";
                else if (checkBox == TarCheckBox) extension = ".tar";
                else if (checkBox == GzCheckBox) extension = ".gz";
                else if (checkBox == Bz2CheckBox) extension = ".bz2";
                else if (checkBox == LzmaCheckBox) extension = ".lzma";
                else if (checkBox == XzCheckBox) extension = ".xz";
                else if (checkBox == RarCheckBox) extension = ".rar";
                else if (checkBox == LzhCheckBox) extension = ".lzh";
                else if (checkBox == CabCheckBox) extension = ".cab";
                else if (checkBox == ArjCheckBox) extension = ".arj";
                else if (checkBox == ZCheckBox) extension = ".z";
                
                if (!string.IsNullOrEmpty(extension))
                {
                    // 実際のファイル関連付けを設定/解除
                    if (isChecked)
                    {
                        if (FileAssociation.AssociateFileType(extension) == false)
                        {
                            System.Windows.MessageBox.Show(
                                "ファイル関連付けの設定に失敗しました。", 
                                "エラー", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        FileAssociation.DisassociateFileType(extension);
                    }
                }
            }
        }

        /// <summary>
        /// 圧縮形式選択コンボボックスの選択変更イベントハンドラー
        /// 選択された圧縮形式を設定に保存する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CompressionFormatComboBox.SelectedItem is string selectedFormat)
            {
                _settings.CompressionFormat = selectedFormat;
                SaveSettings();
            }
        }

        /// <summary>
        /// 展開用出力先パターン選択の変更イベントハンドラー
        /// 選択された出力先パターンを設定に保存する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void ExtractionOutputPattern_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radioButton && _settings != null)
            {
                _settings.ExtractionOutputToSameDirectory = radioButton == ExtractionOutputToSameDirectoryRadio;
                SaveSettings();
            }
        }

        /// <summary>
        /// 圧縮用出力先パターン選択の変更イベントハンドラー
        /// 選択された出力先パターンを設定に保存する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void CompressionOutputPattern_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radioButton && _settings != null)
            {
                _settings.CompressionOutputToSameDirectory = radioButton == CompressionOutputToSameDirectoryRadio;
                SaveSettings();
            }
        }

        /// <summary>
        /// ショートカット作成ボタンのクリックイベントハンドラー
        /// デスクトップにショートカットを作成する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            if (ShortcutCreator.CreateDesktopShortcut())
            {
                System.Windows.MessageBox.Show("デスクトップにショートカットを作成しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("ショートカットの作成に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定を保存ボタンのクリックイベントハンドラー
        /// 現在の設定をファイルに保存し、ファイル関連付けとショートカット作成を実行する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// キャンセルボタンのクリックイベントハンドラー
        /// アプリケーションを終了する
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">イベント引数</param>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// ドロップゾーンのドラッグエンターイベントハンドラー
        /// ドラッグされたアイテムがドロップ可能かどうかを判定し、視覚的フィードバックを提供
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">ドラッグイベント引数</param>
        private void DropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                DropZone.Background = new SolidColorBrush(Colors.LightBlue);
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// ドロップゾーンのドラッグリーブイベントハンドラー
        /// ドラッグが終了した際の視覚的フィードバックを元に戻す
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">ドラッグイベント引数</param>
        private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            DropZone.Background = new SolidColorBrush(Colors.LightGray);
            e.Handled = true;
        }

        /// <summary>
        /// ドロップゾーンのドロップイベントハンドラー
        /// ドロップされたファイルまたはフォルダに対して圧縮・展開処理を実行
        /// </summary>
        /// <param name="sender">イベントの送信元オブジェクト</param>
        /// <param name="e">ドラッグイベント引数</param>
        private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
        {
            DropZone.Background = new SolidColorBrush(Colors.LightGray);
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    var filePath = files[0];
                    ProcessDroppedItem(filePath);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// ドロップされたアイテムを処理する
        /// ファイルまたはフォルダの種類に応じて適切な処理（圧縮・展開）を実行
        /// 対応圧縮ファイル形式以外のファイルは圧縮処理を行う
        /// </summary>
        /// <param name="filePath">ドロップされたファイルまたはフォルダのパス</param>
        private async void ProcessDroppedItem(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    if (ArchiveExtractor.IsSupportedArchiveType(filePath))
                    {
                        // 対応圧縮ファイル形式の場合は展開処理
                        await ExtractFile(filePath);
                    }
                    else
                    {
                        // 対応圧縮ファイル形式以外のファイルは圧縮処理
                        await CompressFile(filePath);
                    }
                }
                else if (Directory.Exists(filePath))
                {
                    // フォルダの場合は圧縮処理
                    await CompressFolder(filePath);
                }
                else
                {
                    System.Windows.MessageBox.Show("ファイルまたはフォルダが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"処理中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ファイルを展開する
        /// 指定されたアーカイブファイルを展開処理ウィンドウで実行
        /// </summary>
        /// <param name="filePath">展開するファイルのパス</param>
        /// <returns>展開処理の完了を表すTask</returns>
        private async Task ExtractFile(string filePath)
        {
            if (_settings == null)
            {
                System.Windows.MessageBox.Show("設定が読み込まれていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var outputDir = ArchiveExtractor.GetOutputDirectory(filePath, _settings.ExtractionOutputDirectory, _settings.ExtractionOutputToSameDirectory);
            var progressWindow = new ProgressWindow("展開");
            progressWindow.SetFileName(filePath);
            progressWindow.Show();
            var progress = new System.Progress<int>(percentage =>
            {
                progressWindow.UpdateProgress(percentage, "ファイルを展開中...");
            });
            await ArchiveExtractor.ExtractArchiveAsync(filePath, outputDir, progress);
            progressWindow.SetCompleted($"展開が完了しました。\n出力先: {outputDir}");
        }

        /// <summary>
        /// フォルダを圧縮する
        /// 指定されたフォルダを圧縮処理ウィンドウで実行
        /// </summary>
        /// <param name="folderPath">圧縮するフォルダのパス</param>
        /// <returns>圧縮処理の完了を表すTask</returns>
        private async Task CompressFolder(string folderPath)
        {
            if (_settings == null)
            {
                System.Windows.MessageBox.Show("設定が読み込まれていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var fileName = ArchiveCompressor.GetCompressedFileName(folderPath, _settings.CompressionFormat, _settings.CompressionOutputDirectory, _settings.CompressionOutputToSameDirectory);
            var progressWindow = new ProgressWindow("圧縮");
            progressWindow.SetFileName(fileName);
            progressWindow.Show();
            var progress = new System.Progress<int>(percentage =>
            {
                progressWindow.UpdateProgress(percentage, "フォルダを圧縮中...");
            });
            await CompressWithFormat(folderPath, fileName, _settings.CompressionFormat, progress);
            progressWindow.SetCompleted($"圧縮が完了しました。\n出力先: {fileName}");
        }

        /// <summary>
        /// ファイルを圧縮する
        /// 指定されたファイルを圧縮処理ウィンドウで実行
        /// </summary>
        /// <param name="filePath">圧縮するファイルのパス</param>
        /// <returns>圧縮処理の完了を表すTask</returns>
        private async Task CompressFile(string filePath)
        {
            if (_settings == null)
            {
                System.Windows.MessageBox.Show("設定が読み込まれていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var fileName = ArchiveCompressor.GetCompressedFileName(filePath, _settings.CompressionFormat, _settings.CompressionOutputDirectory, _settings.CompressionOutputToSameDirectory);
            var progressWindow = new ProgressWindow("圧縮");
            progressWindow.SetFileName(fileName);
            progressWindow.Show();
            var progress = new System.Progress<int>(percentage =>
            {
                progressWindow.UpdateProgress(percentage, "ファイルを圧縮中...");
            });
            await CompressWithFormat(filePath, fileName, _settings.CompressionFormat, progress);
            progressWindow.SetCompleted($"圧縮が完了しました。\n出力先: {fileName}");
        }

        /// <summary>
        /// 指定された圧縮形式でフォルダを圧縮する
        /// </summary>
        /// <param name="sourcePath">圧縮するフォルダのパス</param>
        /// <param name="outputPath">出力ファイルのパス</param>
        /// <param name="format">圧縮形式</param>
        /// <param name="progress">進行状況を報告するオブジェクト</param>
        /// <returns>圧縮処理の完了を表すTask</returns>
        private async Task CompressWithFormat(string sourcePath, string outputPath, string format, System.IProgress<int> progress)
        {
            await ArchiveCompressor.CompressAsync(sourcePath, outputPath, format, progress);
        }

        /// <summary>
        /// 設定を保存する
        /// 現在の設定オブジェクトをJSON形式でファイルに書き込む
        /// </summary>
        private void SaveSettings()
        {
            if (_settings != null)
            {
                _settings.Save();
            }
        }
    }
}