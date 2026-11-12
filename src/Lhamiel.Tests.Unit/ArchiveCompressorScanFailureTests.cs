using System.Security.AccessControl;
using System.Security.Principal;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// 圧縮スキャン（<see cref="ArchiveCompressor.ScanSourceFiles"/>）で列挙できなかったフォルダーの扱いを検証する。
/// <para>
/// 契約: 列挙に失敗したフォルダーは黙って 0 件扱いにせず、失敗件数を呼び出し元へ返して
/// アクセス不能スキップ数（パスワード保護時に利用者へ通知する値）に合算する。
/// また、ルート自体を列挙できなかった場合は IncludeRoot の「空フォルダーとしてルートを追加」を行わず、
/// 全入力がアクセス不能なら空アーカイブを公開せずに失敗させる。
/// </para>
/// <para>
/// アクセス拒否は、テスト用フォルダーに現在ユーザーの「フォルダーの一覧表示」拒否 ACE を付けて再現する。
/// 後始末で必ず ACE を外してから削除する。
/// </para>
/// </summary>
public sealed class ArchiveCompressorScanFailureTests
{
    /// <summary>フォルダーの一覧表示を現在ユーザーに拒否する ACE。</summary>
    private static FileSystemAccessRule CreateDenyListRule() => new(
        WindowsIdentity.GetCurrent().User!,
        FileSystemRights.ListDirectory,
        InheritanceFlags.None,
        PropagationFlags.None,
        AccessControlType.Deny);

