using System.ComponentModel;
using Lhamiel.Models;
using Lhamiel.Util;
using Microsoft.Win32;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// VelopackUpdateDialog.Avalonia 統合の正常系テスト (Happy Path)。
/// /rere レビュー後の修正で追加された LhamielUpdateStrings / Settings 新規プロパティ /
/// SettingsManager.MutateAndSave / UpdateChecker.TryBuildUpdateManager /
/// TempCleanup / DiagnosticsCollector / StartupRegistration の代表的な使用シナリオを検証する。
/// </summary>
public class VelopackIntegrationHappyTests
{
    /// <summary>
    /// LhamielUpdateStrings.Instance を複数回取得しても同一の参照が返されること (シングルトン保証)。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_Instance_ReturnsSameSingletonInstance()
    {
        // Arrange
        var first = LhamielUpdateStrings.Instance;

        // Act
        var second = LhamielUpdateStrings.Instance;

        // Assert
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    /// <summary>
    /// LhamielUpdateStrings の全 getter プロパティが null を返さないこと。
    /// App.Current が未初期化でも App.Text のフォールバックで "Text.{key}" が返る前提。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_AllGetters_ReturnNonNullStrings()
    {
        // Arrange
        var strings = LhamielUpdateStrings.Instance;

        // Act & Assert: IUpdateDialogStrings の 8 プロパティを全て検証
        Assert.NotNull(strings.Title);
        Assert.NotNull(strings.AvailableHeader);
        Assert.NotNull(strings.DownloadAndInstall);
        Assert.NotNull(strings.IgnoreThisVersion);
        Assert.NotNull(strings.UpToDateMessage);
        Assert.NotNull(strings.ErrorHeader);
        Assert.NotNull(strings.Close);
        Assert.NotNull(strings.CheckingMessage);
    }

    /// <summary>
    /// NotifyLocaleChanged() を呼ぶと PropertyName=null (全プロパティ更新シグナル) で PropertyChanged が発火する。
    /// XAML バインディングの一括再評価を要求する INPC 契約。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_NotifyLocaleChanged_RaisesPropertyChangedWithNullName()
    {
        // Arrange
        var strings = LhamielUpdateStrings.Instance;
        string? raisedName = "not-raised";
        var raised = false;
        PropertyChangedEventHandler handler = (s, e) =>
        {
            raised = true;
            raisedName = e.PropertyName;
        };
        strings.PropertyChanged += handler;

        try
        {
            // Act
            strings.NotifyLocaleChanged();

            // Assert
            Assert.True(raised);
            Assert.Null(raisedName); // null = 全プロパティ更新
        }
        finally
        {
            strings.PropertyChanged -= handler;
        }
    }

    /// <summary>
    /// Settings の新規プロパティのデフォルト値: Check4UpdatesOnStartup=true, IgnoreUpdateTag="".
    /// </summary>
    [Fact]
    public void Settings_DefaultValues_Check4UpdatesOnStartupIsTrue_IgnoreUpdateTagIsEmpty()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.True(settings.Check4UpdatesOnStartup);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// SanitizeAfterLoad() が IgnoreUpdateTag の前後の空白を Trim すること。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAfterLoad_TrimsIgnoreUpdateTag()
    {
        // Arrange
        var settings = new Settings
        {
            IgnoreUpdateTag = "   v1.2.3   "
        };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.Equal("v1.2.3", settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// ResetToDefaults() で Check4UpdatesOnStartup / IgnoreUpdateTag が既定値に戻ること。
    /// </summary>
    [Fact]
    public void Settings_ResetToDefaults_RestoresCheck4UpdatesAndIgnoreTag()
    {
        // Arrange
        var settings = new Settings
        {
            Check4UpdatesOnStartup = false,
            IgnoreUpdateTag = "v9.9.9"
        };

        // Act
        settings.ResetToDefaults();

        // Assert
        Assert.True(settings.Check4UpdatesOnStartup);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// SettingsManager.MutateAndSave() に渡したミューテータが Current 設定に適用され、後続参照に反映されること。
    /// </summary>
    [Collection("Sequential")]
    public class MutateAndSaveHappyTests
    {
        [Fact]
        public void SettingsManager_MutateAndSave_AppliesMutationAtomically()
        {
            // Arrange
            var mgr = SettingsManager.Instance;
            var original = mgr.Current.IgnoreUpdateTag;
            var marker = "happy-marker-" + Guid.NewGuid().ToString("N");

            try
            {
                // Act
                mgr.MutateAndSave(s => s.IgnoreUpdateTag = marker);

                // Assert
                Assert.Equal(marker, mgr.Current.IgnoreUpdateTag);
            }
            finally
            {
                try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = original); } catch { }
            }
        }
    }

    /// <summary>
    /// MutateAndSave に null Action を渡すと ArgumentNullException が即座にスローされること。
    /// </summary>
    [Fact]
    public void SettingsManager_MutateAndSave_NullMutator_ThrowsArgumentNullException()
    {
        // Arrange
        Action<Settings>? mutator = null;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => SettingsManager.Instance.MutateAndSave(mutator!));
        Assert.Equal("mutator", ex.ParamName);
    }

    /// <summary>
    /// TryBuildUpdateManager は UpdateManager コンストラクタが内部で VelopackLocator.Current を要求するため、
    /// VelopackApp.Build().Run() を呼んでいない単体テスト環境では InvalidOperationException がスローされる。
    /// この挙動は production では Program.cs の VelopackApp.Build().Run() で初期化済みなので問題ない。
    /// テスト環境では「Velopack 未初期化時に投げる」契約を固定する。
    /// </summary>
    [Fact]
    public void UpdateChecker_TryBuildUpdateManager_VelopackNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new Settings();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => UpdateChecker.TryBuildUpdateManager(settings));
        Assert.Contains("VelopackLocator", ex.Message);
    }

    /// <summary>
    /// CleanupOrphanedTempDirectories が 30 分以上前に作成された Lhamiel_Temp_&lt;Guid:N&gt; を削除すること。
    /// </summary>
    [Collection("Sequential")]
    public class TempCleanupHappyTests
    {
        [Fact]
        public void TempCleanup_CleanupOrphanedTempDirectories_DeletesOldAutoGeneratedDirectories()
        {
            // Arrange
            var tempRoot = Path.GetTempPath();
            var guid = Guid.NewGuid().ToString("N");
            var oldDir = Path.Combine(tempRoot, $"Lhamiel_Temp_{guid}");
            Directory.CreateDirectory(oldDir);
            // 1 時間前に偽装 (MinAge=30 分超え)
            var oldTime = DateTime.UtcNow.AddHours(-1);
            Directory.SetLastWriteTimeUtc(oldDir, oldTime);

            try
            {
                // Act
                TempCleanup.CleanupOrphanedTempDirectories();

                // Assert
                Assert.False(Directory.Exists(oldDir), "30 分以上前の Lhamiel_Temp_<Guid> は削除されるべき");
            }
            finally
            {
                if (Directory.Exists(oldDir))
                {
                    try { Directory.Delete(oldDir, recursive: true); } catch { /* best-effort */ }
                }
            }
        }
    }

    /// <summary>
    /// StartupRegistration.Register() で HKCU\Run に値が書き込まれ、Unregister() で削除されること。
    /// </summary>
    [Collection("Sequential")]
    public class StartupRegistrationHappyTests
    {
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
    }

    /// <summary>
    /// MaskSensitiveValues が token/secret/password 系のキーを SensitivePatternRegex 経由でマスクすること。
    /// _sensitiveKeys 空配列でも regex フォールバックが効くことを検証。
    /// </summary>
    [Fact]
    public void DiagnosticsCollector_MaskSensitiveValues_MasksTokenKeysViaRegex()
    {
        // Arrange
        var input = """{"AccessToken":"abc123","ApiKey":"xyz789","Password":"p@ss","NormalField":"keep-me"}""";

        // Act
        var masked = DiagnosticsCollector.MaskSensitiveValues(input);

        // Assert
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("xyz789", masked);
        Assert.DoesNotContain("p@ss", masked);
        Assert.Contains("keep-me", masked);
    }
}
