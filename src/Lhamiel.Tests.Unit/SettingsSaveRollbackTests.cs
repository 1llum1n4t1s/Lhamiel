using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// 設定を読めないセッションの公開状態と、保存の失敗・中止時に SettingsManager がロック内で
/// メモリ上の設定を変更前へ戻すこと、中止メッセージが理由だけ（接頭辞なし）であることを検証する。
/// 実ユーザーの設定へ触れないよう、注入した読み書きだけを使う。
/// </summary>
public sealed class SettingsSaveRollbackTests
{
    private static SettingsLoadResult Result(Settings settings, SettingsLoadStatus status, Exception? error = null)
        => new(settings, status, error);

    private static SettingsLoadResult Loaded() => Result(new Settings(), SettingsLoadStatus.Loaded);

    private static SettingsLoadResult Unreadable()
        => Result(new Settings(), SettingsLoadStatus.Unreadable, new IOException("locked: C:\\Users\\someone\\settings.json"));

    /// <summary>設定プロパティそれぞれを既定値と異なる値へ変える（CopyFrom の網羅漏れを検出するため全プロパティを対象にする）。</summary>
    private static void ChangeEveryProperty(Settings settings)
    {
        foreach (var property in typeof(Settings).GetProperties())
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                continue;

            object? changed = property.GetValue(settings) switch
            {
                string s => s + "-changed",
                bool b => !b,
                int i => i + 1,
                string[] => new[] { "changed-1", "changed-2" },
                byte[] => new byte[] { 9, 8, 7 },
                null when property.PropertyType == typeof(byte[]) => new byte[] { 9, 8, 7 },
                Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(v => !v.Equals(e)),
                _ => throw new NotSupportedException($"未対応の設定プロパティ型: {property.Name} ({property.PropertyType})"),
            };
            property.SetValue(settings, changed);
        }
    }

    private static Dictionary<string, string> Describe(Settings settings)
        => typeof(Settings).GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToDictionary(
                p => p.Name,
                p => p.GetValue(settings) switch
                {
                    string[] array => string.Join("|", array),
                    byte[] bytes => Convert.ToHexString(bytes),
                    var value => value?.ToString() ?? "(null)",
                });

    [Fact]
    public void MutateAndSave_WriteFails_RestoresEveryPropertyAndKeepsReference()
    {
        var manager = new SettingsManager(
            Loaded(),
            Loaded,
            _ => throw new IOException("disk full"));
        var current = manager.Current;
        var before = Describe(current);

        Assert.Throws<IOException>(() => manager.MutateAndSave(ChangeEveryProperty));

        Assert.Same(current, manager.Current);
        Assert.Equal(before, Describe(manager.Current));
    }

    [Fact]
    public void MutateAndSave_WriteSucceeds_KeepsMutation()
    {
        var manager = new SettingsManager(Loaded(), Loaded, _ => { });

        manager.MutateAndSave(ChangeEveryProperty);

        Assert.NotEqual(Describe(new Settings()), Describe(manager.Current));
    }

    [Fact]
    public void Save_WriteFails_KeepsPendingUiMutation()
    {
        var manager = new SettingsManager(Loaded(), Loaded, _ => throw new IOException("disk full"));
        manager.Mutate(s => s.Theme = "Dark");

        Assert.Throws<IOException>(() => manager.Save());

        Assert.Equal("Dark", manager.Current.Theme);
    }

    [Fact]
    public void StartedWithUnreadableSettings_ReflectsInitialStatus()
    {
        Assert.True(new SettingsManager(Unreadable(), Loaded, _ => { }).StartedWithUnreadableSettings);
        Assert.False(new SettingsManager(Loaded(), Loaded, _ => { }).StartedWithUnreadableSettings);
    }

    [Fact]
    public void SaveAborted_Messages_AreLocalizedReasonsWithoutPrefixOrPaths()
    {
        var startedUnreadable = new SettingsManager(Unreadable(), Loaded, _ => { });
        var startup = Assert.Throws<InvalidOperationException>(() => startedUnreadable.MutateAndSave(_ => { }));
        Assert.Equal(App.Text("Error.SaveAbortedStartupUnreadable"), startup.Message);

        var becameUnreadable = new SettingsManager(Loaded(), Unreadable, _ => { });
        var file = Assert.Throws<InvalidOperationException>(() => becameUnreadable.MutateAndSave(_ => { }));
        Assert.Equal(App.Text("Error.SaveAbortedFileUnreadable"), file.Message);
        Assert.DoesNotContain("someone", file.Message);
        Assert.IsType<IOException>(file.InnerException);

        // 接頭辞は表示側が1回だけ付けるため、例外メッセージ自体には含まれない。
        var prefixOnly = App.Text("Error.SaveSettingsFailed", "").TrimEnd(' ', ':', '：');
        Assert.DoesNotContain(prefixOnly, startup.Message);
        Assert.DoesNotContain(prefixOnly, file.Message);
    }
}
