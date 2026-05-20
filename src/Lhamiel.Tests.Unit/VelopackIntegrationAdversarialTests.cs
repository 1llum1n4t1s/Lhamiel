using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lhamiel.Models;
using Lhamiel.Util;
using Microsoft.Win32;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// VelopackUpdateDialog.Avalonia 統合の嫌がらせテスト (Adversarial)。
/// 境界値・並行性・リソース枯渇・状態遷移・型パンチ・環境異常の 6 視点を 1 ファイルに集約。
/// /rere レビュー後の修正対象 7 領域 (LhamielUpdateStrings / Settings / SettingsManager /
/// UpdateChecker / TempCleanup / DiagnosticsCollector / StartupRegistration) を攻める。
/// </summary>
public class VelopackIntegrationAdversarialTests
{
    // ────────────────────────────────────────────────────────────────
    // 🗡️ 境界値 (Boundary Assault)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// IgnoreUpdateTag に null が代入されても SanitizeAfterLoad が "" に正規化する (NRE 防止)。
    /// </summary>
    [Fact]
    public void Settings_IgnoreUpdateTagNull_NormalizedToEmpty()
    {
        // Arrange: Reflection で non-nullable string プロパティに null を強制代入
        var settings = new Settings();
        typeof(Settings)
            .GetProperty(nameof(Settings.IgnoreUpdateTag))!
            .SetValue(settings, null);

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.NotNull(settings.IgnoreUpdateTag);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag の長さ境界 (256 / 257 / 巨大) に対する正規化挙動を網羅。
    /// 256 は保持、257 から破棄される off-by-one 検出。
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(255, true)]
    [InlineData(256, true)]   // ぎりぎり保持
    [InlineData(257, false)]  // 一文字超過で破棄
    [InlineData(100_000, false)]
    public void Settings_IgnoreUpdateTag_BoundaryLength_BehaviorMatrix(int length, bool shouldKeep)
    {
        // Arrange
        var tag = new string('v', length);
        var settings = new Settings { IgnoreUpdateTag = tag };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        if (shouldKeep)
            Assert.Equal(tag, settings.IgnoreUpdateTag);
        else
            Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag に制御文字 (\0〜\x1F) が含まれる場合は空に正規化される (ログインジェクション防御)。
    /// </summary>
    [Theory]
    [InlineData("v1.0.166\0", true)]
    [InlineData("v1.0.166\x01", true)]
    [InlineData("v1.0.166\x1F", true)]
    [InlineData("v1.0.166\r\n", true)]
    [InlineData("v1.0.166\t", true)]
    [InlineData("\x1Fv1.0.166", true)]
    [InlineData("v1.0.166 ", false)]  // 空白は制御範囲外、Trim される
    public void Settings_IgnoreUpdateTagControlCharacters_AlwaysCleared(string input, bool expectEmpty)
    {
        // Arrange
        var settings = new Settings { IgnoreUpdateTag = input };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        if (expectEmpty)
            Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
        else
            Assert.False(string.IsNullOrEmpty(settings.IgnoreUpdateTag));
    }

    /// <summary>
    /// サロゲートペア・RTL マーク・ゼロ幅スペースは制御範囲外なので保持される (過剰防御回避)。
    /// </summary>
    [Theory]
    [InlineData("v1.0.166🎯")]  // サロゲートペア (🎯)
    [InlineData("v1.0.166​")]         // ゼロ幅スペース
    [InlineData("v1.0.166‮")]         // RTL Override
    public void Settings_IgnoreUpdateTag_UnicodeNonControl_Preserved(string input)
    {
        // Arrange
        var settings = new Settings { IgnoreUpdateTag = input };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.Equal(input.Trim(), settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// UpdateChecker.TryBuildUpdateManager に null を渡すと ArgumentNullException がスローされる。
    /// </summary>
    [Fact]
    public void UpdateChecker_TryBuildUpdateManager_NullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => UpdateChecker.TryBuildUpdateManager(null!));
        Assert.Equal("settings", ex.ParamName);
    }

    // ────────────────────────────────────────────────────────────────
    // 🎭 型パンチ (Type Punching)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 空 JSON `{}` は JsonSerializer で成功し全プロパティがデフォルト値になる。
    /// </summary>
    [Fact]
    public void Settings_EmptyJsonObject_AllPropertiesAtDefaults()
    {
        // Arrange
        const string json = "{}";

        // Act
        var s = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);

        // Assert
        Assert.NotNull(s);
        var defaults = new Settings();
        Assert.Equal(defaults.Theme, s!.Theme);
        Assert.Equal(defaults.IgnoreUpdateTag, s.IgnoreUpdateTag);
        Assert.Equal(defaults.Check4UpdatesOnStartup, s.Check4UpdatesOnStartup);
        Assert.Equal(defaults.UpdateChannel, s.UpdateChannel);
    }

