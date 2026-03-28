#if DEBUG
using CRDebugger.Avalonia;
using CRDebugger.Core;
using CRDebugger.Core.Options.Attributes;
using Lhamiel.Models;
using Lhamiel.View;

namespace Lhamiel.Util;

/// <summary>
/// デバッグモード専用: CRDebugger の初期化とダイアログプレビュー機能を提供する。
/// </summary>
internal static class DebugHelper
{
    /// <summary>
    /// CRDebugger を初期化してダイアログプレビューコンテナを登録する。
    /// </summary>
    public static void InitializeCRDebugger()
    {
        if (CRDebugger.Core.CRDebugger.IsInitialized) return;

        CRDebuggerAvaloniaExtensions.Initialize(opts =>
        {
            opts.Theme = CRDebugger.Core.Theming.CRTheme.System;
            opts.DefaultTab = CRTab.Options;
        });

        CRDebugger.Core.CRDebugger.AddOptionContainer(new DialogPreviewContainer());
        CRDebugger.Core.CRDebugger.Log("CRDebugger 初期化完了");
    }

    /// <summary>
    /// CRDebugger のトグル表示。
    /// </summary>
    public static void Toggle() => CRDebugger.Core.CRDebugger.Toggle();
}

/// <summary>
/// CRDebugger Options タブからダイアログをプレビュー表示するためのコンテナ。
/// </summary>
[CRContainer(Group = "ダイアログプレビュー")]
internal class DialogPreviewContainer
{
    [CRCategory("展開")]
    [CRAction(Label = "FileConflictDialog（展開時 2ペイン）")]
    public void ShowExtractionConflictDialog()
    {
        var groups = new List<FileConflictGroup>
        {
            new()
            {
                ConflictingName = "readme.txt",
                Entries =
                [
                    new FileConflictEntry(@"C:\Archive\readme.txt", "readme.txt", 1024, DateTime.Now.AddDays(-7)),
                    new FileConflictEntry(@"C:\Destination\readme.txt", "readme.txt", 2048, DateTime.Now.AddDays(-1))
                ]
            },
            new()
            {
                ConflictingName = "config.json",
                Entries =
                [
                    new FileConflictEntry(@"C:\Archive\config.json", "config.json", 512, DateTime.Now.AddDays(-30)),
                    new FileConflictEntry(@"C:\Destination\config.json", "config.json", 512, DateTime.Now.AddDays(-30))
                ]
            }
        };
        ShowDialog(() => new FileConflictDialog(groups, isTwoPane: true));
    }

    [CRCategory("圧縮")]
    [CRAction(Label = "FileConflictDialog（圧縮時 1列）")]
    public void ShowCompressionConflictDialog()
    {
        var groups = new List<FileConflictGroup>
        {
            new()
            {
                ConflictingName = "data.csv",
                Entries =
                [
                    new FileConflictEntry(@"C:\FolderA\data.csv", "data.csv", 4096, DateTime.Now.AddHours(-3)),
                    new FileConflictEntry(@"C:\FolderB\data.csv", "data.csv", 8192, DateTime.Now.AddHours(-1))
                ]
            }
        };
        ShowDialog(() => new FileConflictDialog(groups, isTwoPane: false));
    }

    [CRCategory("圧縮")]
    [CRAction(Label = "ProgressWindow（圧縮：準備 → 圧縮 → 完了）")]
    public void ShowCompressPreparingProgress()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var pw = new ProgressWindow(App.Text("Progress.Compressing"));
            pw.Show();

            // ① マーキー表示（一時コピー中）
            pw.SetIndeterminate(App.Text("Progress.PreparingFiles"));
            await Task.Delay(3000);

            // ② 通常進捗に戻る（圧縮処理中）
            for (var i = 0; i <= 100; i += 5)
            {
                pw.UpdateProgress(i);
                await Task.Delay(100);
            }

            await Task.Delay(500);
            pw.CloseSafe();
        });
    }

    [CRCategory("展開")]
    [CRAction(Label = "ProgressWindow（展開：展開 → 配置 → 完了）")]
    public void ShowExtractMovingProgress()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var pw = new ProgressWindow(App.Text("Progress.Extracting"));
            pw.Show();

            // ① 展開進捗（0% → 100%）
            for (var i = 0; i <= 100; i += 10)
            {
                pw.UpdateProgress(i);
                await Task.Delay(80);
            }

            await Task.Delay(500);

            // ② マーキー表示（ファイル配置中）
            pw.SetIndeterminate(App.Text("Progress.MovingFiles"));
            await Task.Delay(3000);

            // ③ 完了
            pw.UpdateProgress(100);
            await Task.Delay(500);
            pw.CloseSafe();
        });
    }

    [CRCategory("展開")]
    [CRAction(Label = "ErrorRecoveryDialog")]
    public void ShowErrorRecoveryDialog()
    {
        var errorInfo = new ArchiveErrorInfo
        {
            ErrorType = ArchiveErrorType.CorruptedFile,
            Message = "アーカイブが破損しています（プレビュー）",
            Details = "CRC チェックに失敗しました。ファイルの一部が破損している可能性があります。",
            IsRecoverable = true,
            ProblematicFilePath = @"C:\Sample\broken_archive.zip"
        };
        ShowDialog(() => new ErrorRecoveryDialog(errorInfo));
    }

    [CRCategory("展開")]
    [CRAction(Label = "DiskSpaceDialog")]
    public void ShowDiskSpaceDialog()
    {
        ShowDialog(() => new DiskSpaceDialog(
            requiredBytes: 1_073_741_824,
            availableBytes: 536_870_912,
            shortageBytes: 536_870_912,
            outputPath: @"C:\ExtractHere"));
    }

    /// <summary>
    /// ダイアログ（ShowDialog）をメインウィンドウの子として表示する。
    /// </summary>
    private static void ShowDialog<T>(Func<T> factory) where T : Avalonia.Controls.Window
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var owner = GetMainWindow();
            var dialog = factory();
            if (owner != null)
                await dialog.ShowDialog(owner);
            else
                dialog.Show();
        });
    }

    private static Avalonia.Controls.Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
#endif
