using Avalonia.Controls;
using Avalonia.Interactivity;
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

        InitializeComponent();
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);

        TitleText.Text = App.Text("DiskSpace.Title");
        RequiredText.Text = DiskSpaceChecker.FormatSize(requiredBytes);
        AvailableText.Text = DiskSpaceChecker.FormatSize(availableBytes);
        ShortageText.Text = $"-{DiskSpaceChecker.FormatSize(shortageBytes)}";
        var root = Path.GetPathRoot(outputPath) ?? "";
        DriveText.Text = App.Text("DiskSpace.Drive", root);

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
        AvailableText.Text = DiskSpaceChecker.FormatSize(available);
        ShortageText.Text = $"-{DiskSpaceChecker.FormatSize(shortage)}";

        Logger.Log($"再開試行: まだ容量不足（空き={DiskSpaceChecker.FormatSize(available)}, 不足={DiskSpaceChecker.FormatSize(shortage)}）");
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
