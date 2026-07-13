using System.Text.Json.Serialization;

namespace Recognizer.Cli.Output;

/// <summary>出力 DTO のソース生成された型情報。</summary>
// Why not: この context の JsonTypeInfo を JsonSerializer.Serialize(value, CliJsonContext.Default.X) の形で
// 直接使わない。命名ポリシーと列挙子変換が効かず PascalCase・enum が数値になる(design §7 / research §8)。
// 必ず CliJson.Options(TypeInfoResolver として結線したもの)を経由する。
[JsonSerializable(typeof(DetectFaceOutput))]
[JsonSerializable(typeof(DetectObjectOutput))]
[JsonSerializable(typeof(CompareFaceOutput))]
[JsonSerializable(typeof(ErrorOutput))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
