using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.IO;
using GGEZArchiver.Util;

namespace GGEZArchiver.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Settings settings;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            UpdateAssociationStatus();
        }

        private void LoadSettings()
        {
            settings = Settings.Load();
            OutputPathTextBox.Text = settings.OutputDirectory;
            
            // 現在の関連付け状況をWindowsから読み込んで反映
            ZipCheckBox.IsChecked = FileAssociation.IsAssociated("zip");
            SevenZipCheckBox.IsChecked = FileAssociation.IsAssociated("7z");
            LzhCheckBox.IsChecked = FileAssociation.IsAssociated("lzh");
            CabCheckBox.IsChecked = FileAssociation.IsAssociated("cab");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "出力先フォルダを選択してください",
                SelectedPath = OutputPathTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void FileAssociation_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAssociationStatus();
        }

        private void UpdateAssociationStatus()
        {
            // 現在の関連付け状況をWindowsから読み込んで反映
            ZipCheckBox.IsChecked = FileAssociation.IsAssociated("zip");
            SevenZipCheckBox.IsChecked = FileAssociation.IsAssociated("7z");
            LzhCheckBox.IsChecked = FileAssociation.IsAssociated("lzh");
            CabCheckBox.IsChecked = FileAssociation.IsAssociated("cab");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 設定を保存
                settings.OutputDirectory = OutputPathTextBox.Text;
                settings.AssociateZip = ZipCheckBox.IsChecked == true;
                settings.Associate7z = SevenZipCheckBox.IsChecked == true;
                settings.AssociateLzh = LzhCheckBox.IsChecked == true;
                settings.AssociateCab = CabCheckBox.IsChecked == true;

                settings.Save();

                // ファイル関連付けを更新
                UpdateFileAssociations();

                System.Windows.MessageBox.Show("設定を保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateAssociationStatus();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"設定の保存中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateFileAssociations()
        {
            var extensions = new[] { "zip", "7z", "lzh", "cab" };
            var settingsArray = new[] { settings.AssociateZip, settings.Associate7z, settings.AssociateLzh, settings.AssociateCab };

            for (int i = 0; i < extensions.Length; i++)
            {
                if (settingsArray[i])
                {
                    FileAssociation.AssociateFileType(extensions[i], $"{extensions[i].ToUpper()} アーカイブ");
                }
                else
                {
                    FileAssociation.RemoveFileAssociation(extensions[i]);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appPath = ShortcutCreator.GetApplicationPath();
                var desktopPath = ShortcutCreator.GetDesktopPath();
                
                // 選択された圧縮形式を取得
                var selectedFormat = CompressionFormatComboBox.SelectedIndex == 0 ? "zip" : "7z";
                var shortcutPath = System.IO.Path.Combine(desktopPath, $"GGEZArchiver ({selectedFormat.ToUpper()}).lnk");
                var arguments = $"--format={selectedFormat}";

                if (ShortcutCreator.CreateShortcut(appPath, shortcutPath, $"GGEZArchiver - 圧縮ファイル展開ツール ({selectedFormat.ToUpper()})", arguments))
                {
                    System.Windows.MessageBox.Show($"デスクトップに{selectedFormat.ToUpper()}形式のショートカットを作成しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("ショートカットの作成に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ショートカット作成中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}