using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lhamiel;
using Lhamiel.Util;
using Lhamiel.View;

internal static class Probe
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    static extern ref string DataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    static extern ref string SettingsPath(Settings? owner);
    static string Root = "";
    static bool Before;
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--decoy")
        {
            File.WriteAllText(args[1] + ".ready", Environment.ProcessId.ToString());
            var until = DateTime.UtcNow.AddSeconds(40);
            while (!File.Exists(args[1]) && DateTime.UtcNow < until) Thread.Sleep(50);
            return;
        }
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("LHAMIEL_PROGRESS_TEST_ROOT") ?? throw new ArgumentException("LHAMIEL_PROGRESS_TEST_ROOT missing"));
        Before = args.Contains("--before");
        if (Directory.Exists(Root)) throw new ArgumentException("Use an unused fixture root");
        Directory.CreateDirectory(Root);
        var settings = Path.Combine(Root, "settings");
        Directory.CreateDirectory(settings);
        DataDirectory(null) = settings;
        SettingsPath(null) = Path.Combine(settings, "settings.json");
        File.WriteAllText(SettingsPath(null), JsonSerializer.Serialize(new Settings {
            Locale = "ja_JP", Check4UpdatesOnStartup = false,
            OpenExtractionOutputFolder = false, OpenCompressionOutputFolder = false,
            AddExtractToContextMenu = false, AddCompressToContextMenu = false }));
        try { IpcAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { Write("ipc-failed.json", new { error = ex.ToString() }); Environment.ExitCode = 1; return; }
        if (args.Contains("--ipc-only")) return;
        Lhamiel.Program.BuildAvaloniaApp().AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            var windows = new List<ProgressWindow>();
            try
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                await Task.Delay(300);
                var a = Path.Combine(Root, "first", "same.zip");
                var b = Path.Combine(Root, "second", "same.zip");
                var longPath = Path.Combine(Root, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('長', 40), 8)), "same.zip");
                foreach (var locale in new[] { "ja_JP", "en_US" })
                {
                    App.SetLocale(locale);
                    var first = new ProgressWindow(App.Text("Progress.Extracting"));
                    var second = new ProgressWindow(App.Text("Progress.Extracting"));
                    var group = new ProgressWindow(App.Text("Progress.Extracting"));
                    SetTarget(first, [a], false); SetTarget(second, [b], false); SetTarget(group, [a, b, longPath], true);
                    windows.AddRange([first, second, group]);
                    first.Show(); second.Show(); group.Show();
                    await Task.Delay(300);
                    var identitiesDistinct = first.Title != second.Title;
                    var fullTargets = group.FindControl<TextBlock>("TargetTextBlock")?.Text?.Contains(longPath) == true;
                    Write(locale + "-windows.json", new { identitiesDistinct, fullTargets, windows = windows.Select(Snapshot).ToArray() });
                    for (var i = 0; i < windows.Count; i++)
                    {
                        var w = windows[i];
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w.Bounds.Width), (int)Math.Ceiling(w.Bounds.Height)));
                        bitmap.Render(w); bitmap.Save(Path.Combine(Root, $"{locale}-{i}.png"), PngBitmapEncoderOptions.Default);
                    }
                    var firstToken = first.GetCancellationToken(); var secondToken = second.GetCancellationToken();
                    first.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(100);
                    var individualCancellation = firstToken.IsCancellationRequested && !secondToken.IsCancellationRequested && second.IsVisible;
                    Write(locale + "-cancel.json", new { individualCancellation, firstCancelled = firstToken.IsCancellationRequested, secondCancelled = secondToken.IsCancellationRequested, second.IsVisible });
                    if (!Before && (!identitiesDistinct || !fullTargets || !individualCancellation)) throw new InvalidOperationException("Progress identity / cancellation contract failed");
                    foreach (var w in windows) w.Close(); windows.Clear();
                    // 存在確認を伴わない表示試験。実ドライブや UNC share は読み書きしない。
                    var rootSnapshots = new List<object>();
                    foreach (var rootTarget in new[] { @"C:\", @"D:\", @"\\ProgressFocus.invalid\share\" })
                    {
                        var rootWindow = new ProgressWindow(App.Text("Progress.Compressing"));
                        windows.Add(rootWindow);
                        SetTarget(rootWindow, [rootTarget], false);
                        rootWindow.Show();
                        await Task.Delay(150);
                        var expectedIdentity = rootTarget.TrimEnd('\\');
                        var rootCancel = rootWindow.FindControl<Button>("CancelButton")!;
                        var identified = rootWindow.Title!.Contains(expectedIdentity, StringComparison.Ordinal)
                            && AutomationProperties.GetName(rootCancel)?.Contains(expectedIdentity, StringComparison.Ordinal) == true;
                        rootSnapshots.Add(new { rootTarget, identified, window = Snapshot(rootWindow) });
                        if (!Before && !identified) throw new InvalidOperationException("Root identity contract failed: " + rootTarget);
                        rootWindow.Close(); windows.Clear();
                    }
                    Write(locale + "-roots.json", rootSnapshots);
                    var syntheticTargets = Enumerable.Range(0, 5000)
                        .Select(i => Path.Combine(Root, "synthetic", $"folder-{i:D4}", "same.zip")).ToArray();
                    var preparationTimer = Stopwatch.StartNew();
                    var largeGroup = new ProgressWindow(App.Text("Progress.Extracting"));
                    windows.Add(largeGroup);
                    SetTarget(largeGroup, syntheticTargets, true);
                    preparationTimer.Stop();
                    var displayTimer = Stopwatch.StartNew();
                    largeGroup.Show();
                    await Task.Delay(300);
                    displayTimer.Stop();
                    var largeCancel = largeGroup.FindControl<Button>("CancelButton")!;
                    var cancelOrigin = largeCancel.TranslatePoint(new Point(0, 0), largeGroup);
                    var cancelFits = cancelOrigin is { } origin && origin.X >= 0 && origin.Y >= 0
                        && origin.X + largeCancel.Bounds.Width <= largeGroup.Bounds.Width
                        && origin.Y + largeCancel.Bounds.Height <= largeGroup.Bounds.Height;
                    var targetBlock = largeGroup.FindControl<TextBlock>("TargetTextBlock");
                    var allTargetsRetained = targetBlock?.Text?.Split('\n').Length == syntheticTargets.Length;
                    var targetFits = targetBlock is not null && targetBlock.Bounds.Height <= targetBlock.MaxHeight;
                    Write(locale + "-large-group.json", new { count = syntheticTargets.Length, preparationBeforeShowMs = preparationTimer.Elapsed.TotalMilliseconds,
                        displayObservationMs = displayTimer.Elapsed.TotalMilliseconds, observationDelayMs = 300,
                        cancelFits, cancelVisible = largeCancel.IsVisible, allTargetsRetained, targetFits,
                        cancelOrigin = cancelOrigin?.ToString(), window = Snapshot(largeGroup) });
                    using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(largeGroup.Bounds.Width), (int)Math.Ceiling(largeGroup.Bounds.Height))))
                    {
                        bitmap.Render(largeGroup);
                        bitmap.Save(Path.Combine(Root, locale + "-large-group.png"), PngBitmapEncoderOptions.Default);
                    }
                    if (!Before && (!cancelFits || !largeCancel.IsVisible || !allTargetsRetained || !targetFits))
                        throw new InvalidOperationException("Large group layout contract failed");
                    largeGroup.Close(); windows.Clear();
                }
                Write("result.json", new { passed = !Before, before = Before, settingsIsolated = Settings.AppDataDirectory == settings, pid = Environment.ProcessId });
                desktop.Shutdown();
            }
            catch (Exception ex)
            {
                Write("failed.json", new { error = ex.ToString() });
                foreach (var w in windows) w.Close();
                desktop.Shutdown(1);
            }
        })).StartWithClassicDesktopLifetime([]);
    }
    static void SetTarget(ProgressWindow window, string[] paths, bool cancelAll) =>
        typeof(ProgressWindow).GetMethod("SetOperationTarget")?.Invoke(window, [paths, cancelAll]);
    static object Snapshot(ProgressWindow w)
    {
        var target = w.FindControl<TextBlock>("TargetTextBlock"); var cancel = w.FindControl<Button>("CancelButton");
        return new { w.Title, w.IsVisible, bounds = w.Bounds.ToString(), name = AutomationProperties.GetName(w),
            target = target?.Text, targetName = target is null ? null : AutomationProperties.GetName(target),
            targetBounds = target?.Bounds.ToString(), maxHeight = target?.MaxHeight, tooltip = target is null ? null : ToolTip.GetTip(target)?.ToString(),
            cancel = cancel?.Content?.ToString(), cancelName = cancel is null ? null : AutomationProperties.GetName(cancel), cancelBounds = cancel?.Bounds.ToString() };
    }
    static async Task IpcAsync()
    {
        var stopPath = Path.Combine(Root, "decoy.stop");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--decoy"); start.ArgumentList.Add(stopPath);
        using var decoy = Process.Start(start)!;
        try
        {
            var readyDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(stopPath + ".ready") && DateTime.UtcNow < readyDeadline) await Task.Delay(50);
            if (!File.Exists(stopPath + ".ready")) throw new TimeoutException("decoy startup");
            var method = typeof(IpcService).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name == "SendArgsToExistingInstanceAsync" && m.GetParameters()[1].ParameterType == typeof(string))
                .OrderByDescending(m => m.GetParameters().Length).First();
            var pipe = "Lhamiel_ProgressFocus_" + Guid.NewGuid().ToString("N");
            using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            uint owner = 0; bool received = false, callbackBeforeSend = false;
            Action<uint> callback = id => { owner = id; callbackBeforeSend = !received; };
            var read = Task.Run(async () => { await server.WaitForConnectionAsync(); using var reader = new StreamReader(server, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true); var json = await reader.ReadToEndAsync(); received = true; return json; });
            var arguments = method.GetParameters().Length == 4 ? new object?[] { new[] { "canary" }, pipe, CancellationToken.None, callback } : new object?[] { new[] { "canary" }, pipe, CancellationToken.None };
            var sent = await (Task<bool>)method.Invoke(null, arguments)!;
            var payload = await read.WaitAsync(TimeSpan.FromSeconds(5));
            var resolvedOwner = owner == (uint)Environment.ProcessId && owner != (uint)decoy.Id && callbackBeforeSend;
            Write("ipc-owner.json", new { sent, owner, serverPid = Environment.ProcessId, decoyPid = decoy.Id, sameName = decoy.ProcessName == Process.GetCurrentProcess().ProcessName, callbackBeforeSend, resolvedOwner, payload, before = Before });
            if (!sent || (!Before && !resolvedOwner)) throw new InvalidOperationException("IPC owner contract failed");
            server.Disconnect();
            var legacyRead = Task.Run(async () => { await server.WaitForConnectionAsync(); using var reader = new StreamReader(server, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true); return await reader.ReadToEndAsync(); });
            var legacyArguments = method.GetParameters().Length == 4 ? new object?[] { Array.Empty<string>(), pipe, CancellationToken.None, null } : new object?[] { Array.Empty<string>(), pipe, CancellationToken.None };
            var legacySent = await (Task<bool>)method.Invoke(null, legacyArguments)!;
            var legacyPayload = await legacyRead.WaitAsync(TimeSpan.FromSeconds(5));
            Write("ipc-legacy.json", new { legacySent, legacyPayload });
            if (!legacySent || legacyPayload != "[]") throw new InvalidOperationException("IPC compatibility failed");
        }
        finally
        {
            File.WriteAllText(stopPath, "stop");
            if (!decoy.WaitForExit(5000)) { decoy.Kill(); decoy.WaitForExit(); }
        }
    }
    static void Write(string name, object content) => File.WriteAllText(Path.Combine(Root, name), JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
}
