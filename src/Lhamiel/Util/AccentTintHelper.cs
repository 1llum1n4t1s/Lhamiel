using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Lhamiel.Util;

/// <summary>
/// OS のアクセントカラーをテーマ基調色の上にごく薄く上乗せするオーバーレイ。
/// ライトテーマ + 青アクセントなら背景がごく薄い水色になる、という趣旨。
/// ルート Panel のアクリル直後（コンテンツより下）に挿入するため、
/// アクリル有効時にも AcrylicFallbackHelper の不透明フォールバック時にも同様に効く。
/// OS 側でアクセントカラーを変更したときは ColorValuesChanged で即追従する。
/// </summary>
internal static class AccentTintHelper
{
    private const string OverlayName = "AccentTintOverlay";

    /// <summary>上乗せの濃さ（α 0x18 ≈ 9%。「凄く薄い」が知覚できる程度）</summary>
    private const byte TintAlpha = 0x18;

    public static void Attach(Window window)
    {
        window.Opened += (_, _) => Apply(window);

        // ダイアログは短寿命なので、long-lived な PlatformSettings への購読は Closed で必ず解除する
        var platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is null)
            return;
        EventHandler<PlatformColorValues> onColorsChanged = (_, _) => Apply(window);
        platformSettings.ColorValuesChanged += onColorsChanged;
        window.Closed += (_, _) => platformSettings.ColorValuesChanged -= onColorsChanged;
    }

    private static void Apply(Window window)
    {
        // 全ウィンドウ共通構造: root Panel 直下に ExperimentalAcrylicBorder
        if (window.Content is not Panel rootPanel)
            return;
        var acrylic = rootPanel.Children.OfType<ExperimentalAcrylicBorder>().FirstOrDefault();
        if (acrylic is null)
            return;

        Color accent;
        try
        {
            var colors = Application.Current?.PlatformSettings?.GetColorValues();
            if (colors is not { } c)
                return;
            accent = c.AccentColor1;
        }
        catch (Exception)
        {
            return; // アクセントカラーが取得できなければ上乗せなし
        }

        var overlay = rootPanel.Children.OfType<Border>().FirstOrDefault(b => b.Name == OverlayName);
        if (overlay is null)
        {
            overlay = new Border { Name = OverlayName, IsHitTestVisible = false };
            // アクリル（とその直前に挿入されるフォールバック背景）の上・コンテンツの下
            rootPanel.Children.Insert(rootPanel.Children.IndexOf(acrylic) + 1, overlay);
        }

        overlay.Background = new SolidColorBrush(Color.FromArgb(TintAlpha, accent.R, accent.G, accent.B));
    }
}
