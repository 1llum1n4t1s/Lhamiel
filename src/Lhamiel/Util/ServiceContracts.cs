using Avalonia.Controls;
namespace Lhamiel.Util;

/// <summary>
/// アプリケーションログ出力の契約。
/// </summary>
/// <remarks>
/// /rere レビュー指摘 #25「静的シングルトンでテスト不能域拡大」の入口対応。
/// 既存の <see cref="Logger"/> 静的クラスをそのまま残しつつ、将来の DI 移行や
/// テストでのモック差し替えに備えてインターフェースを定義する。
/// 呼び出し元の段階的移行を可能にするため、<see cref="DefaultAppLogger"/> が
/// 既存 <see cref="Logger"/> 実装に委譲する形で動作する。
/// 名前空間衝突を避けるため <c>IAppLogger</c> としている（<c>Microsoft.Extensions.Logging.ILogger</c> と区別）。
/// </remarks>
public interface IAppLogger
{
    /// <summary>メッセージをログに記録する。</summary>
    void Log(string message, LogLevel level = LogLevel.Info);

    /// <summary>例外と説明を一緒にログに記録する。</summary>
    void LogException(string message, Exception ex);
}

/// <summary>
/// メッセージダイアログ表示の契約。
/// </summary>
/// <remarks>
/// /rere レビュー指摘 #25 の入口対応。テストでのモック差し替え用。
/// 既存 <see cref="MessageService"/> 静的クラスはそのまま残す。
/// </remarks>
public interface IAppMessageService
{
    /// <summary>エラーメッセージを表示する。</summary>
    Task ShowError(string message, string? title = null);

    /// <summary>情報メッセージを表示する。</summary>
    Task ShowInfo(string message, string? title = null);

    /// <summary>警告メッセージを表示する。</summary>
    Task ShowWarning(string message, string? title = null);

    /// <summary>例外を整形して表示する。</summary>
    Task ShowException(string context, Exception ex, string? title = null);

    /// <summary>成功メッセージを表示する。</summary>
    Task ShowSuccess(string message, string? title = null);

    /// <summary>はい/いいえ確認ダイアログを表示する。</summary>
    Task<bool> ShowYesNoQuestionAsync(string message, string title, Window? parentWindow = null);
}

/// <summary>
/// <see cref="IAppLogger"/> の既定実装。<see cref="Logger"/> 静的 API へ委譲する。
/// </summary>
internal sealed class DefaultAppLogger : IAppLogger
{
    public void Log(string message, LogLevel level = LogLevel.Info) => Logger.Log(message, level);
    public void LogException(string message, Exception ex) => Logger.LogException(message, ex);
}

/// <summary>
/// <see cref="IAppMessageService"/> の既定実装。<see cref="MessageService"/> 静的 API へ委譲する。
/// </summary>
internal sealed class DefaultAppMessageService : IAppMessageService
{
    public Task ShowError(string message, string? title = null) => MessageService.ShowError(message, title);
    public Task ShowInfo(string message, string? title = null) => MessageService.ShowInfo(message, title);
    public Task ShowWarning(string message, string? title = null) => MessageService.ShowWarning(message, title);
    public Task ShowException(string context, Exception ex, string? title = null) => MessageService.ShowException(context, ex, title);
    public Task ShowSuccess(string message, string? title = null) => MessageService.ShowSuccess(message, title);
    public Task<bool> ShowYesNoQuestionAsync(string message, string title, Window? parentWindow = null)
        => MessageService.ShowYesNoQuestionAsync(message, title, parentWindow);
}

/// <summary>
/// アプリケーションスコープのサービスインスタンスを保持するコンテナ。
/// 既定では静的クラスへの委譲アダプタを返すが、テストやワイヤリング段階で差し替え可能。
/// </summary>
/// <remarks>
/// /rere レビュー指摘 #25 入口対応。完全な DI コンテナ化は別ロードマップだが、
/// テストで差し替えたい場合は <see cref="ResetForTests"/> を呼んで
/// <see cref="Logger"/> / <see cref="MessageService"/> 経路を経由しないモックを設定できる。
/// </remarks>
public static class AppServices
{
    private static IAppLogger _logger = new DefaultAppLogger();
    private static IAppMessageService _messageService = new DefaultAppMessageService();

    /// <summary>現在のロガー実装。</summary>
    public static IAppLogger Logger => _logger;

    /// <summary>現在のメッセージサービス実装。</summary>
    public static IAppMessageService MessageService => _messageService;

    /// <summary>テスト用にロガーを差し替える（プロダクションでは使わないこと）。</summary>
    internal static void SetLoggerForTests(IAppLogger logger) => _logger = logger ?? new DefaultAppLogger();

    /// <summary>テスト用にメッセージサービスを差し替える（プロダクションでは使わないこと）。</summary>
    internal static void SetMessageServiceForTests(IAppMessageService messageService) => _messageService = messageService ?? new DefaultAppMessageService();

    /// <summary>テスト同士の独立性を保つため、既定の静的クラス委譲実装に戻す。</summary>
    internal static void ResetForTests()
    {
        _logger = new DefaultAppLogger();
        _messageService = new DefaultAppMessageService();
    }
}
