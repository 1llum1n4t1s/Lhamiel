namespace Lhamiel.Util;

// 本物の readonly 設定を UnsafeAccessor で上書きすると tiered JIT の定数化と
// 衝突するため、台帳が使う保存先だけを同一 API の可変 fixture で隔離する。
internal static class Settings
{
    internal static string AppDataDirectory = "";
}
