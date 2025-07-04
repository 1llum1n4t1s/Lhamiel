using System.Windows;
using System.IO;

namespace GGEZArchiver.View
{
    public partial class CompressionProgressWindow : Window
    {
        public CompressionProgressWindow()
        {
            InitializeComponent();
        }

        public void SetFileName(string fileName)
        {
            FileNameTextBlock.Text = Path.GetFileName(fileName);
        }

        public void UpdateProgress(int percentage, string detail = "")
        {
            ProgressBar.Value = percentage;
            ProgressTextBlock.Text = $"{percentage}%";
            
            if (!string.IsNullOrEmpty(detail))
            {
                DetailTextBlock.Text = detail;
            }
        }

        public void SetCompleted(string message = "圧縮が完了しました。")
        {
            ProgressBar.Value = 100;
            ProgressTextBlock.Text = "100%";
            DetailTextBlock.Text = message;
        }
    }
} 