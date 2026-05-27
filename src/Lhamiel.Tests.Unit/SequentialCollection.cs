using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ファイルI/O等、逐次実行が必要なテストで使用するコレクション定義。
/// このコレクションに属するテストは並列実行を無効化する。
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection
{
}

/// <summary>
/// <c>ArchiveProcessor.MessageServiceImpl</c> / <c>UiDispatcherImpl</c> / <c>ConflictDialogImpl</c>
/// の <c>internal static</c> プロパティを差し替えるテスト用コレクション定義。
/// xUnit 3 では <see cref="CollectionAttribute"/> だけでは並列実行が無効化されないため、
/// 対応する <see cref="CollectionDefinitionAttribute"/> + <c>DisableParallelization=true</c> を
/// 明示する必要がある（RTK レビュー #D-002 対応）。
/// </summary>
[CollectionDefinition("ArchiveProcessor", DisableParallelization = true)]
public class ArchiveProcessorCollection
{
}
