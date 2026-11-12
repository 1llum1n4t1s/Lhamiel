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

    /// <summary>本文を収めた Grid の既定名（全ダイアログの XAML で共通）。</summary>
    internal const string DefaultBodyName = "DialogBody";

    /// <summary>操作ボタン群の既定名。ProgressWindow だけはキャンセルボタン単体（<c>CancelButton</c>）を渡す。</summary>
    internal const string DefaultActionsName = "DialogActions";

    /// <summary>
    /// ダイアログ code-behind の共通初期化。XAML 読込み（InitializeComponent / AvaloniaXamlLoader.Load）の直後に呼ぶ。
    /// <para>
    /// 順序は固定: (1) <see cref="Util.AcrylicFallbackHelper.Attach"/> → (2) <see cref="Attach"/> → (3) <see cref="Util.AppIconManager.Apply"/>。
    /// (1) は root Panel の直接の子にある ExperimentalAcrylicBorder を探索する契約なので、(2) が本文を
    /// DialogChrome へ移し替えて root の子構成を変える前に呼ぶ（従来の各ダイアログと同じ順序）。
    /// (3) のアイコン適用は見た目のみで順序依存はないが、従来どおり最後に行う。
    /// </para>
    /// </summary>
    /// <param name="window">初期化するダイアログ。</param>
    /// <param name="actionsName">操作領域のコントロール名。既定は <see cref="DefaultActionsName"/>。</param>
    /// <param name="appIconVariant">ウィンドウアイコンの variant。<c>null</c> なら現在の設定値（従来の <c>AppIconManager.Apply(this)</c> と同じ）。</param>
    internal static void InitializeDialog(Window window, string actionsName = DefaultActionsName, string? appIconVariant = null)
    {
        Util.AcrylicFallbackHelper.Attach(window);
        Attach(window, DefaultBodyName, actionsName);
        Util.AppIconManager.Apply(window, appIconVariant);
    }

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
