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
    public void AnalyzeError_OperationCanceledException_NotMisclassifiedAsEncrypted()
    {
        // パスワード入力キャンセル経路は ArchiveExtractor で OperationCanceledException に
        // 変換され、上位 ArchiveProcessor の when (ex is not OperationCanceledException) で
        // AnalyzeError 経路から除外される。万一直接渡された場合も「パスワードが違います」
        // と誤分類されないことが重要なので、明示的に検証する。
        var ex = new OperationCanceledException("user cancelled");
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\locked.zip", @"C:\out");

        // 現状は switch の default 分岐に落ちて Unknown 分類される。
        // 専用 ArchiveErrorType.Cancelled を導入する場合はここを更新する。
        Assert.Equal(ArchiveErrorType.Unknown, info.ErrorType);
        // 回帰防止: EncryptedOrWrongPassword 誤分類が再発しないことを明示
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
        // 日本語 OS で CLR が例外メッセージを翻訳したケース
    // （v1.0.160 で追加 → 同 ver 取り下げ → 再リリースで再導入された日本語フォールバック）
        var ex = new InvalidOperationException(msg);
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\broken.zip", @"C:\out");
        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
    }

    // === IsCorruptedHResult ===

    [Theory]
    [InlineData(unchecked((int)0x80070017))] // ERROR_CRC
    [InlineData(unchecked((int)0x8007000D))] // ERROR_INVALID_DATA
    [InlineData(unchecked((int)0x8007000B))] // ERROR_BAD_FORMAT
    [InlineData(unchecked((int)0x80070570))] // ERROR_FILE_CORRUPT
    [InlineData(unchecked((int)0x80070571))] // ERROR_DISK_CORRUPT
    public void IsCorruptedHResult_WithCorruptionHResult_ReturnsTrue(int hResult)
    {
        Assert.True(ArchiveErrorHandler.IsCorruptedHResult(hResult));
    }

    [Theory]
    [InlineData(0)]                            // S_OK
    [InlineData(unchecked((int)0x80070070))]   // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070020))]   // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80004005))]   // E_FAIL
    public void IsCorruptedHResult_WithNonCorruptionHResult_ReturnsFalse(int hResult)
    {
        Assert.False(ArchiveErrorHandler.IsCorruptedHResult(hResult));
    }

    [Fact]
    public void AnalyzeError_InvalidOperationWithCrcHResult_ClassifiedAsCorruptedFile()
    {
        // メッセージに破損キーワードが無くても HResult で破損判定できる
        var ex = new InvalidOperationException("Unknown error occurred.");
        SetHResult(ex, unchecked((int)0x80070017)); // ERROR_CRC
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\test.7z", @"C:\out");
        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
    }

    [Fact]
    public void AnalyzeError_InvalidOperationWithFileCorruptHResult_ClassifiedAsCorruptedFile()
    {
        var ex = new InvalidOperationException("Some generic error.");
        SetHResult(ex, unchecked((int)0x80070570)); // ERROR_FILE_CORRUPT
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\test.zip", @"C:\out");
        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
    }

    [Fact]
    public void AnalyzeError_InvalidOperationWithGenericHResultAndNoKeyword_NotClassifiedAsCorrupted()
    {
        // HResult もメッセージもヒットしない場合は Unknown
        var ex = new InvalidOperationException("Something went wrong.");
        SetHResult(ex, unchecked((int)0x80004005)); // E_FAIL
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\test.zip", @"C:\out");
        Assert.Equal(ArchiveErrorType.Unknown, info.ErrorType);
    }

    private static void SetHResult(Exception ex, int hResult)
    {
        ex.HResult = hResult;
    }
}
