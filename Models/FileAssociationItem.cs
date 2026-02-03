using CommunityToolkit.Mvvm.ComponentModel;
namespace Lhamiel.Models;

/// <summary>
/// ファイル関連付けの1項目（拡張子・表示名・関連付け状態）
/// </summary>
public partial class FileAssociationItem : ObservableObject
{
    /// <summary>
    /// ファイル拡張子（例: zip, 7z）
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// 表示用説明（例: ZIP (.zip)）
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// この形式を関連付けるかどうか
    /// </summary>
    [ObservableProperty]
    private bool _isAssociated;
}
