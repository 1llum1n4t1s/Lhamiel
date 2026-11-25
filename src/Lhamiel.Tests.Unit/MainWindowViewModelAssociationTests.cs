using Lhamiel.Models;
using Lhamiel.ViewModels;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>関連付けの適用結果の判定（レジストリや実際の関連付けには触れず、操作を差し替えて確認する）。</summary>
public class MainWindowViewModelAssociationTests
{
    private static FileAssociationItem Item(string extension, bool isAssociated)
        => new() { Extension = extension, IsAssociated = isAssociated };

    [Fact]
    public void SyncAssociations_ReturnsTrue_WhenAssociateFails()
    {
        var items = new[] { Item("zip", true) };

        var anyFailed = MainWindowViewModel.SyncAssociations(
            items, new Dictionary<string, bool> { ["zip"] = false }, false, _ => false, _ => true);

        Assert.True(anyFailed);
    }

    [Fact]
    public void SyncAssociations_ReturnsTrue_WhenDisassociateFails()
    {
        var items = new[] { Item("zip", false) };

        var anyFailed = MainWindowViewModel.SyncAssociations(
            items, new Dictionary<string, bool> { ["zip"] = true }, false, _ => true, _ => false);

        Assert.True(anyFailed);
    }

    [Fact]
    public void SyncAssociations_ReturnsFalse_AndAppliesOnlyDifferences_WhenAllSucceed()
    {
        var items = new[] { Item("zip", true), Item("7z", false), Item("lzh", true) };
        var status = new Dictionary<string, bool> { ["zip"] = false, ["7z"] = true, ["lzh"] = true };
        var associated = new List<string>();
        var disassociated = new List<string>();

        var anyFailed = MainWindowViewModel.SyncAssociations(
            items, status, false,
            e => { associated.Add(e); return true; },
            e => { disassociated.Add(e); return true; });

        Assert.False(anyFailed);
        Assert.Equal(["zip"], associated);
        Assert.Equal(["7z"], disassociated);
    }

    [Fact]
    public void SyncAssociations_ReassociatesAlreadyAssociated_WhenRefreshing()
    {
        var items = new[] { Item("zip", true) };
        var associated = new List<string>();

        MainWindowViewModel.SyncAssociations(
            items, new Dictionary<string, bool> { ["zip"] = true }, true,
            e => { associated.Add(e); return true; }, _ => true);

        Assert.Equal(["zip"], associated);
    }
}
