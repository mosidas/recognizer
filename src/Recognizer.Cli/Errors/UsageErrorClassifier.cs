using System.CommandLine;
using System.CommandLine.Parsing;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Errors;

/// <summary>
/// パースに失敗した <see cref="ParseResult"/> を、エラー JSON(<c>error</c> / <c>code</c>)へ写す
/// (design §8.2・要件 2.4・2.5・7.8)。
/// </summary>
// Why not ParseError.Message を文字列一致で判定しない: フレームワークのメッセージは英語("Option '--model' is
// required." 等)で、将来のバージョンで文言が変わると分類が壊れる。SymbolResult の型と UnmatchedTokens の
// 構造で判定する(design §8.2・research §7.2)。日本語メッセージ(CLAUDE.md の規約)も自前で生成する。
internal static class UsageErrorClassifier
{
    /// <summary>
    /// design §8.2 の対応表を上から順に評価し、最初に一致した行の <c>code</c> と日本語メッセージを返す
    /// (複数種別が同時に起きても <c>code</c> は一意になる。要件 7.8)。
    /// </summary>
    public static ErrorOutput Classify(ParseResult parseResult, UsageErrorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(collector);

        // 順 1・2: CustomParser が検出した値エラー。記録済みの日本語メッセージ(指定値を含む)をそのまま使う。
        ErrorOutput? valueError =
            FindRecorded(collector, ErrorCodes.InvalidOptionValue)
            ?? FindRecorded(collector, ErrorCodes.OptionValueOutOfRange);
        if (valueError is not null)
        {
            return valueError;
        }

        // 順 3: 未知のコマンド・未知のオプション・位置引数の過剰。
        // Why not 順 7(コマンド未指定)より後に置かない: 未知のコマンドは UnmatchedTokens に載りつつ
        // CommandResult.Command が RootCommand にもなるため(実測)、後ろに置くと missingCommand に吸われる。
        if (parseResult.UnmatchedTokens.Count > 0)
        {
            return new ErrorOutput(
                $"認識できない引数です: {string.Join(", ", parseResult.UnmatchedTokens)}",
                ErrorCodes.UnrecognizedArgument);
        }

        // 順 4: 必須オプションの欠落、および必須オプションを値なしで書いた場合(構造上どちらも同じ形になる)。
        // Why not 述語から Option.Required を落とさない: 必須でないオプション(--confidence)の値欠落も
        // 「OptionResult かつトークン 0 件」であり、Required を見ないと必須オプション欠落と誤分類する(design §8.2)。
        foreach (ParseError error in parseResult.Errors)
        {
            if (error.SymbolResult is OptionResult { Option.Required: true, Tokens.Count: 0 } required)
            {
                return new ErrorOutput(
                    $"必須オプション {required.Option.Name} の値が指定されていません。",
                    ErrorCodes.MissingRequiredOption);
            }
        }

        // 順 5: 必須でないオプションの値欠落。値を解釈できない一種として invalidOptionValue に含める(要件 7.8)。
        foreach (ParseError error in parseResult.Errors)
        {
            if (error.SymbolResult is OptionResult { Tokens.Count: 0 } optional)
            {
                return new ErrorOutput(
                    $"{optional.Option.Name} には値が必要です。",
                    ErrorCodes.InvalidOptionValue);
            }
        }

        // 順 6: 位置引数の不足。
        foreach (ParseError error in parseResult.Errors)
        {
            if (error.SymbolResult is ArgumentResult argument)
            {
                return new ErrorOutput(
                    $"位置引数 {argument.Argument.Name} が指定されていません。",
                    ErrorCodes.MissingArgument);
            }
        }

        // 順 7: サブコマンド未指定。
        if (parseResult.CommandResult.Command is RootCommand root)
        {
            return new ErrorOutput(DescribeMissingCommand(root), ErrorCodes.MissingCommand);
        }

        // 順 8: 上記以外(同一オプションの重複指定など)。
        return new ErrorOutput("引数が不正です。", ErrorCodes.InvalidUsage);
    }

    private static ErrorOutput? FindRecorded(UsageErrorCollector collector, string code)
        => collector.Errors.FirstOrDefault(e => e.Code == code);

    // Why not コマンド名を直書きしない: Errors/ にコマンド一覧の複製ができ、コマンドの増減で案内が古くなる。
    private static string DescribeMissingCommand(RootCommand root)
    {
        string names = string.Join(" / ", root.Subcommands.Where(c => !c.Hidden).Select(c => c.Name));

        return $"コマンドを指定してください({names})。";
    }
}
