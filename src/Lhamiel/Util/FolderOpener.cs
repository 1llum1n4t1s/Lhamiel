namespace Lhamiel.Util;

/// <summary>
/// フォルダをWindowsエクスプローラーで開く機能を提供するクラス
/// </summary>
public static class FolderOpener
{
    /// <summary>
    /// テスト時に Process.Start をスキップするためのフラグ（InternalsVisibleTo 経由で設定）
    /// </summary>
    internal static bool DryRun { get; set; }
    /// <summary>
    /// 展開結果のフォルダを開く。
    /// CreateArchiveNameFolder=ON + 二重ネスト防止スキップ時は、アーカイブのルートフォルダを開く。
    /// </summary>
    /// <param name="outputPath">展開先パス</param>
    /// <param name="structureInfo">アーカイブ構造情報</param>
    /// <param name="createArchiveNameFolder">展開時に使用されたフォルダ作成設定の値</param>
    public static void OpenExtractionResult(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo = null,
        bool? createArchiveNameFolder = null)
    {
        var folderToOpen = GetExtractionFolderToOpen(outputPath, structureInfo, createArchiveNameFolder);
        if (Directory.Exists(folderToOpen))
            OpenFolder(folderToOpen);
    }

    /// <summary>
    /// <see cref="OpenExtractionResult"/> の待機可能版。explorer 起動 (Process.Start のリターン)
    /// まで完了する Task を返す。
    /// </summary>
    /// <remarks>
    /// CLI / ファイル関連付け / アイコンドロップ起動の「処理後に自己終了する」経路で使う。
    /// 同期版は fire-and-forget のため、呼び出し元が直後に <c>desktop.Shutdown()</c> すると
    /// explorer 起動がバックグラウンドスレッド上で走り切る前にプロセスが落ち、フォルダが
    /// 開かない競合になる (v1.0.171 回帰)。この版を await すれば Shutdown 前に explorer 起動が
    /// 完了する。explorer 起動自体は引き続き <see cref="ShellOpener.OpenInExplorerAsync"/> の
    /// Task.Run で別スレッドに逃がすため、UI スレッドはブロックしない (Issue #54 対策を維持)。
    /// </remarks>
    public static async Task OpenExtractionResultAsync(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo = null,
        bool? createArchiveNameFolder = null)
    {
        var folderToOpen = GetExtractionFolderToOpen(outputPath, structureInfo, createArchiveNameFolder);
        if (Directory.Exists(folderToOpen))
            await OpenFolderAsync(folderToOpen);
    }

    /// <summary>
    /// 展開結果として開くべきフォルダのパスを決定する。
    /// CreateArchiveNameFolder=ON + 二重ネスト防止でフォルダ作成がスキップされた場合、
    /// アーカイブのルートフォルダ（outputPath/SingleRootItemName）を返す。
    /// </summary>
    /// <remarks>
    /// 展開時に使われた設定値を最も優先する：
    /// <list type="number">
    ///   <item><description><see cref="ArchiveExtractor.ArchiveStructureInfo.CapturedCreateArchiveNameFolder"/>（展開時スナップショット）</description></item>
    ///   <item><description><paramref name="createArchiveNameFolder"/>（呼び出し側が明示）</description></item>
    ///   <item><description>現在の設定値（フォールバック）</description></item>
    /// </list>
    /// この順で参照することで、展開中のユーザー設定変更に対しても
    /// 「作成したフォルダ」と「開くフォルダ」の整合性を保つ。
    /// </remarks>
    /// <param name="createArchiveNameFolder">展開時に使用された設定値。nullの場合は structureInfo か現在の設定を参照する。</param>
    internal static string GetExtractionFolderToOpen(
        string outputPath,
        ArchiveExtractor.ArchiveStructureInfo? structureInfo,
        bool? createArchiveNameFolder = null)
    {
        var createFolder = structureInfo?.CapturedCreateArchiveNameFolder
                           ?? createArchiveNameFolder
                           ?? SettingsManager.Instance.Current.CreateArchiveNameFolder;

        if (createFolder && structureInfo is { ShouldSkipFolderCreation: true }
            && !string.IsNullOrEmpty(structureInfo.SingleRootItemName))
        {
            var archiveFolder = Path.Combine(outputPath, structureInfo.SingleRootItemName);
            if (Directory.Exists(archiveFolder))
                return archiveFolder;
        }

        return outputPath;
    }

    /// <summary>
    /// 指定したフォルダをWindowsエクスプローラーで開く
    /// </summary>
    /// <param name="folderPath">開くフォルダのパス</param>
    public static void OpenFolder(string folderPath)
    {
        if (!TryPrepareOpen(folderPath))
            return;

        // explorer.exe 起動は ShellOpener が Task.Run で別スレッドへ逃がす (Issue #54 対策)。
        // 戻り値の Task は fire-and-forget で投げる: 呼び出し元は同期 void シグネチャを
        // 維持するため await しない。例外はタスク内で catch してログに記録する。
        // 常駐インスタンス (ドラッグ&ドロップ / ダイアログ) 専用 — プロセスが生存し続けるため
        // detached な起動 Task は確実に完走する。自己終了する CLI 経路は OpenFolderAsync を使う。
        _ = ShellOpener.OpenInExplorerAsync(folderPath)
            .ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception is not null)
                    Logger.LogException($"フォルダを開く処理でエラーが発生しました: {folderPath}", t.Exception.GetBaseException());
                else if (t.IsCompletedSuccessfully)
                    Logger.Log($"フォルダをエクスプローラーで開きました: {folderPath}", LogLevel.Debug);
            }, TaskScheduler.Default);
    }

    /// <summary>
    /// <see cref="OpenFolder"/> の待機可能版。explorer 起動 (Process.Start のリターン) まで
    /// 完了する Task を返す。処理後に <c>desktop.Shutdown()</c> する CLI / ファイル関連付け
    /// 経路で await して使う (シャットダウン競合の解消)。explorer 起動は引き続き
    /// <see cref="ShellOpener.OpenInExplorerAsync"/> の Task.Run で別スレッドに逃がすため
    /// UI スレッドはブロックしない。
    /// </summary>
    /// <param name="folderPath">開くフォルダのパス</param>
    public static async Task OpenFolderAsync(string folderPath)
    {
        if (!TryPrepareOpen(folderPath))
            return;

        try
        {
            await ShellOpener.OpenInExplorerAsync(folderPath);
            Logger.Log($"フォルダをエクスプローラーで開きました: {folderPath}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.LogException($"フォルダを開く処理でエラーが発生しました: {folderPath}", ex);
        }
    }

    /// <summary>
    /// フォルダオープンの共通事前チェック (空パス / 非存在 / DryRun)。
    /// 開いてよければ true、スキップすべきなら理由をログして false を返す。
    /// </summary>
    private static bool TryPrepareOpen(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Logger.Log("フォルダパスが指定されていません", LogLevel.Warning);
            return false;
        }

        if (!Directory.Exists(folderPath))
        {
            Logger.Log($"指定されたフォルダが見つかりません: {folderPath}", LogLevel.Warning);
            return false;
        }

        if (DryRun)
        {
            Logger.Log($"フォルダを開く処理をスキップしました（DryRun）: {folderPath}", LogLevel.Debug);
            return false;
        }

        return true;
    }
}
