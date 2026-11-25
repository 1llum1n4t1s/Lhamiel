using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
using LzhArchiveErrorKind = Kagayoi.UnLhaRe.ArchiveErrorKind;
using LzhArchiveNativeException = Kagayoi.UnLhaRe.ArchiveNativeException;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveErrorHandler の分類ロジック、特に v1.0.159 で追加された
/// パスワード保護アーカイブ関連のエラー経路を検証する。
/// </summary>
public class ArchiveErrorHandlerTests
{
    [Theory]
    [InlineData(LzhArchiveErrorKind.Format)]
    [InlineData(LzhArchiveErrorKind.InvalidPath)]
    [InlineData(LzhArchiveErrorKind.Exists)]
    public void AnalyzeError_UnLhaReArchiveFailure_ReturnsCorruptedFileType(
        LzhArchiveErrorKind kind)
    {
        var ex = new LzhArchiveNativeException(-1, kind, "native archive failure");

        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\broken.lzh", @"C:\out");

        Assert.Equal(ArchiveErrorType.CorruptedFile, info.ErrorType);
        Assert.False(info.IsRecoverable);
    }

    [Fact]
    public void AnalyzeError_UnLhaReLimit_ReturnsResourceLimitType()
    {
        var ex = new LzhArchiveNativeException(-1, LzhArchiveErrorKind.Limit, "entry limit exceeded");

        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\large.lzh", @"C:\out");

        Assert.Equal(ArchiveErrorType.ResourceLimitExceeded, info.ErrorType);
        Assert.Equal(App.Text("ErrorHandler.ResourceLimit"), info.Message);
        Assert.Equal("entry limit exceeded", info.Details);
        Assert.False(info.IsRecoverable);
    }

    [Fact]
    public void AnalyzeError_UnLhaReIo_ReturnsLocalizedIoErrorWithoutAssumingDiskFull()
    {
        var ex = new LzhArchiveNativeException(-1, LzhArchiveErrorKind.Io, "source disappeared");

        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\input.lzh", @"C:\out");

        Assert.Equal(ArchiveErrorType.Unknown, info.ErrorType);
        Assert.Equal(App.Text("ErrorHandler.IOError"), info.Message);
        Assert.NotEqual(ArchiveErrorType.InsufficientDiskSpace, info.ErrorType);
        Assert.True(info.IsRecoverable);
    }

    [Theory]
    [InlineData(LzhArchiveErrorKind.Format)]
    [InlineData(LzhArchiveErrorKind.InvalidPath)]
    [InlineData(LzhArchiveErrorKind.Exists)]
    public void AnalyzeLzhCreateError_InputOrOutputFailure_IsNotReportedAsCorruptedArchive(
        LzhArchiveErrorKind kind)
    {
        var ex = new LzhArchiveNativeException(-1, kind, "create failed");

        var info = ArchiveErrorHandler.AnalyzeLzhCreateError(ex, @"C:\output.lzh");

        Assert.Equal(ArchiveErrorType.Unknown, info.ErrorType);
        Assert.Equal(App.Text("ErrorHandler.InvalidOperation"), info.Message);
        Assert.NotEqual(App.Text("ErrorHandler.Corrupted"), info.Message);
        Assert.False(info.IsRecoverable);
    }

    [Fact]
    public void AnalyzeLzhCreateError_UnLhaReLimit_UsesLocalizedClassification()
    {
        var ex = new LzhArchiveNativeException(-1, LzhArchiveErrorKind.Limit, "entry limit exceeded");

        var info = ArchiveErrorHandler.AnalyzeLzhCreateError(ex, @"C:\output.lzh");

        Assert.Equal(ArchiveErrorType.ResourceLimitExceeded, info.ErrorType);
        Assert.Equal(App.Text("ErrorHandler.ResourceLimit"), info.Message);
        Assert.Equal("entry limit exceeded", info.Details);
        Assert.Equal(App.Text("ErrorHandler.ResourceLimitAction"), info.RecommendedAction);
        Assert.False(info.IsRecoverable);
    }

    [Fact]
    public void AnalyzeError_EncryptionException_ReturnsEncryptedOrWrongPasswordType()
    {
        // EncryptionException は IOException を継承しているため、IOException ケースより先に
        // マッチしなければ本来の案内（「パスワードが必要」）ではなく「I/O エラー」になってしまう。
        var ex = new EncryptionException();
        var info = ArchiveErrorHandler.AnalyzeError(ex, @"C:\test.zip", @"C:\out");

        Assert.Equal(ArchiveErrorType.EncryptedOrWrongPassword, info.ErrorType);
        Assert.NotEqual(ArchiveErrorType.Unknown, info.ErrorType);
        Assert.True(info.IsRecoverable, "パスワード誤入力は再試行で回復可能なので IsRecoverable は true");
        Assert.Same(ex, info.OriginalException);
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

    // === HResult による破損判定 ===

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
