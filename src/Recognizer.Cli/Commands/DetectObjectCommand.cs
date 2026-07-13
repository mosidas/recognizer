using System.CommandLine;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Commands;

/// <summary>
/// <c>detect-object</c> コマンドの定義と Action(要件 4.1〜4.3・4.5・4.7・4.8)。
/// ライブラリ呼び出しと DTO 組み立てのみを持ち、JSON の形は <see cref="Output"/> に委ねる(design §2)。
/// </summary>
internal static class DetectObjectCommand
{
    /// <summary>
    /// <c>detect-object &lt;image&gt; --model &lt;path&gt; [--confidence 0.5] [--nms 0.5]</c> を組み立てる。
    /// </summary>
    public static Command Create(UsageErrorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        Argument<string> image = new("image") { Description = "物体を検出する画像のパス。" };
        Option<string> model = new("--model")
        {
            Description = "物体検出モデル(ONNX)のパス。",
            Required = true,
        };

        // Why not: 既定値に detect-face の 0.7 を流用しない。detect-object の --confidence の既定は 0.5 で、
        // ライブラリ(ObjectDetector.DetectAsync)の既定とも一致する(要件 2.3)。
        Option<float> confidence = ThresholdOption.Create("--confidence", 0.5f, collector);
        Option<float> nms = ThresholdOption.Create("--nms", 0.5f, collector);

        Command command = new("detect-object", "画像から物体を検出し、クラス・bbox・信頼度を JSON で出力する。")
        {
            image,
            model,
            confidence,
            nms,
        };

        command.SetAction((parseResult, cancellationToken)
            => ExecuteAsync(parseResult, image, model, confidence, nms, cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        ParseResult parseResult,
        Argument<string> image,
        Option<string> model,
        Option<float> confidence,
        Option<float> nms,
        CancellationToken cancellationToken)
    {
        // Why not: Path.GetFullPath などで正規化しない。image は位置引数の文字列をそのまま出力する契約
        // (要件 6.5)であり、検出にもこの文字列をそのまま使う。
        string imagePath = parseResult.GetValue(image)!;

        // Why not: classNames に CLI が既定値を用意しない。null を渡してライブラリの既定解決
        // (80 クラスなら COCO 名、それ以外は class_{id})に委ねる(要件 4.5)。
        using ObjectDetector detector = new(parseResult.GetValue(model)!, classNames: null);
        IReadOnlyList<ObjectDetection> objects = await detector
            .DetectAsync(
                imagePath,
                parseResult.GetValue(confidence),
                parseResult.GetValue(nms),
                cancellationToken)
            .ConfigureAwait(false);

        // Why not: objects を並べ替えない。ライブラリの返却順(信頼度降順)をそのまま反映する(要件 4.8)。
        // Why not: Console.Out に直接書かない。InvocationConfiguration.Output は CliApplication が呼び出し元から
        // 受け取った TextWriter であり、これを使わないとテストが出力を捕捉できない(design §9.1)。
        CliJson.Write(parseResult.InvocationConfiguration.Output, DetectObjectOutput.From(imagePath, objects));

        // 検出 0 件も成功。空配列を出して 0 で終わる(要件 4.7・7.9)。
        return ExitCodes.Success;
    }
}
