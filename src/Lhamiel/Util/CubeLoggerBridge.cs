namespace Lhamiel.Util;

/// <summary>
/// ライブラリ (1llum1n4t1s.Sevenzip / Cube.*) 内部の <see cref="Cube.Logger"/> 出力を
/// Lhamiel の <see cref="Logger"/> に転送する <see cref="Cube.ILoggerSource"/> 実装。
/// </summary>
/// <remarks>
/// 既定で <see cref="Cube.Logger"/> は <see cref="Cube.NullLoggerSource"/> を使い、ライブラリ内部の
/// 警告・エラー（アーカイブ Open 失敗、圧縮リトライ、一時ファイル削除失敗など）を全て捨てている。
/// 起動時に <c>Cube.Logger.Configure(new CubeLoggerBridge())</c> で差し替えることで、これらの
/// 診断が Lhamiel のログファイルに残り、障害解析・診断 ZIP で追跡できる。RTK レビュー #14 対応。
/// <para>
/// Lhamiel 自身も SuperLightLogger を直接設定しているため、ここでは SuperLightLogger を
/// 二重設定せず、単に <see cref="Logger.Log(string, LogLevel)"/> に転送する（設定の取り合いを避ける）。
/// </para>
/// </remarks>
internal sealed class CubeLoggerBridge : Cube.ILoggerSource
{
    public void Log(string path, int number, Cube.LogLevel level, string message)
    {
        // 呼び出し元ファイル名だけを短く付与し、ライブラリ由来ログだと判別できるようにする。
        var src = string.IsNullOrEmpty(path) ? "lib" : System.IO.Path.GetFileNameWithoutExtension(path);
        Logger.Log($"[7z:{src}:{number}] {message}", Map(level));
    }

    /// <summary>Cube.LogLevel を Lhamiel の <see cref="LogLevel"/> に対応付ける。</summary>
    private static LogLevel Map(Cube.LogLevel level) => level switch
    {
        Cube.LogLevel.Trace => LogLevel.Debug,
        Cube.LogLevel.Debug => LogLevel.Debug,
        Cube.LogLevel.Information => LogLevel.Info,
        Cube.LogLevel.Warning => LogLevel.Warning,
        Cube.LogLevel.Error => LogLevel.Error,
        _ => LogLevel.Info,
    };
}
