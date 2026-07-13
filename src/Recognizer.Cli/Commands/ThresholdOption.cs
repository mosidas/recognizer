using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Recognizer.Cli.Errors;

namespace Recognizer.Cli.Commands;

/// <summary>
/// 閾値オプション(<c>--confidence</c> / <c>--nms</c> / <c>--detection-threshold</c>)を生成する。
/// 数値変換と 0.0〜1.0 の値域検証をパース段階で一体に行い、ライブラリへ不正値を渡さない(要件 2.6・design §6)。
/// </summary>
internal static class ThresholdOption
{
    /// <summary>
    /// 閾値オプションを生成する。値が省略されれば <paramref name="defaultValue"/>、
    /// 解釈不能なら <c>invalidOptionValue</c>、値域外なら <c>optionValueOutOfRange</c> を
    /// <paramref name="collector"/> に記録して ParseError を追加する(例外は投げない)。
    /// </summary>
    public static Option<float> Create(string name, float defaultValue, UsageErrorCollector collector)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(collector);
        if (!IsInRange(defaultValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultValue), defaultValue, "既定値は 0.0 以上 1.0 以下でなければなりません。");
        }

        return new Option<float>(name)
        {
            Description = $"閾値(0.0〜1.0)。既定値: {defaultValue.ToString(CultureInfo.InvariantCulture)}",
            DefaultValueFactory = _ => defaultValue,

            // Why not Option.Validators を使わない: バリデータ内で OptionResult.GetValueOrDefault<float>() を
            // 呼ぶと、変換不能な値(--confidence abc)に対して Parse() 自体が InvalidOperationException を
            // 投げ、CLI がエラー JSON を出す前にクラッシュする(実測。research §7.2)。CustomParser なら
            // 変換失敗も値域違反も ParseError として表現でき、日本語メッセージも自前で出せる。
            CustomParser = argumentResult => ParseThreshold(argumentResult, name, collector),
        };
    }

    // CustomParser はオプション省略時にも「値なし」指定時にも呼ばれない(実測。research §7.2)。
    // 前者は DefaultValueFactory の値が使われ、後者は UsageErrorClassifier が分類する(design §8.2 順 5)。
    private static float ParseThreshold(ArgumentResult argumentResult, string name, UsageErrorCollector collector)
    {
        string raw = argumentResult.Tokens[0].Value;

        // Why not raw の解釈をカルチャに委ねない: ロケール依存だと "0.7" の小数点が環境で変わる。
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            return Reject(
                argumentResult,
                collector,
                $"{name} は数値で指定してください(指定値: {raw})。",
                ErrorCodes.InvalidOptionValue);
        }

        if (!IsInRange(value))
        {
            return Reject(
                argumentResult,
                collector,
                $"{name} は 0.0 以上 1.0 以下で指定してください(指定値: {raw})。",
                ErrorCodes.OptionValueOutOfRange);
        }

        return value;
    }

    // Why not v < 0f || v > 1f と書かない: float.TryParse は "NaN" の解析に成功し、NaN はあらゆる比較が
    // false になるため、その書き方だと NaN が値域検証を素通りしてライブラリへ渡る(実測。research §8)。
    // 肯定形の範囲を否定することで、NaN も Infinity も値域外として弾ける(要件 2.6)。
    private static bool IsInRange(float value) => value >= 0f && value <= 1f;

    private static float Reject(
        ArgumentResult argumentResult,
        UsageErrorCollector collector,
        string message,
        string code)
    {
        collector.Add(message, code);
        argumentResult.AddError(message);

        // ParseError を立てた時点で CliApplication は Action を呼ばないため、この値は使われない。
        return default;
    }
}
