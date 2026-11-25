namespace Lhamiel.Util;

/// <summary>settings.json の読み込み結果の種別。</summary>
internal enum SettingsLoadStatus
{
    /// <summary>ファイルが無く、既定値で新規作成した（作成に失敗しても既存設定は失われない）。</summary>
    Created,

    /// <summary>正常に読み込めた（一部プロパティだけ回収した場合を含む）。</summary>
    Loaded,

    /// <summary>JSON が回収不能に破損していたため、退避して既定値にした（ファイルの中身は既に失われている）。</summary>
    CorruptReset,

    /// <summary>
    /// ファイルがあるのに読めなかった（他プロセスの排他・アクセス拒否・属性取得失敗など）。
    /// 設定は既定値だが、実際の内容は不明なので、呼び出し側は既定値をファイルへ書き戻してはならない。
    /// </summary>
    Unreadable,
}

/// <summary>settings.json の読み込み結果。<see cref="Error"/> は <see cref="SettingsLoadStatus.Unreadable"/> の原因。</summary>
internal sealed record SettingsLoadResult(Settings Settings, SettingsLoadStatus Status, Exception? Error = null);
