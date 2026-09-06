using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.LogicalTree;

namespace Lhamiel.View;

/// <summary>本文と操作領域を共通の枠で囲む。アクリルは各窓のメイン画面準拠の背景を使う。</summary>
public partial class DialogChrome : UserControl
{
    public DialogChrome() => AvaloniaXamlLoader.Load(this);

    internal static void Attach(Window window, string bodyName, string actionsName)
    {
        var body = window.FindControl<Grid>(bodyName)!;
        var actions = window.FindControl<Control>(actionsName)!;
        var root = (Panel)window.Content!;
        var actionRow = Grid.GetRow(actions);
        body.Children.Remove(actions);
        body.RowDefinitions.RemoveAt(actionRow);
        root.Children.Remove(body);
        body.Margin = default;
        root.Children.Add(Create(window, body, actions));
    }

    internal static DialogChrome Create(Window window, Control body, Control actions)
    {
        actions.Margin = default;
        actions.ClearValue(Grid.RowProperty);
        if (actions is StackPanel stack)
            stack.Spacing = 8;

        // 列幅・可視性・イベントは維持し、旧ボタン寸法だけを共通スタイルへ移す。
        var buttons = actions is Button button ? new[] { button } : actions.GetLogicalDescendants().OfType<Button>();
        foreach (var action in buttons)
        {
            action.ClearValue(HeightProperty);
            action.ClearValue(Button.PaddingProperty);
            action.Classes.Add("dialogAction");
        }

        var chrome = new DialogChrome();
        chrome.FindControl<ContentControl>("BodyHost")!.Content = body;
        chrome.FindControl<ContentControl>("ActionHost")!.Content = actions;
        chrome.FindControl<TextBlock>("Caption")!.Bind(TextBlock.TextProperty, window.GetObservable(Window.TitleProperty));
        chrome.FindControl<Button>("CloseButton")!.Click += (_, _) => window.Close();
        chrome.FindControl<Grid>("TitleBar")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                window.BeginMoveDrag(e);
        };
        var surface = chrome.FindControl<Border>("Surface")!;
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(surface.Bounds.Size), 14, 14);
        window.WindowDecorations = WindowDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = 32;
        return chrome;
    }
}
