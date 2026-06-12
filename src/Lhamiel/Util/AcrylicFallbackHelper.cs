using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Win32;

namespace Lhamiel.Util;

/// <summary>
/// アクリルブラーが実際には効かない環境（Windows の透過効果 OFF・リモートデスクトップ・
/// プラットフォーム非対応）で、ExperimentalAcrylicBorder の代わりにテーマ色の不透明背景を表示する。
/// アクリルの背後は不透明黒になるため、特にライトテーマでは薄い tint が黒と混ざって
/// くすんだ灰色に見えてしまう。Windows は透過効果 OFF でも AcrylicBlur を「適用済み」と
/// 報告するため、ActualTransparencyLevel だけでなく OS 側の状態（レジストリ・リモート判定）も見る。
/// </summary>
internal static class AcrylicFallbackHelper
{
    private const string FallbackBorderName = "AcrylicOpaqueFallback";
    private const int SmRemoteSession = 0x1000;

    /// <summary>
    /// ウィンドウにフォールバック判定を取り付ける。Opened で初回適用し、
    /// 透過レベル変化と Activated（設定アプリで透過を切り替えて戻ってきたケース）で再評価する。
    /// </summary>
    public static void Attach(Window window)
    {
        window.Opened += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ActualTransparencyLevelProperty)
                Apply(window);
        };
    }

    private static void Apply(Window window)
    {
        // 全ウィンドウ共通構造: root Panel 直下に ExperimentalAcrylicBorder
        if (window.Content is not Panel rootPanel)
            return;
        var acrylic = rootPanel.Children.OfType<ExperimentalAcrylicBorder>().FirstOrDefault();
        if (acrylic is null)
            return;

        var useFallback = ShouldUseOpaqueBackground(window);
        var fallback = rootPanel.Children.OfType<Border>().FirstOrDefault(b => b.Name == FallbackBorderName);
        if (fallback is null)
        {
            if (!useFallback)
                return; // アクリルが効く環境では何も足さない
            fallback = new Border { Name = FallbackBorderName, IsHitTestVisible = false };
            // Brush.Window はテーマ切替で Color が差し替わる共有ブラシ（ライト/ダーク追従はこれで足りる）
            fallback.Bind(Border.BackgroundProperty, window.GetResourceObservable("Brush.Window"));
            rootPanel.Children.Insert(rootPanel.Children.IndexOf(acrylic), fallback);
        }

        acrylic.IsVisible = !useFallback;
        fallback.IsVisible = useFallback;
    }

    private static bool ShouldUseOpaqueBackground(Window window)
    {
        var acrylicGranted = window.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
        return ShouldUseOpaqueBackgroundCore(
            acrylicGranted,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() && IsRemoteSession(),
            !OperatingSystem.IsWindows() || IsTransparencyEnabled());
    }

    /// <summary>判定ロジック本体（テスト用に純粋関数として分離）。</summary>
    internal static bool ShouldUseOpaqueBackgroundCore(
        bool acrylicGranted, bool isWindows, bool isRemoteSession, bool transparencyEnabled)
    {
        if (!acrylicGranted)
            return true;
        // Windows は透過効果 OFF / RDP でも acrylicGranted=true を返すので OS 状態で補正する
        return isWindows && (isRemoteSession || !transparencyEnabled);
    }

    private static bool IsRemoteSession()
    {
        try
        {
            return GetSystemMetrics(SmRemoteSession) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsTransparencyEnabled()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency",
                1);
            // 値が無い / 想定外の型なら透過有効とみなして従来動作（アクリル）を維持
            return value is not int enabled || enabled != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
