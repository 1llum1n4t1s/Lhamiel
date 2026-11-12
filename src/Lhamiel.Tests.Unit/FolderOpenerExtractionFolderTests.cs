using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="FolderOpener.GetExtractionFolderToOpen"/> の直接テスト。
/// 展開後に開くフォルダは「展開時スナップショット値 &gt; 呼び出し側の明示値」の順で決まり、
/// 現在設定へはフォールバックしない。二重ネスト防止でフォルダ作成をスキップした場合だけ、
/// 実在するアーカイブのルートフォルダーを開く。
/// </summary>
/// <remarks>
/// GetExtractionFolderToOpen はフォルダを開かない純粋な判定なので DryRun は不要だが、
/// FolderOpener の static 状態に触れる他テストと並走しないよう同じコレクションに入れる。
/// </remarks>
[Collection("FolderOpener")]
public sealed class FolderOpenerExtractionFolderTests : IDisposable
{
    private const string RootName = "ArchiveRoot";

    // 一時ディレクトリの削除は共通ヘルパー TestDirectory の Dispose（best-effort）に任せる。
    private readonly TestDirectory _temp = TestDirectory.Create("Lhamiel_open_folder");

    public void Dispose() => _temp.Dispose();

    /// <summary>単一ルートフォルダーを実在させた展開先を返す。</summary>
    private string CreateOutputWithRoot()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, RootName));
        return _temp.Path;
    }

    private static ArchiveExtractor.ArchiveStructureInfo SkipInfo(bool? captured) => new()
    {
        ShouldSkipFolderCreation = true,
        SingleRootItemName = RootName,
        CapturedCreateArchiveNameFolder = captured,
    };

    [Fact]
    public void SnapshotTrue_OverridesArgumentFalse_OpensRootFolder()
    {
        var output = CreateOutputWithRoot();

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: true), createArchiveNameFolder: false);

        Assert.Equal(Path.Combine(output, RootName), result);
    }

    [Fact]
    public void SnapshotFalse_OverridesArgumentTrue_OpensOutputPath()
    {
        var output = CreateOutputWithRoot();

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: false), createArchiveNameFolder: true);

        Assert.Equal(output, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SnapshotNull_ArgumentDecides(bool createArchiveNameFolder)
    {
        var output = CreateOutputWithRoot();

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: null), createArchiveNameFolder);

        Assert.Equal(createArchiveNameFolder ? Path.Combine(output, RootName) : output, result);
    }

    [Fact]
    public void SkipAndRootFolderExists_OpensRootFolder()
    {
        var output = CreateOutputWithRoot();

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: true), createArchiveNameFolder: true);

        Assert.Equal(Path.Combine(output, RootName), result);
    }

    [Fact]
    public void SkipButRootFolderMissing_OpensOutputPath()
    {
        // ルートフォルダーを作らない（展開が途中で失敗した・ルートがファイルだった等）。
        var output = _temp.Path;

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: true), createArchiveNameFolder: true);

        Assert.Equal(output, result);
    }

    [Fact]
    public void SkipButRootIsFile_OpensOutputPath()
    {
        // 単一ルート項目がファイルの場合、Directory.Exists が false なので展開先を開く。
        File.WriteAllText(Path.Combine(_temp.Path, RootName), "x");

        var result = FolderOpener.GetExtractionFolderToOpen(_temp.Path, SkipInfo(captured: true), createArchiveNameFolder: true);

        Assert.Equal(_temp.Path, result);
    }

    [Fact]
    public void SettingOff_OpensOutputPathEvenIfRootExists()
    {
        var output = CreateOutputWithRoot();

        var result = FolderOpener.GetExtractionFolderToOpen(output, SkipInfo(captured: null), createArchiveNameFolder: false);

        Assert.Equal(output, result);
    }

    [Fact]
    public void NotSkipped_OpensOutputPath()
    {
        var output = CreateOutputWithRoot();
        var info = new ArchiveExtractor.ArchiveStructureInfo
        {
            ShouldSkipFolderCreation = false,
            SingleRootItemName = RootName,
            CapturedCreateArchiveNameFolder = true,
        };

        var result = FolderOpener.GetExtractionFolderToOpen(output, info, createArchiveNameFolder: true);

        Assert.Equal(output, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptySingleRootItemName_OpensOutputPath(string? rootName)
    {
        var output = CreateOutputWithRoot();
        var info = new ArchiveExtractor.ArchiveStructureInfo
        {
            ShouldSkipFolderCreation = true,
            SingleRootItemName = rootName,
            CapturedCreateArchiveNameFolder = true,
        };

        var result = FolderOpener.GetExtractionFolderToOpen(output, info, createArchiveNameFolder: true);

        Assert.Equal(output, result);
    }

    [Fact]
    public void NullStructureInfo_OpensOutputPath()
    {
        var output = CreateOutputWithRoot();

        Assert.Equal(output, FolderOpener.GetExtractionFolderToOpen(output, structureInfo: null, createArchiveNameFolder: true));
        Assert.Equal(output, FolderOpener.GetExtractionFolderToOpen(output, structureInfo: null, createArchiveNameFolder: false));
    }
}