    /// <summary><paramref name="directory"/> の一覧表示を拒否した状態で <paramref name="body"/> を実行し、必ず元に戻す。</summary>
    private static async Task WithListDeniedAsync(string directory, Func<Task> body)
    {
        var info = new DirectoryInfo(directory);
        var rule = CreateDenyListRule();
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            await body();
        }
        finally
        {
            var restore = info.GetAccessControl();
            restore.RemoveAccessRule(rule);
            info.SetAccessControl(restore);
        }
    }

    /// <summary>テスト用の一時ルートを作り、終了時に削除する。</summary>
    private static async Task WithTempRootAsync(Func<string, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"lhamiel_scanfail_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await body(root);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public Task ScanSourceFiles_RootListDenied_IncludeRoot_DoesNotAddRootAsEmptyFolder()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ACL によるアクセス拒否は Windows 限定");
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Denied");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "secret.txt"), "x");

            await WithListDeniedAsync(source, async () =>
            {
                var failures = new ArchiveCompressor.ScanFailureCounter();
                var result = await ArchiveCompressor.ScanSourceFiles(
                    [source], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                    dirModeOverride: DirectoryStructureMode.IncludeRoot,
                    scanFailures: failures);

                // 旧実装: IgnoreInaccessible=true で黙って 0 件になり、ルートが「空フォルダー」として
                // 追加されるため addedCount==0 ガードを通過して空フォルダーだけのアーカイブが公開され得た。
                Assert.Empty(result);
                Assert.Equal(1, failures.Count);
            });
        });
    }

    [Fact]
    public Task ScanSourceFiles_SubfolderListDenied_CountsFailureAndKeepsOtherFiles()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ACL によるアクセス拒否は Windows 限定");
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Source");
            var denied = Path.Combine(source, "denied");
            Directory.CreateDirectory(denied);
            File.WriteAllText(Path.Combine(source, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(denied, "hidden-from-scan.txt"), "x");

            await WithListDeniedAsync(denied, async () =>
            {
                var failures = new ArchiveCompressor.ScanFailureCounter();
                var result = await ArchiveCompressor.ScanSourceFiles(
                    [source], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                    dirModeOverride: DirectoryStructureMode.IncludeRoot,
                    scanFailures: failures);

                Assert.Contains(result, r => r.relativePath.EndsWith("keep.txt", StringComparison.Ordinal));
                Assert.DoesNotContain(result, r => r.relativePath.Contains("hidden-from-scan", StringComparison.Ordinal));
                // 中身が不明なフォルダーを「空フォルダー」エントリとして出力しない。
                Assert.DoesNotContain(result, r => r.relativePath.TrimEnd('/').EndsWith("denied", StringComparison.Ordinal));
                // 列挙できなかったフォルダーは黙って捨てず、失敗として数える。
                Assert.Equal(1, failures.Count);
            });
        });
    }

    [Fact]
    public Task ScanSourceFiles_NoFailures_ReportsZero()
    {
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Source");
            Directory.CreateDirectory(Path.Combine(source, "sub"));
            File.WriteAllText(Path.Combine(source, "sub", "a.txt"), "x");

            var failures = new ArchiveCompressor.ScanFailureCounter();
            var result = await ArchiveCompressor.ScanSourceFiles(
                [source], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                scanFailures: failures);

            Assert.Contains(result, r => r.relativePath.EndsWith("a.txt", StringComparison.Ordinal));
            Assert.Equal(0, failures.Count);
        });
    }

    [Fact]
    public Task ScanSourceFiles_EmptyRootFolder_IncludeRoot_StillAddsRootEntry()
    {
        // 回帰防止（codex P2 #3384620482）: 列挙に成功した本当に空のフォルダーは従来どおり
        // ルートを空フォルダーエントリとして追加する。
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Empty");
            Directory.CreateDirectory(source);

            var failures = new ArchiveCompressor.ScanFailureCounter();
            var result = await ArchiveCompressor.ScanSourceFiles(
                [source], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                scanFailures: failures);

            Assert.Single(result);
            Assert.Equal("Empty/", result[0].relativePath);
            Assert.Equal(0, failures.Count);
        });
    }

    [Fact]
    public Task CompressFilesAsync_RootListDenied_FailsWithoutPublishingArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ACL によるアクセス拒否は Windows 限定");
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Denied");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "secret.txt"), "x");
            var output = Path.Combine(temp, "out.zip");
            var settings = new Settings { DirectoryStructureMode = DirectoryStructureMode.IncludeRoot };

            await WithListDeniedAsync(source, async () =>
            {
                // 「全入力がアクセス不能なら失敗」の契約: 空フォルダーだけのアーカイブを公開しない。
                await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveCompressor.CompressFilesAsync(
                    [source], output, Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken,
                    settingsOverride: settings));
            });

            Assert.False(File.Exists(output), "アクセス不能な入力から出力が公開された");
        });
    }

    [Fact]
    public Task CompressFilesAsync_PartiallyDenied_PublishesReadableFilesAndReturnsScanFailureInSkipCount()
    {
        // 一部成功の経路: 読めるファイルはアーカイブに入り、列挙できなかったフォルダーは
        // 戻り値のスキップ数（パスワード保護時に利用者へ通知する値）に合算される。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ACL によるアクセス拒否は Windows 限定");
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Source");
            var denied = Path.Combine(source, "denied");
            Directory.CreateDirectory(denied);
            File.WriteAllText(Path.Combine(source, "keep.txt"), "x");
            File.WriteAllText(Path.Combine(denied, "secret.txt"), "x");
            var output = Path.Combine(temp, "out.zip");
            var settings = new Settings { DirectoryStructureMode = DirectoryStructureMode.IncludeRoot };

            var skipped = -1;
            await WithListDeniedAsync(denied, async () =>
            {
                skipped = await ArchiveCompressor.CompressFilesAsync(
                    [source], output, Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken,
                    settingsOverride: settings);
            });

            Assert.True(File.Exists(output), "読めるファイルがあるのに出力が公開されなかった");
            Assert.Equal(1, skipped);
        });
    }

    [Fact]
    public Task CompressFilesAsync_OnlyDeniedSubfolder_FailsWithoutPublishingArchive()
    {
        // ルートは読めるが中身が列挙不能なサブフォルダーだけの場合も、ルートやサブフォルダーを
        // 「空フォルダー」として出力せず、全入力アクセス不能として失敗させる。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ACL によるアクセス拒否は Windows 限定");
        return WithTempRootAsync(async temp =>
        {
            var source = Path.Combine(temp, "Source");
            var denied = Path.Combine(source, "denied");
            Directory.CreateDirectory(denied);
            File.WriteAllText(Path.Combine(denied, "secret.txt"), "x");
            var output = Path.Combine(temp, "out.zip");
            var settings = new Settings { DirectoryStructureMode = DirectoryStructureMode.IncludeRoot };

            await WithListDeniedAsync(denied, async () =>
            {
                var failures = new ArchiveCompressor.ScanFailureCounter();
                var scanned = await ArchiveCompressor.ScanSourceFiles(
                    [source], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                    dirModeOverride: DirectoryStructureMode.IncludeRoot,
                    scanFailures: failures);
                Assert.Empty(scanned);
                Assert.Equal(1, failures.Count);

                await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveCompressor.CompressFilesAsync(
                    [source], output, Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken,
                    settingsOverride: settings));
            });

            Assert.False(File.Exists(output), "アクセス不能な入力から出力が公開された");
        });
    }
}
