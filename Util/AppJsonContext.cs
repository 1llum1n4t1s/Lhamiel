using System.Text.Json.Serialization;

namespace Lhamiel.Util;

/// <summary>
/// JSON Source Generator 用のコンテキストクラス。
/// Native AOT 環境での JSON シリアライズをサポートします。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(string[]))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
