using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// settings.json の読込み結果の区別（不存在 / 正常 / 破損 / 読取り不能）と、破損退避の段階、
/// 読めない間に既存設定を既定値で潰さない SettingsManager の保存を検証する。
/// 実ユーザーの設定へ触れないよう、パス指定の入口と注入した読み書きだけを使う。
/// </summary>
public sealed class SettingsLoadFailureTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        TestEnvironmentIsolation.IsolatedDirectory, "settings-load-" + Guid.NewGuid().ToString("N"));

    public SettingsLoadFailureTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (Exception) { /* 隔離フォルダーごと終了時に片付く */ }
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private string LegacyPath => Path.Combine(_directory, "legacy", "settings.json");

    private SettingsLoadResult Load() => Settings.LoadFromPath(SettingsPath, LegacyPath);

    private void WriteSavedSettings(string theme)
    {
        var settings = new Settings { Theme = theme, IgnoreUpdateTag = "keep-me" };
        settings.SaveTo(SettingsPath);
    }

    [Fact]
    public void LoadFromPath_MissingFile_CreatesDefaultsAsCreated()
    {
        var result = Load();

        Assert.Equal(SettingsLoadStatus.Created, result.Status);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void LoadFromPath_ValidFile_ReturnsLoadedValues()
    {
        WriteSavedSettings("Dark");

        var result = Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("Dark", result.Settings.Theme);
    }

    [Fact]
    public void LoadFromPath_ExclusivelyLockedFile_IsUnreadableAndKeepsFile()
    {
        WriteSavedSettings("Dark");
        var before = File.ReadAllText(SettingsPath);

        SettingsLoadResult result;
        var stopwatch = Stopwatch.StartNew();
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = Load();
        }
        stopwatch.Stop();

        Assert.Equal(SettingsLoadStatus.Unreadable, result.Status);
        Assert.IsAssignableFrom<IOException>(result.Error);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
        // 恒久的に読めなくても、毎回長く待たせない。
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"待機が長すぎます: {stopwatch.Elapsed}");
    }

    [Fact]
    public void LoadFromPath_TransientLockReleasedDuringRetry_LoadsNormally()
    {
        WriteSavedSettings("Dark");
        var holder = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);
        // スレッドプールの混雑に左右されないよう専用スレッドで解放し、開始を確認してから読み込む。
        using var started = new ManualResetEventSlim(false);
        var releaser = new Thread(() =>
        {
            started.Set();
            Thread.Sleep(20);
            holder.Dispose();
        })
        { IsBackground = true };
        releaser.Start();
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));

        var result = Load();
        releaser.Join();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("Dark", result.Settings.Theme);
    }

    [Fact]
    public void LoadFromPath_ReadDeniedByAcl_IsUnreadableAndKeepsFile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "ACL は Windows 限定");
        WriteSavedSettings("Dark");
        var before = File.ReadAllText(SettingsPath);
        var file = new FileInfo(SettingsPath);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(rule);
        file.SetAccessControl(security);
        try
        {
            var result = Load();

            Assert.Equal(SettingsLoadStatus.Unreadable, result.Status);
            Assert.IsType<UnauthorizedAccessException>(result.Error);
        }
        finally
        {
            security = file.GetAccessControl();
            security.RemoveAccessRule(rule);
            file.SetAccessControl(security);
        }
        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void LoadFromPath_PathIsDirectory_IsUnreadableAndWritesNothing()
    {
        Directory.CreateDirectory(SettingsPath);

        var result = Load();

        Assert.Equal(SettingsLoadStatus.Unreadable, result.Status);
        Assert.True(Directory.Exists(SettingsPath));
    }

    [Fact]
    public void LoadFromPath_CorruptJson_QuarantinesAndReportsCorruptReset()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var result = Load();

        Assert.Equal(SettingsLoadStatus.CorruptReset, result.Status);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt_*.bak"));
    }

    [Fact]
    public void QuarantineCorruptSettingsFile_MoveSucceeds_MovesToTimestampedBackup()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        var timestamp = new DateTime(2026, 11, 25, 1, 2, 3, 456);
        var backupPath = SettingsPath + ".corrupt_20261125010203_456.bak";

        var message = Settings.QuarantineCorruptSettingsFile(SettingsPath, timestamp, "元のエラー");

        Assert.False(File.Exists(SettingsPath));
        Assert.Equal("{ broken", File.ReadAllText(backupPath));
        Assert.Equal($"破損ファイルは {backupPath} に退避済みです", message);
    }

    [Fact]
    public void QuarantineCorruptSettingsFile_MoveFails_FallsBackToDelete()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        var timestamp = new DateTime(2026, 11, 25, 1, 2, 3, 456);
        // 退避先と同名のフォルダーがあると Move も Copy も失敗する。次の段（Delete）へ進むことを確かめる。
        Directory.CreateDirectory(SettingsPath + ".corrupt_20261125010203_456.bak");

        var message = Settings.QuarantineCorruptSettingsFile(SettingsPath, timestamp, "元のエラー");

        Assert.False(File.Exists(SettingsPath));
        Assert.Equal("破損ファイルを退避できなかったため削除しました（内容は保存されていません）", message);
    }

    private static SettingsLoadResult Result(Settings settings, SettingsLoadStatus status, Exception? error = null)
        => new(settings, status, error);

    private static SettingsLoadResult UnreadableResult()
        => Result(new Settings(), SettingsLoadStatus.Unreadable, new IOException("locked"));

    private sealed class Recorder
    {
        public List<Settings> Saved { get; } = [];

        public void Save(Settings settings) => Saved.Add(settings);
    }

    [Fact]
    public void SettingsManager_StartedUnreadable_RefusesToSaveAndKeepsMemoryUntouched()
    {
        var recorder = new Recorder();
        var manager = new SettingsManager(
            UnreadableResult(),
            () => Result(new Settings { Theme = "Dark" }, SettingsLoadStatus.Loaded),
            recorder.Save);

        Assert.Throws<InvalidOperationException>(() => manager.Save());
        Assert.Throws<InvalidOperationException>(() => manager.MutateAndSave(s => s.IgnoreUpdateTag = "x"));

        Assert.Empty(recorder.Saved);
        Assert.Equal("", manager.Current.IgnoreUpdateTag);
    }

    [Fact]
    public void SettingsManager_LatestBecomesUnreadable_AbortsWithoutApplyingMutation()
    {
        var recorder = new Recorder();
        var manager = new SettingsManager(
            Result(new Settings { Theme = "Dark" }, SettingsLoadStatus.Loaded),
            UnreadableResult,
            recorder.Save);

        Assert.Throws<InvalidOperationException>(() => manager.MutateAndSave(s => s.IgnoreUpdateTag = "x"));
        Assert.Throws<InvalidOperationException>(() => manager.Save());

        Assert.Empty(recorder.Saved);
        Assert.Equal("", manager.Current.IgnoreUpdateTag);
        Assert.Equal("Dark", manager.Current.Theme);
    }

    [Fact]
    public void SettingsManager_LatestReadable_SavesMemoryWithMutationAndKeepsWorkerPassword()
    {
        var recorder = new Recorder();
        byte[] workerPassword = [1, 2, 3];
        var manager = new SettingsManager(
            Result(new Settings(), SettingsLoadStatus.Loaded),
            () => Result(new Settings { EncryptedCompressionPassword = workerPassword }, SettingsLoadStatus.Loaded),
            recorder.Save);

        manager.MutateAndSave(s => s.IgnoreUpdateTag = "x");

        var saved = Assert.Single(recorder.Saved);
        Assert.Same(manager.Current, saved);
        Assert.Equal("x", saved.IgnoreUpdateTag);
        Assert.Equal(workerPassword, saved.EncryptedCompressionPassword);
    }

    [Fact]
    public void SettingsManager_Worker_UnreadableLatest_AbortsWithoutSaving()
    {
        var recorder = new Recorder();
        var manager = new SettingsManager(
            Result(new Settings(), SettingsLoadStatus.Loaded),
            UnreadableResult,
            recorder.Save,
            isArchiveWorker: true);
        var mutated = false;

        Assert.Throws<InvalidOperationException>(() => manager.MutateAndSave(_ => mutated = true));

        Assert.False(mutated);
        Assert.Empty(recorder.Saved);
    }

    [Fact]
    public void SettingsManager_Worker_ReadableLatest_SavesOnlyLatestWithMutation()
    {
        var recorder = new Recorder();
        var latest = new Settings { Theme = "Dark" };
        var manager = new SettingsManager(
            Result(new Settings { Theme = "Light" }, SettingsLoadStatus.Loaded),
            () => Result(latest, SettingsLoadStatus.Loaded),
            recorder.Save,
            isArchiveWorker: true);

        manager.MutateAndSave(s => s.EncryptedCompressionPassword = [9]);

        var saved = Assert.Single(recorder.Saved);
        Assert.Same(latest, saved);
        Assert.Equal("Dark", saved.Theme);
        Assert.Equal([9], manager.Current.EncryptedCompressionPassword);
    }

    [Fact]
    public void SettingsManager_Refresh_Unreadable_KeepsOldPassword()
    {
        byte[] password = [4, 5, 6];
        var manager = new SettingsManager(
            Result(new Settings { EncryptedCompressionPassword = password }, SettingsLoadStatus.Loaded),
            UnreadableResult,
            new Recorder().Save);

        manager.RefreshSavedCompressionPassword();

        Assert.Same(password, manager.Current.EncryptedCompressionPassword);
    }

    [Fact]
    public void SettingsManager_Refresh_Readable_TakesLatestPassword()
    {
        var manager = new SettingsManager(
            Result(new Settings { EncryptedCompressionPassword = [4] }, SettingsLoadStatus.Loaded),
            () => Result(new Settings { EncryptedCompressionPassword = [7] }, SettingsLoadStatus.Loaded),
            new Recorder().Save);

        manager.RefreshSavedCompressionPassword();

        Assert.Equal([7], manager.Current.EncryptedCompressionPassword);
    }
}
