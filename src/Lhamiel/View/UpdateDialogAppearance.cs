using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using VelopackUpdateDialog;

namespace Lhamiel.View;

/// <summary>SDK の状態遷移とイベントを維持して、表示部分を共通ダイアログへ組み込む。</summary>
internal static class UpdateDialogAppearance
{
    internal static IDisposable Observe() => Window.WindowOpenedEvent.AddClassHandler<UpdateDialogWindow>(
        (window, _) => Apply(window));

    internal static void Apply(UpdateDialogWindow window)
    {
        // 必須要素を変更前にまとめて照合する。型違いも SDK 標準画面へ戻す。
        if (window.Content is not Panel root
            || window.FindControl<Control>("DialogBody") is not UpdateDialogView view
            || view.Content is not Grid body
            || view.Parent is not Panel parent
            || window.FindControl<Control>("AcrylicBackdrop") is not ExperimentalAcrylicBorder acrylic
            || window.FindControl<Control>("SolidBackdrop") is not Border solid)
        {
            // SDK 更新で構造が変わっても、更新操作自体は SDK 標準の画面で続行する。
            Util.Logger.Log("更新ダイアログの共通デザインを適用できませんでした。SDK 標準の画面を使用します。");
            return;
        }

        var actions = new Grid { DataContext = view.DataContext };
        // SDK は状態ごとに本文と操作を保持する。操作のイベントと状態表示をそのまま移す。
        foreach (var state in body.Children.OfType<StackPanel>())
        {
            var action = state.Children.LastOrDefault();
            if (action is not Button && !(action is StackPanel panel && panel.Children.All(c => c is Button)))
                continue;
            state.Children.Remove(action);
            action.Margin = default;
            action.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            var host = new ContentControl { Content = action, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            host.Bind(Visual.IsVisibleProperty, state.GetObservable(Visual.IsVisibleProperty));
            actions.Children.Add(host);
        }

        parent.Children.Remove(view);
        body.Margin = default;
        foreach (var button in actions.GetLogicalDescendants().OfType<Button>())
        {
            button.Classes.Remove("primary");
            button.Classes.Remove("secondary");
            foreach (var text in button.GetLogicalDescendants().OfType<TextBlock>())
                text.Bind(TextBlock.ForegroundProperty, window.GetResourceObservable("Brush.FG1"));
            foreach (var icon in button.GetLogicalDescendants().OfType<Avalonia.Controls.Shapes.Path>())
                icon.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, window.GetResourceObservable("Brush.FG1"));
        }
        // SDK 自身の背景切替処理が参照する背景要素は維持する。
        foreach (var grid in root.Children.OfType<Grid>().ToArray())
            root.Children.Remove(grid);
        acrylic.Material = new ExperimentalAcrylicMaterial { BackgroundSource = AcrylicBackgroundSource.Digger };
        acrylic.Material.Bind(ExperimentalAcrylicMaterial.TintColorProperty, window.GetResourceObservable("Color.Window"));
        solid.Bind(Border.BackgroundProperty, window.GetResourceObservable("Brush.Window"));
        var scrim = new Border { Opacity = 0.5, IsHitTestVisible = false };
        scrim.Bind(Border.BackgroundProperty, window.GetResourceObservable("Brush.Window"));
        root.Children.Add(scrim);
        root.Children.Add(DialogChrome.Create(window, view, actions));
        window.TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
        Util.AcrylicFallbackHelper.Attach(window);
    }
}
