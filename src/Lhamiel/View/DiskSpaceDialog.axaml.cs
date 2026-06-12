using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// ディスク容量不足ダイアログ。
/// 容量の確保を促し、「再開」か「キャンセル」を選択させる。
/// </summary>
public partial class DiskSpaceDialog : Window
{
    private readonly long _requiredBytes;
    private readonly string _outputPath;

    public DiskSpaceDialog() : this(0, 0, 0, "") { }

    public DiskSpaceDialog(long requiredBytes, long availableBytes, long shortageBytes, string outputPath)
    {
        _requiredBytes = requiredBytes;
        _outputPath = outputPath;

        AvaloniaXamlLoader.Load(this);
        AcrylicFallbackHelper.Attach(this);

        var titleText = this.FindControl<TextBlock>("TitleText");
        var requiredText = this.FindControl<TextBlock>("RequiredText");
        var availableText = this.FindControl<TextBlock>("AvailableText");
        var shortageText = this.FindControl<TextBlock>("ShortageText");
        var driveText = this.FindControl<TextBlock>("DriveText");

        if (titleText != null)
            titleText.Text = App.Text("DiskSpace.Title");
        if (requiredText != null)
            requiredText.Text = DiskSpaceChecker.FormatSize(requiredBytes);
        if (availableText != null)
            availableText.Text = DiskSpaceChecker.FormatSize(availableBytes);
        if (shortageText != null)
            shortageText.Text = $"-{DiskSpaceChecker.FormatSize(shortageBytes)}";
        if (driveText != null)
        {
            var root = Path.GetPathRoot(outputPath) ?? "";
            driveText.Text = App.Text("DiskSpace.Drive", root);
        }

        Title = App.Text("DiskSpace.Title");
    }

    private void RetryButton_Click(object? sender, RoutedEventArgs e)
    {
        // 再チェック: 容量が確保されたか確認
        var available = DiskSpaceChecker.GetAvailableSpace(_outputPath);
        if (available >= _requiredBytes)
        {
            Close(true);
            return;
        }

        // まだ足りない → 表示を更新して再表示のまま
        var shortage = _requiredBytes - available;
        var availableText = this.FindControl<TextBlock>("AvailableText");
        var shortageText = this.FindControl<TextBlock>("ShortageText");

        if (availableText != null)
            availableText.Text = DiskSpaceChecker.FormatSize(available);
        if (shortageText != null)
            shortageText.Text = $"-{DiskSpaceChecker.FormatSize(shortage)}";

        Logger.Log($"再開試行: まだ容量不足（空き={DiskSpaceChecker.FormatSize(available)}, 不足={DiskSpaceChecker.FormatSize(shortage)}）");
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
