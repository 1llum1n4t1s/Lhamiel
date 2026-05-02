using Cube.FileSystem.SevenZip;
namespace Lhamiel.Util;

/// <summary>
/// 展開後のアーカイブ整合性検証。
/// <see cref="ArchiveReaderExtension.Test(ArchiveReader)"/> で 7z.dll 内部の CRC 検証を実行し、
/// アーカイブが破損していないことを確認する。
/// </summary>
internal static class ArchiveIntegrityVerifier
{
    internal record VerificationResult(
        bool IsValid,
        string? ErrorMessage = null);

    /// <summary>
    /// アーカイブの整合性を検証する（展開完了後に呼び出すことを想定）。
    /// 7z.dll の Test モードで全エントリをメモリ上に展開し、CRC 検証を行う。
    /// ディスクには書き込まない。
    /// </summary>
    internal static async Task<VerificationResult> VerifyArchiveAsync(
        string archivePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(archivePath))
            return new VerificationResult(false, App.Text("ErrorHandler.FileNotFound"));

        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var reader = LockedFileRetryPolicy.Execute(() => new ArchiveReader(archivePath), archivePath);

                // パスワード保護アーカイブはパスワードなしで Test() すると失敗するためスキップ。
                // ヘッダー暗号化(-mhe=on)の場合は reader.Items 自体がアクセス不可。
                bool hasEncryptedItems;
                try
                {
                    hasEncryptedItems = reader.Items.Any(item => item.Encrypted);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"アーカイブヘッダー読み取り失敗（ヘッダー暗号化の可能性）のため CRC 検証をスキップ: {archivePath}");
                    return new VerificationResult(true);
                }

                if (hasEncryptedItems)
                {
                    Logger.Log($"パスワード保護アーカイブのため CRC 検証をスキップ: {archivePath}");
                    return new VerificationResult(true);
                }

                reader.Test();
                return new VerificationResult(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"アーカイブ整合性検証失敗: {archivePath} - {ex.Message}", LogLevel.Warning);
                return new VerificationResult(false, ex.Message);
            }
        }, cancellationToken);
    }
}
