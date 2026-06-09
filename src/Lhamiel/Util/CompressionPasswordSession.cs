using System.Security.Cryptography;
using System.Text;

namespace Lhamiel.Util;

/// <summary>
/// 圧縮実行スコープでのみ平文パスワードを扱う一時保持ヘルパ。
/// <para>
/// <see cref="Settings.EncryptedCompressionPassword"/> は DPAPI 暗号化バイト列のみを保持し、
/// 平文 <c>string</c> は <see cref="TryUnprotect"/> の戻り値を **短寿命のローカル変数** に閉じて
/// 圧縮終了後はすぐ参照を捨てる運用にする（GC 寿命は伸ばさない）。
/// </para>
/// <para>
/// DPAPI scope = <see cref="DataProtectionScope.CurrentUser"/>。同じ Windows ユーザー
/// + 同じ PC でのみ復号可能。別 PC へ <c>settings.json</c> をコピーした場合や Windows パスワード
/// リセット後は復号失敗 → <see cref="TryUnprotect"/> が null を返し、UI 側で再設定を要求する設計。
/// </para>
/// <para>
/// ⚠️ Settings の自動 wipe はしない。<see cref="TryUnprotect"/> が null を返しても
/// <see cref="Settings.EncryptedCompressionPassword"/> は変更しない（呼出側が明示的に
/// 再設定 or PromptEachTime 切替を行うまで温存）。サイレント wipe は OneDrive 同期等で
/// 一時的に復号失敗するケースをユーザーが気付かずパスワードを失うリスクがある。
/// </para>
/// </summary>
internal static class CompressionPasswordSession
{
    /// <summary>パスワード平文の最大長（chars）。DPAPI ciphertext は UTF-8 byte 数 + メタデータで増加するため余裕を持って 1024 とする。</summary>
    internal const int MaxPlaintextLength = 1024;

    /// <summary>
    /// 平文パスワードを DPAPI（CurrentUser scope）で暗号化したバイト列に変換する。
    /// </summary>
    /// <param name="plaintext">平文パスワード。<c>null</c> または空文字列なら <c>null</c> を返す。</param>
    /// <returns>DPAPI 暗号化済みバイト列。null/empty 入力時は <c>null</c>。</returns>
    /// <exception cref="ArgumentException">平文が <see cref="MaxPlaintextLength"/> を超える長さの場合。</exception>
    /// <remarks>
    /// 中間 byte[] は <see cref="CryptographicOperations.ZeroMemory"/> で best-effort に 0 埋めする。
    /// 元の <see cref="string"/> 自体は .NET の immutable string なので破棄できない（GC 任せ）。
    /// </remarks>
    internal static byte[]? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        if (plaintext.Length > MaxPlaintextLength)
            throw new ArgumentException(
                $"Password too long (max {MaxPlaintextLength} chars, got {plaintext.Length}).",
                nameof(plaintext));

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// DPAPI 暗号化バイト列を平文パスワードに復号する。
    /// </summary>
    /// <param name="ciphertext">DPAPI 暗号化バイト列。<c>null</c> または空ならそのまま <c>null</c>。</param>
    /// <returns>
    /// 復号成功時は平文文字列。失敗時（別ユーザー / 別 PC / マスタキー破損）は <c>null</c>。
    /// <see cref="Settings"/> は変更されないため、呼出側で UI に「再設定してください」と促す。
    /// </returns>
    internal static string? TryUnprotect(byte[]? ciphertext)
    {
        if (ciphertext is null || ciphertext.Length == 0)
            return null;

        byte[]? plainBytes = null;
        try
        {
            plainBytes = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            // 別ユーザー / 別 PC / Windows パスワードリセット後等で発生。
            // Settings 側は変更しない（サイレント wipe を避ける）。呼出側が UI 警告 + 再プロンプトで対応する。
            try
            {
                Logger.Log(
                    $"圧縮パスワードの DPAPI 復号に失敗しました（別ユーザー/PC コピーや Windows パスワードリセット等が原因の可能性）: {ex.Message}",
                    LogLevel.Warning);
            }
            catch { /* Logger 未初期化のケース */ }
            return null;
        }
        finally
        {
            if (plainBytes is not null)
                CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}
