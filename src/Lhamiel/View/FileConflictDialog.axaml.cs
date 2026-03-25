using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lhamiel.Models;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// ファイル競合解決ダイアログ（Windows 風 2ペイン構成）。
/// 左右の列にソースを配置し、各ファイルの取捨選択をユーザーに委ねる。
/// </summary>
public partial class FileConflictDialog : Window
{
    private readonly List<ConflictRowViewModel> _rows;
    private readonly string[] _columnNames;
    private readonly bool _isTwoPane;

    public FileConflictDialog() : this([], true) { }

    /// <param name="conflictGroups">競合グループ</param>
    /// <param name="isTwoPane">true: 左右2ペイン（展開時）、false: 縦1列（圧縮時）</param>
    public FileConflictDialog(List<FileConflictGroup> conflictGroups, bool isTwoPane)
    {
        _isTwoPane = isTwoPane;
        InitializeComponent();

        if (conflictGroups.Count == 0)
        {
            _columnNames = ["", ""];
            _rows = [];
            return;
        }

        var firstGroup = conflictGroups[0];

        if (isTwoPane)
        {
            // 2ペインモード（展開時）: エントリを左右に配置
            _columnNames =
            [
                firstGroup.Entries.Count > 0 ? (Path.GetDirectoryName(firstGroup.Entries[0].FullPath) ?? "") : "",
                firstGroup.Entries.Count > 1 ? (Path.GetDirectoryName(firstGroup.Entries[1].FullPath) ?? "") : ""
            ];

            _rows = [];
            foreach (var g in conflictGroups)
            {
                var entries = g.Entries;
                for (var i = 0; i < entries.Count; i += 2)
                {
                    var left = new ConflictCellViewModel(entries[i]);
                    var right = i + 1 < entries.Count ? new ConflictCellViewModel(entries[i + 1]) : null;
                    var fileName = i == 0 ? g.ConflictingName : "";
                    _rows.Add(new ConflictRowViewModel(fileName, g.ConflictingName, left, right) { ShowPath = false });
                }
            }
        }
        else
        {
            // 圧縮時: エントリを2つずつ左右に配置（2列グリッド）、グループヘッダーに一括CB
            _columnNames = ["", ""];

            _rows = [];
            foreach (var g in conflictGroups)
            {
                var groupRows = new List<ConflictRowViewModel>();
                for (var i = 0; i < g.Entries.Count; i += 2)
                {
                    var left = new ConflictCellViewModel(g.Entries[i]);
                    var right = i + 1 < g.Entries.Count ? new ConflictCellViewModel(g.Entries[i + 1]) : null;
                    var fileName = i == 0 ? g.ConflictingName : "";
                    var row = new ConflictRowViewModel(fileName, g.ConflictingName, left, right)
                    {
                        ShowGroupCheckBox = i == 0,
                        ShowPath = true
                    };
                    groupRows.Add(row);
                    _rows.Add(row);
                }
                if (groupRows.Count > 0)
                    groupRows[0].GroupRows = groupRows;
            }
        }

        // ヘッダーテキスト + ウィンドウタイトル
        var totalConflicts = conflictGroups.Count;
        var headerStr = App.Text("Conflict.Header", totalConflicts);
        Title = headerStr;
        var headerText = this.FindControl<TextBlock>("HeaderText");
        if (headerText != null)
            headerText.Text = headerStr;

        // 説明文（展開時と圧縮時で異なる）
        var descriptionText = this.FindControl<TextBlock>("DescriptionText");
        if (descriptionText != null)
            descriptionText.Text = isTwoPane
                ? App.Text("Conflict.Description.Extract")
                : App.Text("Conflict.Description.Compress");

        // 列ヘッダー（2ペインモードのみ表示）
        var columnHeaders = this.FindControl<Grid>("ColumnHeaders");
        if (columnHeaders != null && isTwoPane)
        {
            var leftName = _columnNames[0];
            var rightName = _columnNames[1];
            columnHeaders.Children.Add(CreateColumnCheckBox(leftName, 0, isLeft: true));
            if (!string.IsNullOrEmpty(rightName))
                columnHeaders.Children.Add(CreateColumnCheckBox(rightName, 1, isLeft: false));
        }
        else if (columnHeaders != null)
        {
            columnHeaders.IsVisible = false;
        }

        // リストバインド
        var conflictList = this.FindControl<ItemsControl>("ConflictList");
        if (conflictList != null)
            conflictList.ItemsSource = _rows;

        // 同一ファイルスキップ（常に表示）
        var skipCheckBox = this.FindControl<CheckBox>("SkipIdenticalCheckBox");
        if (skipCheckBox != null)
        {
            var identicalCount = CountIdenticalFiles(conflictGroups);
            skipCheckBox.Content = App.Text("Conflict.SkipIdentical", identicalCount);
            skipCheckBox.IsCheckedChanged += (_, _) => ApplySkipIdentical(skipCheckBox.IsChecked == true);
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// 列ヘッダーのチェックボックスを生成（「現在の場所 フォルダ名(リンク)」形式）
    /// </summary>
    private CheckBox CreateColumnCheckBox(string path, int column, bool isLeft)
    {
        var folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        var prefix = isLeft
            ? App.Text("Conflict.ColumnLeft", "")
            : App.Text("Conflict.ColumnRight", "");

        // プレフィックス部分（「現在の場所 」）
        var prefixBlock = new TextBlock
        {
            Text = prefix,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        // フォルダ名をクリック可能なリンクにする
        var linkBlock = new TextBlock
        {
            Text = folderName,
            FontSize = 12,
            Foreground = Avalonia.Media.Brushes.DodgerBlue,
            TextDecorations = Avalonia.Media.TextDecorations.Underline,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        ToolTip.SetTip(linkBlock, path);
        linkBlock.PointerPressed += (_, e) =>
        {
            e.Handled = true; // チェックボックスへの伝播を防止
            if (Directory.Exists(path))
                FolderOpener.OpenFolder(path);
        };

        // フォルダ名が長い場合に改行するよう WrapPanel を使用
        var panel = new Avalonia.Controls.WrapPanel
        {
            Children = { prefixBlock, linkBlock }
        };

        var checkBox = new CheckBox { Content = panel };
        checkBox.IsCheckedChanged += (_, _) => SetAllInColumn(isLeft, checkBox.IsChecked == true);
        Grid.SetColumn(checkBox, column);
        return checkBox;
    }

    /// <summary>
    /// 列全体を一括チェック/アンチェック
    /// </summary>
    private void SetAllInColumn(bool isLeft, bool isChecked)
    {
        foreach (var row in _rows)
        {
            var cell = isLeft ? row.Left : row.Right;
            if (cell != null)
                cell.IsSelected = isChecked;
        }
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e) => Close(FileConflictResult.Continue);
    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(FileConflictResult.Cancel);

    /// <summary>
    /// ユーザーが選択したファイルのリストを返す。
    /// 同じ行で両方選択されたファイルは自動的にリネームされる。
    /// </summary>
    public List<(string fullPath, string relativePath)> GetSelectedFiles()
    {
        var result = new List<(string fullPath, string relativePath)>();
        var usedPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            var selected = new List<ConflictCellViewModel>();
            if (row.Left is { IsSelected: true }) selected.Add(row.Left);
            if (row.Right is { IsSelected: true }) selected.Add(row.Right);

            if (selected.Count == 0) continue;

            var baseName = row.GroupName;

            // このグループで初めてのファイルならオリジナル名を使用
            var startIndex = 0;
            if (!usedPaths.ContainsKey(baseName))
            {
                usedPaths[baseName] = 1;
                result.Add((selected[0].Entry.FullPath, baseName));
                startIndex = 1;
            }

            // 2番目以降（または既にオリジナル名が使われている場合は全部）をリネーム
            for (var i = startIndex; i < selected.Count; i++)
            {
                var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(Path.GetFileName(baseName));
                var dir = Path.GetDirectoryName(baseName) ?? "";
                var counter = usedPaths.GetValueOrDefault(baseName, 1);
                string newPath;
                do
                {
                    newPath = string.IsNullOrEmpty(dir)
                        ? $"{stem}_{counter}{ext}"
                        : Path.Combine(dir, $"{stem}_{counter}{ext}");
                    counter++;
                } while (usedPaths.ContainsKey(newPath));

                usedPaths[baseName] = counter;
                usedPaths[newPath] = 1;
                result.Add((selected[i].Entry.FullPath, newPath));
            }
        }

        return result;
    }

    /// <summary>
    /// 2つのエントリが同一ファイルかどうかを判定する。
    /// 片方のサイズが 0（アーカイブ内ファイルでサイズ不明）の場合は日付のみで比較。
    /// </summary>
    private static bool AreEntriesIdentical(FileConflictEntry a, FileConflictEntry b)
    {
        // サイズが異なれば確実に非同一
        if (a.FileSize != b.FileSize)
            return false;

        // 日時は秒単位（切り捨て）で比較（ZIP は偶数秒精度、7z は100ns精度などの差を吸収）
        var aDate = new DateTime(a.LastModified.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
        var bDate = new DateTime(b.LastModified.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
        return aDate == bDate;
    }

    /// <summary>
    /// 日付とサイズが同一のファイルをリストから除外（非表示化）
    /// </summary>
    private void ApplySkipIdentical(bool skip)
    {
        foreach (var row in _rows)
        {
            if (row.Left == null || row.Right == null) continue;

            if (AreEntriesIdentical(row.Left.Entry, row.Right.Entry))
            {
                row.IsVisible = !skip;
                if (skip)
                {
                    // 非表示にする行はチェックもOFFに
                    row.Left.IsSelected = false;
                    row.Right.IsSelected = false;
                }
            }
        }

        // ヘッダーの件数を更新（表示中の行数 = 衝突ファイル数）
        var visibleCount = _rows.Count(r => r.IsVisible);
        var headerText = this.FindControl<TextBlock>("HeaderText");
        if (headerText != null)
        {
            var displayCount = skip ? visibleCount : _rows.Count;
            var headerStr = App.Text("Conflict.Header", displayCount);
            headerText.Text = headerStr;
            Title = headerStr;
        }
    }

    private static int CountIdenticalFiles(List<FileConflictGroup> groups)
    {
        var count = 0;
        foreach (var g in groups)
        {
            if (g.Entries.Count >= 2 && AreEntriesIdentical(g.Entries[0], g.Entries[1]))
                count++;
        }
        return count;
    }

    // ── Static helpers ──

    /// <summary>
    /// 上書き確認をバックグラウンドスレッドから表示する（FileOverwriteDialog の代替）。
    /// </summary>
    public static async Task<bool> CanOverwriteFromBackgroundAsync(string sourcePath, string destinationPath, Window? parentWindow)
    {
        if (parentWindow == null) return true;

        var destExists = File.Exists(destinationPath) || Directory.Exists(destinationPath);
        if (!destExists) return true;

        var destName = Path.GetFileName(destinationPath);
        if (string.IsNullOrEmpty(destName)) destName = destinationPath;

        var destInfo = File.Exists(destinationPath) ? new FileInfo(destinationPath) : null;
        var destDirInfo = Directory.Exists(destinationPath) ? new DirectoryInfo(destinationPath) : null;
        var srcInfo = File.Exists(sourcePath) ? new FileInfo(sourcePath) : null;

        // 左=ソース（これからコピー/展開するもの）、右=宛先（既に存在するもの）
        var group = new FileConflictGroup
        {
            ConflictingName = destName,
            Entries =
            [
                new FileConflictEntry(
                    sourcePath, Path.GetFileName(sourcePath),
                    srcInfo?.Length ?? 0,
                    srcInfo?.LastWriteTime ?? DateTime.MinValue),
                new FileConflictEntry(
                    destinationPath, destName,
                    destInfo?.Length ?? 0,
                    destInfo?.LastWriteTime ?? destDirInfo?.LastWriteTime ?? DateTime.MinValue)
            ]
        };

        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new FileConflictDialog([group], isTwoPane: true);
            var result = await dialog.ShowDialog<FileConflictResult?>(parentWindow) ?? FileConflictResult.Cancel;
            if (result != FileConflictResult.Continue) return false;

            // ユーザーがソース側（左ペイン）を選択した場合のみ上書き許可
            var selectedFiles = dialog.GetSelectedFiles();
            return selectedFiles.Any(f => f.fullPath == sourcePath);
        });
    }

    /// <summary>
    /// バックグラウンドスレッドから競合ダイアログを表示する。
    /// </summary>
    /// <param name="isTwoPane">true: 左右2ペイン（展開時）、false: 縦1列（圧縮時）</param>
    public static async Task<(FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
        ShowFromBackgroundAsync(List<FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true)
    {
        if (parentWindow == null)
        {
            var allFiles = groups.SelectMany(g => g.Entries.Select(e => (e.FullPath, e.RelativePath))).ToList();
            return (FileConflictResult.Continue, allFiles);
        }

        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new FileConflictDialog(groups, isTwoPane);
            var result = await dialog.ShowDialog<FileConflictResult?>(parentWindow) ?? FileConflictResult.Cancel;
            var selectedFiles = result == FileConflictResult.Continue
                ? dialog.GetSelectedFiles()
                : [];
            return (result, selectedFiles);
        });
    }
}

// ── ViewModels ──

/// <summary>
/// 1行 = 1つの競合ファイル名。左右にセルを持つ。
/// </summary>
public class ConflictRowViewModel : ObservableObject
{
    /// <summary>
    /// 表示用ファイル名（グループの先頭行のみ設定、2行目以降は空）
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// リネーム用のグループ名（常にファイル名を保持）
    /// </summary>
    public string GroupName { get; }

    public ConflictCellViewModel? Left { get; }
    public ConflictCellViewModel? Right { get; }
    public bool HasLeft => Left != null;
    public bool HasRight => Right != null;
    public bool HasFileName => !string.IsNullOrEmpty(FileName);

    /// <summary>
    /// 圧縮時（縦並び）のみパス名を表示する
    /// </summary>
    public bool ShowPath { get; set; }

    /// <summary>
    /// 縦1列モードで、グループヘッダーにチェックボックスを表示するか
    /// </summary>
    public bool ShowGroupCheckBox { get; set; }

    /// <summary>
    /// 2ペインモードで、グループヘッダーにテキストラベルを表示するか
    /// </summary>
    public bool ShowGroupLabel => HasFileName && !ShowGroupCheckBox;

    /// <summary>
    /// 同グループの全行への参照（グループ一括制御用）
    /// </summary>
    public List<ConflictRowViewModel>? GroupRows { get; set; }

    private bool _isGroupSelected;
    public bool IsGroupSelected
    {
        get => _isGroupSelected;
        set
        {
            if (!SetProperty(ref _isGroupSelected, value)) return;
            // グループ内の全セルを連動
            if (GroupRows == null) return;
            foreach (var row in GroupRows)
            {
                if (row.Left != null) row.Left.IsSelected = value;
                if (row.Right != null) row.Right.IsSelected = value;
            }
        }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ConflictRowViewModel(string fileName, string groupName, ConflictCellViewModel? left, ConflictCellViewModel? right)
    {
        FileName = fileName;
        GroupName = groupName;
        Left = left;
        Right = right;
    }
}

/// <summary>
/// 左右いずれかのセル（1つのファイルバージョン）。
/// </summary>
public class ConflictCellViewModel : ObservableObject
{
    public FileConflictEntry Entry { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FileSizeDisplay => Entry.FileSizeDisplay;
    public string LastModifiedDisplay => Entry.LastModified.ToString("yyyy/MM/dd HH:mm");
    public string ParentFolderName => Entry.ShortenedPath;
    public string FullPathDisplay => Path.GetDirectoryName(Entry.FullPath) ?? Entry.FullPath;
    public string DateAndSizeDisplay => $"{LastModifiedDisplay}  {FileSizeDisplay}";

    /// <summary>
    /// OS から取得したファイルアイコン
    /// </summary>
    public Bitmap? Icon { get; }

    public ConflictCellViewModel(FileConflictEntry entry)
    {
        Entry = entry;
        // 実在する画像・動画ファイルはサムネイル優先、それ以外はアイコン
        var isRealFile = (File.Exists(entry.FullPath) || Directory.Exists(entry.FullPath))
            && !ArchiveExtractor.IsSupportedArchiveType(entry.FullPath);
        Icon = isRealFile
            ? FileIconHelper.GetThumbnailOrIcon(entry.FullPath)
            : FileIconHelper.GetFileIcon(Path.GetFileName(entry.RelativePath));
    }
}
