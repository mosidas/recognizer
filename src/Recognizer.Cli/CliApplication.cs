using System.CommandLine;
using Recognizer.Cli.Commands;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli;

/// <summary>
/// CLI 全体の制御フロー(design §4・§6): RootCommand 構築 → Parse → 使用法エラー判定 → Invoke →
/// 実行時エラー捕捉 → 終了コード決定。「エラーは JSON で stderr、非 0 終了」という中心ルールを所有する。
/// </summary>
internal static class CliApplication
{
    /// <summary>
    /// CLI を 1 回実行する。成功時のみ <paramref name="output"/> に 1 行の JSON を書き、エラー時は
    /// <paramref name="error"/> にのみ 1 行のエラー JSON を書く(要件 6.1・7.1・7.2)。
    /// 戻り値は <see cref="ExitCodes"/> のいずれか(要件 7.3)。例外は外へ漏らさない。
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        // Why not: RootCommand と collector を静的にキャッシュしない。collector は実行単位で可変な記録を持ち、
        // 共有すると連続実行・並行実行で前回の値エラーが混線する(design §6)。
        UsageErrorCollector collector = new();
        ParseResult parseResult = BuildRootCommand(collector).Parse(args);

        // Why not: パースエラーがあるときに InvokeAsync を呼ばない。既定の ParseErrorAction が英語のエラー文と
        // ヘルプを stdout / stderr に書き、JSON 契約(要件 7.1・7.2)を破る(design §8.2・research §7.2)。
        if (parseResult.Errors.Count > 0)
        {
            CliJson.Write(error, UsageErrorClassifier.Classify(parseResult, collector));

            return ExitCodes.UsageError;
        }

        InvocationConfiguration configuration = new()
        {
            // Why not: 既定の例外ハンドラを使わない。有効なままだと英語のスタックトレースが stderr に出て、
            // エラーは {error, code} の JSON という契約(要件 7.1)を破る(design §8.1)。
            EnableDefaultExceptionHandler = false,
            Output = output,
            Error = error,
        };

        try
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Why not: 例外型を絞り込まない。ライブラリ由来の既知の例外(design §8.1 の順 2〜5)に加え、
            // 未知の例外も unexpectedError として JSON 化して終端する契約のため(design §6 の事後条件)。
            CliJson.Write(error, RuntimeErrorMapper.Map(exception));

            return ExitCodes.RuntimeError;
        }
    }

    // compare-face の登録は後続タスクが行う。閾値オプションの CustomParser が値エラーの
    // 記録先として collector を要するため、各コマンドは登録時に collector を受け取る。
    private static RootCommand BuildRootCommand(UsageErrorCollector collector)
        => new("YOLO 形式の ONNX モデルで顔検出・物体検出・顔類似度を実行する CLI。")
        {
            DetectFaceCommand.Create(collector),
            DetectObjectCommand.Create(collector),
        };
}
