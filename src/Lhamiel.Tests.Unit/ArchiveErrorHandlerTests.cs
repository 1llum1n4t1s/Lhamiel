using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveErrorHandler の分類ロジック、特に v1.0.159 で追加された
/// パスワード保護アーカイブ関連のエラー経路を検証する。
/// </summary>
public class ArchiveErrorHandlerTests
{
    [Fact]
    public void AnalyzeError_EncryptionException_ReturnsEncryptedOrWrongPasswordType()
    {
        var ex = new EncryptionException();
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\test.zip", @"C:\out");

        Assert.Equal(ArchiveErrorType.EncryptedOrWrongPassword, info.ErrorType);
        Assert.True(info.IsRecoverable, "パスワード誤入力は再試行で回復可能なので IsRecoverable は true");
        Assert.Same(ex, info.OriginalException);
    }

    [Fact]
    public void AnalyzeError_EncryptionExceptionBeforeIOException_DoesNotFallThrough()
    {
        // EncryptionException は IOException を継承しているため、IOException ケースより先に
        // マッチしなければ本来の案内（「パスワードが必要」）ではなく「I/O エラー」になってしまう。
        var ex = new EncryptionException();
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\secret.zip", @"C:\out");

        Assert.Equal(ArchiveErrorType.EncryptedOrWrongPassword, info.ErrorType);
        Assert.NotEqual(ArchiveErrorType.Unknown, info.ErrorType);
    }

    [Fact]
    public void AnalyzeError_OperationCanceledException_ClassifiedAsCancellation()
    {
        // パスワード入力キャンセル経路は ArchiveExtractor で OperationCanceledException に
        // 変換されるため、ArchiveErrorHandler 側ではキャンセルとして扱われることを確認。
        var ex = new OperationCanceledException("user cancelled");
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\locked.zip", @"C:\out");

        // EncryptedOrWrongPassword にはならない（キャンセルの伝播）
        Assert.NotEqual(ArchiveErrorType.EncryptedOrWrongPassword, info.ErrorType);
    }

    [Theory]
    [InlineData("File is corrupted")]
    [InlineData("Data is damaged")]
    [InlineData("Invalid archive format")]
    [InlineData("CRC mismatch")]
    [InlineData("Checksum error")]
    public void AnalyzeError_EnglishCorruptionKeyword_ClassifiedAsCorruptedFile(string msg)
    {
        var ex = new InvalidOperationException(msg);
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\broken.zip", @"C:\out");
        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
    }

    [Theory]
    [InlineData("ファイルが破損しています")]
    [InlineData("アーカイブが壊れています")]
    [InlineData("無効なデータ形式です")]
    [InlineData("チェックサムが一致しません")]
    public void AnalyzeError_JapaneseCorruptionKeyword_ClassifiedAsCorruptedFile(string msg)
    {
        // 日本語 OS で CLR が例外メッセージを翻訳したケース（v1.0.160 で追加された日本語フォールバック）
        var ex = new InvalidOperationException(msg);
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\broken.zip", @"C:\out");
        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
    }
}
