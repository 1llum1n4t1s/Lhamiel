using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="FolderOpener.GetExtractionFolderToOpen"/> の直接テスト。
/// 展開後に開くフォルダは「展開時スナップショット値 &gt; 呼び出し側の明示値」の順で決まり、
/// 現在設定へはフォールバックしない（親フォルダーを開く不具合 cbc95c8 の回帰）。
/// GetExtractionFolderToOpen はフォルダを開かない純粋な判定である。
/// </summary>
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
}
