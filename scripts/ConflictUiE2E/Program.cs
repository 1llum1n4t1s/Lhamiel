using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lhamiel;
using Lhamiel.Models;
using Lhamiel.Util;
using Lhamiel.View;
using System.Runtime.CompilerServices;
using System.Text.Json;

// 失敗モード: 個別・列の名前欠落、左右/同名候補を判別不能、ロケール変更で名前が陳腐化、空一覧のクローズ不能。
// 実製品のダイアログを表示し、設定・ファイル・操作マーカーをこの fixture のみに隔離する。
internal static class ConflictUiProbe
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    static extern ref string DataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    static extern ref string SettingsPath(Settings? owner);
    static string Root = "";
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--ui-review")
            throw new ArgumentException("未使用の fixture 絶対パスを指定してください");
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("LHAMIEL_CONFLICT_TEST_ROOT") ?? throw new ArgumentException("fixture root missing"));
        if (Directory.Exists(Root)) throw new ArgumentException("fixture already exists");
        Directory.CreateDirectory(Root);
        var settings = Path.Combine(Root, "settings");
        Directory.CreateDirectory(settings);
        DataDirectory(null) = settings;
        SettingsPath(null) = Path.Combine(settings, "settings.json");
        File.WriteAllText(SettingsPath(null), JsonSerializer.Serialize(new Settings {
            Locale = "ja_JP", Check4UpdatesOnStartup = false,
            OpenExtractionOutputFolder = false, OpenCompressionOutputFolder = false,
            AddExtractToContextMenu = false, AddCompressToContextMenu = false }));
        Lhamiel.Program.BuildAvaloniaApp().AfterSetup(builder => Dispatcher.UIThread.Post(async () =>
        {
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            FileConflictDialog? dialog = null;
            try
            {
                await Task.Delay(300);
                var main = desktop.MainWindow!;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var source = Path.Combine(Root, "source", "readme.txt");
                var destination = Path.Combine(Root, "destination", "readme.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(source, "source canary");
                File.WriteAllText(destination, "destination canary");
                var groups = new List<FileConflictGroup> { new() {
                    ConflictingName = "nested/readme.txt",
                    Entries = [new(source, "nested/readme.txt", 13, DateTime.Now),
                               new(destination, "nested/readme.txt", 18, DateTime.Now)]
                }};
                dialog = new FileConflictDialog(groups, true);
                _ = dialog.ShowDialog(main);
                await Task.Delay(250);
                Write("two-pane-ja.json", Snapshot(dialog));
                await Signal("locale.signal");
                App.SetLocale("en_US");
                await Task.Delay(250);
                Write("two-pane-en.json", Snapshot(dialog));
                await Signal("compression.signal");
                dialog.Close();
                dialog = new FileConflictDialog(groups, false);
                _ = dialog.ShowDialog(main);
                await Task.Delay(250);
                Write("compression-en.json", Snapshot(dialog));
                await Signal("finish.signal");
                dialog.Close();
                dialog = new FileConflictDialog([], true);
                _ = dialog.ShowDialog(main);
                await Task.Delay(200);
                Write("empty.json", Snapshot(dialog));
                dialog.Close();
                Write("result.json", new { passed = true, processId = Environment.ProcessId,
                    settingsIsolated = Settings.AppDataDirectory == settings, stages = 4 });
                desktop.Shutdown();
            }
            catch (Exception ex)
            {
                Write("failed.json", new { error = ex.ToString() });
                dialog?.Close();
                desktop.Shutdown(1);
            }
        })).StartWithClassicDesktopLifetime([]);
    }
    static object Snapshot(FileConflictDialog dialog) => new {
        pid = Environment.ProcessId, dialog.Title, dialog.IsVisible,
        checkBoxes = dialog.GetVisualDescendants().OfType<CheckBox>().Where(x => x.IsVisible).Select(x => new {
            name = AutomationProperties.GetName(x), content = x.Content?.ToString(),
            checkedValue = x.IsChecked }).ToArray()
    };
    static void Write(string name, object content) => File.WriteAllText(Path.Combine(Root,name),
        JsonSerializer.Serialize(content,new JsonSerializerOptions { WriteIndented = true }));
    static async Task Signal(string name)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (!File.Exists(Path.Combine(Root,name))) await Task.Delay(100, deadline.Token);
    }
}
