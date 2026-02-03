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
