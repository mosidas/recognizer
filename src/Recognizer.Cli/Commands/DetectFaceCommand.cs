using System.CommandLine;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Commands;

/// <summary>
/// <c>detect-face</c> コマンドの定義と Action(要件 3.1〜3.7)。
/// ライブラリ呼び出しと DTO 組み立てのみを持ち、JSON の形は <see cref="Output"/> に委ねる(design §2)。
/// </summary>
internal static class DetectFaceCommand
{
    /// <summary>
    /// <c>detect-face &lt;image&gt; --model &lt;path&gt; [--confidence 0.7] [--nms 0.5]</c> を組み立てる。
    /// </summary>
    public static Command Create(UsageErrorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        Argument<string> image = new("image") { Description = "顔を検出する画像のパス。" };
        Option<string> model = new("--model")
        {
            Description = "顔検出モデル(ONNX)のパス。",
            Required = true,
        };
        Option<float> confidence = ThresholdOption.Create("--confidence", 0.7f, collector);
        Option<float> nms = ThresholdOption.Create("--nms", 0.5f, collector);

        Command command = new("detect-face", "画像から顔を検出し、bbox・信頼度・ランドマークを JSON で出力する。")
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

        // Why not: Mat を CLI で扱わない。ライブラリの string パス版オーバーロードが画像デコードまで担うため、
        // CLI が OpenCvSharp に依存する理由がない(design §2)。
        using FaceDetector detector = new(parseResult.GetValue(model)!);
        IReadOnlyList<FaceDetection> faces = await detector
            .DetectAsync(
                imagePath,
                parseResult.GetValue(confidence),
                parseResult.GetValue(nms),
                cancellationToken)
            .ConfigureAwait(false);

        // Why not: faces を並べ替えない。ライブラリの返却順(信頼度降順)をそのまま反映する(要件 3.7)。
        // Why not: Console.Out に直接書かない。InvocationConfiguration.Output は CliApplication が呼び出し元から
        // 受け取った TextWriter であり、これを使わないとテストが出力を捕捉できない(design §9.1)。
        CliJson.Write(parseResult.InvocationConfiguration.Output, DetectFaceOutput.From(imagePath, faces));

        // 検出 0 件も成功。空配列を出して 0 で終わる(要件 3.6・7.9)。
        return ExitCodes.Success;
    }
}
