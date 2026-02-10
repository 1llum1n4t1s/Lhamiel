using Microsoft.Win32;
using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// Windows スタートアップへのアプリケーション登録を管理するクラス
/// ログイン時にサイレント更新チェックを実行するために使用
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Lhamiel";

    /// <summary>
    /// スタートアップにアプリケーションを登録する
    /// </summary>
    public static void Register()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Log("スタートアップ登録: 実行ファイルパスを取得できませんでした。", LogLevel.Warning);
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Logger.Log("スタートアップ登録: レジストリキーを開けませんでした。", LogLevel.Warning);
                return;
            }

            var value = $"\"{exePath}\" --update-check";
            key.SetValue(EntryName, value);
            Logger.Log($"スタートアップに登録しました: {value}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"スタートアップ登録に失敗しました: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// スタートアップからアプリケーションの登録を解除する
    /// </summary>
    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (key.GetValue(EntryName) != null)
            {
                key.DeleteValue(EntryName);
                Logger.Log("スタートアップ登録を解除しました。", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"スタートアップ登録解除に失敗しました: {ex.Message}", LogLevel.Warning);
        }
    }
}
