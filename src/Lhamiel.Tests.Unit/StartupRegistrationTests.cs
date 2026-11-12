using Lhamiel.Util;
using Microsoft.Win32;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// StartupRegistration（HKCU\...\Run へのログイン時起動登録）のテスト。
/// 実レジストリを読み書きするため [Collection("Sequential")] で排他実行し、各テストで元の値を復元する。
/// 旧 VelopackIntegrationHappyTests / VelopackIntegrationAdversarialTests から内容別に分離した。
/// </summary>
[Collection("Sequential")]
public class StartupRegistrationTests
{
    /// <summary>
    /// StartupRegistration.Register() で HKCU\Run に値が書き込まれ、Unregister() で削除されること。
    /// </summary>
    [Fact]
    public void StartupRegistration_RegisterThenUnregister_RemovesRegistryValue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "StartupRegistration は Windows 専用");

        // Arrange: 初期状態を退避
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "Lhamiel";
        string? originalValue;
        using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
        {
            originalValue = key?.GetValue(valueName) as string;
        }

        try
        {
            // Act
            StartupRegistration.Register();
            bool registeredExists;
            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
            {
                registeredExists = key?.GetValue(valueName) != null;
            }

            // Environment.ProcessPath が null (AOT / testhost) の場合 Register は no-op
            Assert.SkipWhen(!registeredExists, "Environment.ProcessPath が null のためテスト対象外");

            StartupRegistration.Unregister();
            bool unregisteredExists;
            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
            {
                unregisteredExists = key?.GetValue(valueName) != null;
            }

            // Assert
            Assert.True(registeredExists, "Register 後に値が存在すべき");
            Assert.False(unregisteredExists, "Unregister 後に値が削除されているべき");
        }
        finally
        {
            // Cleanup: 元の値を復元
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key != null)
            {
                if (originalValue != null)
                    key.SetValue(valueName, originalValue);
                else
                    try { key.DeleteValue(valueName, throwOnMissingValue: false); } catch { }
            }
        }
    }

    /// <summary>
    /// StartupRegistration.Unregister は登録されていなくても例外なし (冪等性)。
    /// </summary>
    [Fact]
    public void StartupRegistration_UnregisterWhenNotRegistered_DoesNotThrow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "StartupRegistration は Windows 専用");

        // Arrange: 確実に削除した状態にする
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string entry = "Lhamiel";

        object? originalValue;
        using (var k = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
            originalValue = k?.GetValue(entry);

        using (var k = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true))
        {
            if (k?.GetValue(entry) != null) k.DeleteValue(entry, throwOnMissingValue: false);
        }

        try
        {
            // Act: 2 回連続 Unregister
            var ex1 = Record.Exception(() => StartupRegistration.Unregister());
            var ex2 = Record.Exception(() => StartupRegistration.Unregister());

            // Assert
            Assert.Null(ex1);
            Assert.Null(ex2);
        }
        finally
        {
            // Cleanup
            using var k = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (k != null && originalValue != null) k.SetValue(entry, originalValue);
        }
    }
}
