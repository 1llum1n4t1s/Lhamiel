using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Velopack のインストール／アンインストールライフサイクル連携テスト。
/// </summary>
public class ProgramLifecycleTests
{
    [Fact]
    public void ShortcutIconRestorePending_RestoresOnceAndClearsMarker()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"Lhamiel-IconRestore-{Guid.NewGuid():N}");
        var markerPath = Path.Combine(tempDirectory, "pending");
        string? appliedVariant = null;
        try
        {
            Assert.True(Program.MarkShortcutIconRestorePending(markerPath));
            Assert.True(File.Exists(markerPath));

            Assert.True(Program.RestoreSelectedApplicationShortcutIconsIfPending(
                markerPath,
                variant => appliedVariant = variant,
                "crystal")); // 小文字の入力が正規化されて適用される

            Assert.Equal(Settings.AppIconVariantCrystal, appliedVariant);
            Assert.False(File.Exists(markerPath));
            Assert.False(Program.RestoreSelectedApplicationShortcutIconsIfPending(
                markerPath,
                _ => throw new InvalidOperationException("二重復元されました"),
                Settings.AppIconVariantClassic));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CleanupBeforeUninstall_RemovesStartupContextMenuAndFileAssociations()
    {
        var startupUnregisterCount = 0;
        var contextMenuRemoved = false;
        var fileAssociationsRemoved = false;
        var pendingRefreshCleared = false;

        Program.CleanupBeforeUninstall(
            () => startupUnregisterCount++,
            () =>
            {
                contextMenuRemoved = true;
                return true;
            },
            () =>
            {
                fileAssociationsRemoved = true;
                return true;
            },
            () => pendingRefreshCleared = true);

        Assert.Equal(1, startupUnregisterCount);
        Assert.True(contextMenuRemoved);
        Assert.True(fileAssociationsRemoved);
        Assert.True(pendingRefreshCleared);
    }

    private static string CreateMarker(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "pending");
        File.WriteAllText(path, "pending");
        return path;
    }

    [Fact]
    public void ShellContextMenuRefreshPending_AppliesCurrentSettingsAndClearsMarkerOnSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);
            (bool Extract, bool Compress)? applied = null;

            Assert.True(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, false), (e, c) => { applied = (e, c); return true; }));

            Assert.Equal((true, false), applied);
            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(marker + ".claimed"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_KeepsPendingWhenFailedOrUnreadableAndIgnoresMissingMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);
            var claimed = marker + ".claimed";
            var calls = 0;

            // 読めない間はマーカーを確保もしない。
            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, true, () => (true, true), (_, _) => { calls++; return true; }));
            Assert.True(File.Exists(marker));
            Assert.False(File.Exists(claimed));
            Assert.Equal(0, calls);

            // 失敗したときは確保したマーカーが残り、次回起動で再試行される。
            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => { calls++; return false; }));
            Assert.False(File.Exists(marker));
            Assert.True(File.Exists(claimed));
            Assert.Equal(1, calls);

            // 確保済みだけが残った状態から回収できる。
            Assert.True(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => { calls++; return true; }));
            Assert.False(File.Exists(claimed));
            Assert.Equal(2, calls);

            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => { calls++; return true; }));
            Assert.Equal(2, calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_ClaimedOnlyAndUnreadable_TouchesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = Path.Combine(directory, "pending");
            Directory.CreateDirectory(directory);
            File.WriteAllText(marker + ".claimed", "pending");

            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, true, () => (true, true), (_, _) => throw new InvalidOperationException("適用されました")));

            Assert.True(File.Exists(marker + ".claimed"));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_MarkerWrittenDuringApply_SurvivesForNextStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);

            Assert.True(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true),
                (_, _) =>
                {
                    // 適用中に更新フックが新しいマーカーを書く。
                    File.WriteAllText(marker, "pending");
                    return true;
                }));

            Assert.True(File.Exists(marker));
            Assert.False(File.Exists(marker + ".claimed"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_MarkerWrittenDuringFailedApply_KeepsBothAndNextRunCollectsThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);
            var claimed = marker + ".claimed";

            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true),
                (_, _) =>
                {
                    File.WriteAllText(marker, "pending");
                    return false;
                }));
            Assert.True(File.Exists(marker));
            Assert.True(File.Exists(claimed));

            // 両方ある場合は、新しい方を確保（上書き）して1回で回収する。
            var calls = 0;
            Assert.True(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => { calls++; return true; }));
            Assert.Equal(1, calls);
            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(claimed));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_WhenApplyThrows_KeepsClaimedMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);

            Assert.Throws<InvalidOperationException>(() => Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => throw new InvalidOperationException("失敗")));

            Assert.True(File.Exists(marker + ".claimed"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_WhenApplyIsBusy_KeepsClaimedMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        var previous = ShellContextMenu.AppliedInProcess;
        try
        {
            ShellContextMenu.AppliedInProcess = false;
            var marker = CreateMarker(directory);
            var result = true;
            // 別スレッドが適用ゲートを保持している間に回収すると、待たずに譲って保留を残す。
            ShellContextMenu.RefreshPendingRegistration(
                () => (true, true),
                (_, _) =>
                {
                    var other = new Thread(() => result = Program.RefreshPendingShellContextMenuRegistration(
                        marker, false, () => (true, true), (_, _) => true));
                    other.Start();
                    other.Join();
                    return true;
                });

            Assert.False(result);
            Assert.True(File.Exists(marker + ".claimed"));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            ShellContextMenu.AppliedInProcess = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_WhenClaimFails_DoesNotApplyAndKeepsMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);
            // 確保先にディレクトリがあると Move が失敗する。
            Directory.CreateDirectory(marker + ".claimed");

            Assert.False(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (true, true), (_, _) => throw new InvalidOperationException("適用されました")));

            Assert.True(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_ClearsBothMarkersOnSuccessfulHookAndUninstall()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        try
        {
            var marker = CreateMarker(directory);
            File.WriteAllText(marker + ".claimed", "pending");

            Program.DeleteShellContextMenuRefreshMarkers(marker);

            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(marker + ".claimed"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_WhileAnotherApplyIsRunning_YieldsWithoutWaiting()
    {
        var previous = ShellContextMenu.AppliedInProcess;
        try
        {
            ShellContextMenu.AppliedInProcess = false;
            var inner = ShellContextMenu.PendingRefreshResult.Applied;
            var outer = ShellContextMenu.RefreshPendingRegistration(
                () => (true, true),
                (_, _) =>
                {
                    // 適用中（ゲート保持中）に別スレッドから回収を試みると、待たずに譲る。
                    // Task.Run の待機は同じスレッドで実行され得て、ロックの再入になるため専用スレッドを使う。
                    var other = new Thread(() => inner = ShellContextMenu.RefreshPendingRegistration(
                        () => (true, true), (_, _) => true));
                    other.Start();
                    other.Join();
                    return true;
                });

            Assert.Equal(ShellContextMenu.PendingRefreshResult.Applied, outer);
            Assert.Equal(ShellContextMenu.PendingRefreshResult.Busy, inner);
        }
        finally
        {
            ShellContextMenu.AppliedInProcess = previous;
        }
    }

    [Fact]
    public void ShellContextMenuRefreshPending_AfterUserApplied_DoesNotOverwriteWithOldSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-MenuRefresh-{Guid.NewGuid():N}");
        var previous = ShellContextMenu.AppliedInProcess;
        try
        {
            var marker = CreateMarker(directory);
            ShellContextMenu.AppliedInProcess = true;

            Assert.True(Program.RefreshPendingShellContextMenuRegistration(
                marker, false, () => (false, false),
                (_, _) => throw new InvalidOperationException("古い設定で上書きされました")));

            Assert.False(File.Exists(marker));
        }
        finally
        {
            ShellContextMenu.AppliedInProcess = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewMutexName() => $@"Local\Lhamiel_Test_{Guid.NewGuid():N}";

    [Fact]
    public void RunSilentUpdate_DuringDownload_DoesNotHoldMutex()
    {
        var name = NewMutexName();
        var created = false;

        Program.RunSilentUpdate(name, () =>
        {
            using var other = new Mutex(true, name, out created);
            return null;
        }, () => false);

        Assert.True(created);
    }

    [Fact]
    public void RunSilentUpdate_MutexTakenByOtherThreadBeforeApply_DoesNotApply()
    {
        var name = NewMutexName();
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var applied = 0;
        var owner = new Thread(() =>
        {
            using var m = new Mutex(true, name, out _);
            acquired.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        try
        {
            Program.RunSilentUpdate(name, () =>
            {
                owner.Start();
                acquired.Wait();
                return () => applied++;
            }, () => false);
        }
        finally
        {
            release.Set();
            owner.Join();
        }

        Assert.Equal(0, applied);
    }

    [Fact]
    public void RunSilentUpdate_AnotherInstanceRunning_DoesNotApply()
    {
        var applied = 0;

        Program.RunSilentUpdate(NewMutexName(), () => () => applied++, () => true);

        Assert.Equal(0, applied);
    }

    [Fact]
    public void RunSilentUpdate_Normal_AppliesOnceWhileHoldingMutexAndReleasesAfter()
    {
        var name = NewMutexName();
        var applied = 0;
        var heldDuringApply = false;

        Program.RunSilentUpdate(name, () => () =>
        {
            applied++;
            heldDuringApply = Mutex.TryOpenExisting(name, out var m);
            m?.Dispose();
        }, () => false);

        Assert.Equal(1, applied);
        Assert.True(heldDuringApply);
        Assert.False(Mutex.TryOpenExisting(name, out var after));
        after?.Dispose();
    }

    [Fact]
    public void RunSilentUpdate_MutexAlreadyExists_DoesNotCheckOrDownload()
    {
        var name = NewMutexName();
        using var ui = new Mutex(true, name, out _);
        var checkCount = 0;

        Program.RunSilentUpdate(name, () =>
        {
            checkCount++;
            return null;
        }, () => false);

        Assert.Equal(0, checkCount);
        ui.ReleaseMutex();
    }
}
