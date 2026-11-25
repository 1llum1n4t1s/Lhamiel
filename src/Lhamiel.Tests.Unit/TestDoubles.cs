using Lhamiel.Util;

namespace Lhamiel.Tests.Unit;

// ArchiveProcessor の static seam（MessageServiceImpl / UiDispatcherImpl）を差し替えるテストで
// 共通に使うテストダブル。以前は ArchiveProcessorTests / ArchiveProcessorAdversarialTests /
// ArchiveProcessorPasswordTests / HeaderEncryptedArchiveTests がそれぞれ同等の private 入れ子クラスを持っていた。
// 差し替えたテストは従来どおり [Collection("ArchiveProcessor")] で直列実行し、Dispose で元の実装へ戻すこと
// （このファイルは seam の差し替え・復元を行わない）。

/// <summary>
/// エラー通知を記録するメッセージサービスのスタブ。
/// 並列展開・並列圧縮から同時に呼ばれても記録が壊れないよう、追加時に <see cref="Errors"/> をロックする。
/// 記録の読み取りは処理完了後（単一スレッド）に行う前提。
/// </summary>
internal sealed class RecordingMessageService : IMessageService
{
    /// <summary>ShowError に渡されたメッセージを呼び出し順に保持する。</summary>
    public List<string> Errors { get; } = [];

    /// <summary>ShowInfo に渡されたメッセージを呼び出し順に保持する。</summary>
    public List<string> Infos { get; } = [];

    public Task ShowError(string message, string? title = null)
    {
        lock (Errors)
            Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task ShowInfo(string message, string? title = null)
    {
        lock (Infos)
            Infos.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// UI ディスパッチを呼び出し元スレッドで同期実行するスタブ (headless テスト用)。
/// </summary>
internal sealed class SyncUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
    public Task InvokeAsync(Func<Task> callback) => callback();
    public Task<T> InvokeAsync<T>(Func<Task<T>> callback) => callback();
}
