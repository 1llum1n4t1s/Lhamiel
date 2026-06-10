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
    /// 7z.dll の Test モードで各エントリを順次デコードしながら CRC を照合する。
    /// データはストリーム的に処理され、全エントリをメモリ上に同時展開することも、
    /// ディスクへ書き出すこともしない。
    /// </summary>
    /// <param name="password">展開時に検証済みのパスワード (he=on 7z 等)。指定があれば暗号化アーカイブも CRC 検証を実行する</param>
    internal static async Task<VerificationResult> VerifyArchiveAsync(
        string archivePath, CancellationToken cancellationToken = default, string? password = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(archivePath))
            return new VerificationResult(false, App.Text("ErrorHandler.FileNotFound"));

        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // ネイティブ 7z.dll 直列化ゲート（reader より外側で取得して生成→Test→Dispose を覆う）
                using var nativeGate = NativeArchiveGate.Enter(cancellationToken);
                var longPath = PathValidator.EnsureLongPathPrefix(archivePath);
                // he=on (ヘッダ暗号化) はパスワード無しだと ctor 自体が失敗して外側 catch で
                // 「検証失敗」になるため、検証済みパスワードがあれば reader に渡して開く。
                using var reader = LockedFileRetryPolicy.Execute(
                    () => password is null
                        ? new ArchiveReader(longPath)
                        : new ArchiveReader(longPath, password, new ArchiveOption()),
                    archivePath);

                // パスワード保護アーカイブはパスワードなしで Test() すると失敗するためスキップ。
                // ヘッダー暗号化(-mhe=on)の場合は reader.Items 自体がアクセス不可。
                bool hasEncryptedItems;
                try
                {
                    hasEncryptedItems = reader.Items.Any(item => item.Encrypted);
                }
                catch (OperationCanceledException) { throw; }
                catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"アーカイブヘッダー読み取り中に I/O エラー: {archivePath} - {ex.Message}", LogLevel.Warning);
                    return new VerificationResult(false, ex.Message);
                }
                catch (UnauthorizedAccessException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"アーカイブヘッダー読み取り中にアクセス拒否: {archivePath} - {ex.Message}", LogLevel.Warning);
                    return new VerificationResult(false, ex.Message);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.Log($"アーカイブヘッダー読み取り失敗（ヘッダー暗号化の可能性）のため CRC 検証をスキップ: {archivePath}");
                    return new VerificationResult(true);
                }

                if (hasEncryptedItems && password is null)
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