    /// <summary>
    /// UpdateBaseUrl は [JsonIgnore] + getter-only でハードコード固定。
    /// JSON 経由で書き換え不可 (悪意ある攻撃者ホストへの誘導の防御)。
    /// </summary>
    [Fact]
    public void Settings_JsonInjectionForUpdateBaseUrl_IgnoredByJsonIgnore()
    {
        // Arrange
        const string json = """{"UpdateBaseUrl": "https://evil-attacker.example.com"}""";

        // Act
        var s = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);

        // Assert
        Assert.NotNull(s);
        Assert.Equal("https://lhamiel.nephilim.jp", s!.UpdateBaseUrl);

        // setter が物理的に存在しないことも保証
        var prop = typeof(Settings).GetProperty(nameof(Settings.UpdateBaseUrl));
        Assert.NotNull(prop);
        Assert.False(prop!.CanWrite);
    }

    /// <summary>
    /// LhamielUpdateStrings の全 getter が non-null string を返す型契約。
    /// Reflection でインターフェース実装プロパティを列挙し網羅検証。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_AllGetters_ReturnNonNullStringsViaReflection()
    {
        // Arrange
        var instance = LhamielUpdateStrings.Instance;
        var interfaceType = typeof(LhamielUpdateStrings).GetInterface("IUpdateDialogStrings");
        Assert.NotNull(interfaceType);

        var stringProps = interfaceType!.GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();
        Assert.NotEmpty(stringProps);

        // Act & Assert
        foreach (var prop in stringProps)
        {
            var value = prop.GetValue(instance);
            Assert.NotNull(value);
            Assert.IsType<string>(value);
            Assert.False(string.IsNullOrEmpty((string)value!),
                $"{prop.Name} が空/null を返した");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 🌪️ 環境異常 (Environmental Chaos)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turkish ロケール (tr-TR) で Settings.SanitizeAfterLoad の UpdateChannel 比較が
    /// OrdinalIgnoreCase で動くこと (Turkish I 問題退行検知)。
    /// UpdateChecker.TryBuildUpdateManager 自体は VelopackLocator 未初期化で投げるためここでは呼ばない。
    /// </summary>
    [Fact]
    public void Settings_TurkishLocale_PrereleaseChannelNormalizedCorrectly()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var settings = new Settings { UpdateChannel = "PRERELEASE" };

            // Act
            settings.SanitizeAfterLoad();

            // Assert: tr-TR でも canonical 小文字に正規化される
            Assert.Equal("prerelease", settings.UpdateChannel);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Settings JSON シリアライズが Invariant Culture で数値を出力すること (fr-FR / ar-SA 等で壊れない)。
    /// </summary>
    [Fact]
    public void Settings_JsonSerialize_InvariantAcrossLocales()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var loc in new[] { "fr-FR", "ar-SA", "ja-JP", "en-US" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(loc);

                var s = new Settings
                {
                    LogMaxSizeMB = 12345,
                    LogRetentionDays = 99,
                    ZipCompressionLevel = 7,
                };

                // Act
                var json = JsonSerializer.Serialize(s, AppJsonContext.Default.Settings);

                // Assert: Invariant 形式 (桁区切り無し)
                Assert.Contains("12345", json);
                Assert.DoesNotContain("12,345", json);
                Assert.DoesNotContain("12.345", json);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 🔀 状態遷移 (State Machine Abuse)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// SanitizeAfterLoad → ResetToDefaults と ResetToDefaults → SanitizeAfterLoad が同じ defaults に収束。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAndReset_OrderIndependent()
    {
        // Arrange: Path A
        var pathA = new Settings
        {
            Theme = "HackerGreen",
            UpdateChannel = "evil",
            CompressionFormat = "RAR",
            IgnoreUpdateTag = new string('x', 1024),
        };
        // Path B
        var pathB = new Settings
        {
            Theme = "HackerGreen",
            UpdateChannel = "evil",
            CompressionFormat = "RAR",
            IgnoreUpdateTag = new string('x', 1024),
        };

        // Act
        pathA.SanitizeAfterLoad();
        pathA.ResetToDefaults();

        pathB.ResetToDefaults();
        pathB.SanitizeAfterLoad();

        // Assert
        Assert.Equal("System", pathA.Theme);
        Assert.Equal("System", pathB.Theme);
        Assert.Equal("release", pathA.UpdateChannel);
        Assert.Equal("release", pathB.UpdateChannel);
        Assert.Equal("", pathA.IgnoreUpdateTag);
        Assert.Equal("", pathB.IgnoreUpdateTag);
    }

    /// <summary>
    /// Snapshot 取得後に元 Settings を変更しても snapshot 側が不変 (深コピー検証)。
    /// </summary>
    [Fact]
    public void Settings_Snapshot_IsolatedFromSubsequentMutation()
    {
        // Arrange
        var original = new Settings
        {
            Theme = "Dark",
            ExcludedFilePatterns = new List<string> { "alpha", "beta" },
        };

        // Act
        var snap = original.Snapshot();
        original.Theme = "Light";
        original.ExcludedFilePatterns.Clear();
        original.ExcludedFilePatterns.Add("gamma");

        // Assert: snap1 は影響を受けない
        Assert.Equal("Dark", snap.Theme);
        Assert.Equal(new[] { "alpha", "beta" }, snap.ExcludedFilePatterns);
        Assert.NotSame(original.ExcludedFilePatterns, snap.ExcludedFilePatterns);
    }

    /// <summary>
    /// LhamielUpdateStrings.NotifyLocaleChanged は PropertyChanged 未購読でも NRE しない。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_NotifyLocaleChanged_WithoutSubscribers_DoesNotThrow()
    {
        // Arrange
        var instance = LhamielUpdateStrings.Instance;

        // Act
        var ex = Record.Exception(() => instance.NotifyLocaleChanged());

        // Assert
        Assert.Null(ex);
    }
}

/// <summary>
/// SettingsManager のシングルトン状態を触る嫌がらせテスト群。
/// %LocalAppData%\Lhamiel\settings.json に副作用があるため [Collection("Sequential")] で排他実行。
/// </summary>
[Collection("Sequential")]
public class VelopackIntegrationAdversarialSequentialTests
{
    // ────────────────────────────────────────────────────────────────
    // ⚡ 並行性 (Concurrency Chaos)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 100 並列スレッドが MutateAndSave を呼んでも全 Save が完走し最終状態が一貫。
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task SettingsManager_ConcurrentMutateAndSave_NoDataLoss()
    {
        // Arrange
        const int Threads = 100;
        var mgr = SettingsManager.Instance;
        var originalTag = mgr.Current.IgnoreUpdateTag;
        try
        {
            var barrier = new TaskCompletionSource();
            var exceptions = new ConcurrentBag<Exception>();
            var writtenValues = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, Threads).Select(i => Task.Run(async () =>
            {
                await barrier.Task;
                try
                {
                    var v = $"v1.0.{i:D4}";
                    mgr.MutateAndSave(s => s.IgnoreUpdateTag = v);
                    writtenValues.Add(v);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            // Act
            barrier.SetResult();
            await Task.WhenAll(tasks);

            // Assert
            Assert.Empty(exceptions);
            Assert.Equal(Threads, writtenValues.Count);
            // 最終値が「いずれかの書き込んだ値」と一致 (lost-update は許容)
            Assert.Contains(mgr.Current.IgnoreUpdateTag, writtenValues);
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalTag); } catch { }
        }
    }

    /// <summary>
    /// MutateAndSave 内で mutator が例外を投げた場合、lock が確実に解放され後続操作が完走する。
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task SettingsManager_MutateAndSaveThrowingMutator_LockReleased()
    {
        // Arrange
        var mgr = SettingsManager.Instance;
        var originalTag = mgr.Current.IgnoreUpdateTag;
        try
        {
            // Act
            Assert.Throws<InvalidOperationException>(() =>
                mgr.MutateAndSave(_ => throw new InvalidOperationException("mutator boom")));

            // Assert: lock が解放されていれば後続操作が即座に成功
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var followUp = Task.Run(() =>
            {
                mgr.Mutate(s => s.IgnoreUpdateTag = "after_throw");
            }, cts.Token);

            await followUp;
            Assert.Equal("after_throw", mgr.Current.IgnoreUpdateTag);
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalTag); } catch { }
        }
    }

    /// <summary>
    /// Mutate + CreateSnapshot を並列実行しても InvalidOperationException (列挙中変更) が発生しない。
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task SettingsManager_ConcurrentSnapshotAndMutate_NoEnumerationException()
    {
        // Arrange
        var mgr = SettingsManager.Instance;
        var originalPatterns = new List<string>(mgr.Current.ExcludedFilePatterns);
        try
        {
            using var stop = new CancellationTokenSource();
            var exceptions = new ConcurrentBag<Exception>();

            var mutator = Task.Run(() =>
            {
                var rng = new Random(42);
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        var sz = rng.Next(1, 32);
                        var list = Enumerable.Range(0, sz).Select(_ => Guid.NewGuid().ToString("N")).ToList();
                        mgr.Mutate(s => s.ExcludedFilePatterns = list);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            });

            var snapshotters = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    try
                    {
                        var snap = mgr.CreateSnapshot();
                        foreach (var p in snap.ExcludedFilePatterns) _ = p?.Length ?? 0;
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            })).ToArray();

            // Act
            await Task.WhenAll(snapshotters);
            stop.Cancel();
            await mutator;

            // Assert
            Assert.Empty(exceptions);
        }
        finally
        {
            try { mgr.Mutate(s => s.ExcludedFilePatterns = [.. originalPatterns]); } catch { }
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 💀 リソース枯渇 (Resource Exhaustion)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// IgnoreUpdateTag に 1MB の巨大文字列を入れても SanitizeAfterLoad が O(1) length check で即正規化 ("" に)。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAfterLoad_With1MBIgnoreUpdateTag_NormalizesToEmptyQuickly()
    {
        // Arrange
        var settings = new Settings
        {
            IgnoreUpdateTag = new string('A', 1_000_000)
        };

        // Act
        var sw = Stopwatch.StartNew();
        settings.SanitizeAfterLoad();
        sw.Stop();

        // Assert
        Assert.Equal("", settings.IgnoreUpdateTag);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"SanitizeAfterLoad が遅すぎる: {sw.Elapsed}");
    }

    /// <summary>
    /// ExcludedFilePatterns に 10000 件 (半分重複) を詰めても NormalizeExcludedFilePatterns が現実時間で完了。
    /// </summary>
    [Fact]
    public void Settings_NormalizeExcludedFilePatterns_With10000Items_DedupesUnderOneSecond()
    {
        // Arrange
        var input = new List<string>(10000);
        for (int i = 0; i < 5000; i++) input.Add($"pattern_{i}.tmp");
        for (int i = 0; i < 5000; i++) input.Add($"PATTERN_{i % 5000}.TMP"); // ケース違い重複

        // Act
        var sw = Stopwatch.StartNew();
        var result = Settings.NormalizeExcludedFilePatterns(input);
        sw.Stop();

        // Assert
        Assert.Equal(5000, result.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// MutateAndSave を 500 回連続呼び出ししても全て完走 (ファイル handle 枯渇しない)。
    /// </summary>
    [Fact(Timeout = 60000)]
    public void SettingsManager_MutateAndSave_Repeated500Times_CompletesAllWrites()
    {
        // Arrange
        var mgr = SettingsManager.Instance;
        var originalIgnoreTag = mgr.Current.IgnoreUpdateTag;

        try
        {
            // Act
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++)
            {
                int captured = i;
                mgr.MutateAndSave(s => s.IgnoreUpdateTag = $"v0.0.{captured % 256}");
            }
            sw.Stop();

            // Assert
            Assert.StartsWith("v0.0.", mgr.Current.IgnoreUpdateTag);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60),
                $"500 回の MutateAndSave が遅すぎる: {sw.Elapsed}");
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalIgnoreTag); } catch { }
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 🔀 状態遷移 (TempCleanup / StartupRegistration)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// TempCleanup が GUID 形式でない suffix を持つディレクトリは削除しない (ユーザーデータ誤削除防御)。
    /// </summary>
    [Fact]
    public void TempCleanup_NonGuidSuffix_NotDeleted()
    {
        // Arrange
        var tempRoot = Path.GetTempPath();
        var fakeDir = Path.Combine(tempRoot, "Lhamiel_Temp_NotAGuid_User_Data");
        Directory.CreateDirectory(fakeDir);
        Directory.SetLastWriteTimeUtc(fakeDir, DateTime.UtcNow.AddDays(-7));
        try
        {
            // Act
            TempCleanup.CleanupOrphanedTempDirectories();

            // Assert
            Assert.True(Directory.Exists(fakeDir),
                "GUID 形式でない末尾のディレクトリは削除されてはならない");
        }
        finally
        {
            try { if (Directory.Exists(fakeDir)) Directory.Delete(fakeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 30 分以内の新しい Lhamiel_Temp_{Guid} は誤削除されない (MinAge=30 分ガード)。
    /// </summary>
    [Fact]
    public void TempCleanup_FreshOrphanedDir_PreservedDueToMinAgeGuard()
    {
        // Arrange
        var tempRoot = Path.GetTempPath();
        var freshDir = Path.Combine(tempRoot, $"Lhamiel_Temp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(freshDir);
        // mtime は現在 (= MinAge 30 分未満)
        try
        {
            // Act
            TempCleanup.CleanupOrphanedTempDirectories();

            // Assert
            Assert.True(Directory.Exists(freshDir),
                "MinAge (30 分) 未満の Lhamiel_Temp ディレクトリは保護されるべき");
        }
        finally
        {
            try { if (Directory.Exists(freshDir)) Directory.Delete(freshDir, recursive: true); } catch { }
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
