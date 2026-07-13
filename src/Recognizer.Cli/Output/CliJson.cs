using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Recognizer.Cli.Output;

/// <summary>出力 JSON のシリアライズ設定と書き出し(要件 6.2〜6.4)。</summary>
internal static class CliJson
{
    /// <summary>出力 DTO のシリアライズ設定。</summary>
    internal static JsonSerializerOptions Options { get; } = new()
    {
        TypeInfoResolver = CliJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Why not: JsonStringEnumConverter に命名ポリシーを渡さない。status は列挙子名をそのまま
        // ("NoFaceInImage1")出す契約のため(要件 5.3 / 6.2)。
        Converters = { new JsonStringEnumConverter() },

        // Why not: 既定のエンコーダを使わない。既定は非 ASCII をすべて \uXXXX へエスケープするため、
        // 日本語のエラーメッセージ(要件 7.1 の「人間可読なメッセージ」)やクラス名・パスが端末上で
        // 読めなくなる。UnsafeRelaxedJsonEscaping ではなく UnicodeRanges.All を選ぶのは、
        // < > & 等の HTML 由来のエスケープを維持したまま非 ASCII だけを素通しする安全側のため。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),

        // Why not: DefaultIgnoreCondition を設定しない。landmarks / face1 / face2 は null のときも
        // キーごと省略せず null を出力する契約のため(要件 3.5 / 5.6 / 5.7)。
        WriteIndented = false,
    };

    /// <summary>DTO を 1 行の JSON にする(要件 6.3)。数値は不変形式で出力される(要件 6.4)。</summary>
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>DTO を 1 行の JSON として書き出し、末尾に改行 1 個を付ける(要件 6.3)。</summary>
    internal static void Write<T>(TextWriter writer, T value) => writer.WriteLine(Serialize(value));
}
