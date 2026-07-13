using System.CommandLine;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Commands;

/// <summary>
/// <c>compare-face</c> コマンドの定義と Action(要件 5.1〜5.8)。
/// ライブラリ呼び出しと DTO 組み立てのみを持ち、JSON の形は <see cref="Output"/> に委ねる(design §2)。
/// </summary>
internal static class CompareFaceCommand
{
    /// <summary>
    /// <c>compare-face &lt;image1&gt; &lt;image2&gt; --detector-model &lt;path&gt; --embedding-model &lt;path&gt;
    /// [--detection-threshold 0.7] [--nms 0.5]</c> を組み立てる。
    /// </summary>
    public static Command Create(UsageErrorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        Argument<string> image1 = new("image1") { Description = "比較する画像 1 のパス。" };
        Argument<string> image2 = new("image2") { Description = "比較する画像 2 のパス。" };
        Option<string> detectorModel = new("--detector-model")
        {
            Description = "顔検出モデル(ONNX)のパス。",
            Required = true,
        };
        Option<string> embeddingModel = new("--embedding-model")
        {
            Description = "顔埋め込みモデル(ONNX)のパス。",
            Required = true,
        };

        // Why not: 閾値オプション名に --confidence を流用しない。compare-face の検出閾値は
        // --detection-threshold で、既定は 0.7(ライブラリ CompareFacesAsync の既定と一致。要件 2.3)。
        Option<float> detectionThreshold = ThresholdOption.Create("--detection-threshold", 0.7f, collector);
        Option<float> nms = ThresholdOption.Create("--nms", 0.5f, collector);

        Command command = new("compare-face", "2 枚の画像の顔のコサイン類似度を JSON で出力する。")
        {
            image1,
            image2,
            detectorModel,
            embeddingModel,
            detectionThreshold,
            nms,
        };

        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(
            parseResult, image1, image2, detectorModel, embeddingModel, detectionThreshold, nms, cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        ParseResult parseResult,
        Argument<string> image1,
        Argument<string> image2,
        Option<string> detectorModel,
        Option<string> embeddingModel,
        Option<float> detectionThreshold,
        Option<float> nms,
        CancellationToken cancellationToken)
    {
        // Why not: Path.GetFullPath などで正規化しない。image1 / image2 は位置引数の文字列をそのまま出力する
        // 契約(要件 6.5)であり、比較にもこの文字列をそのまま使う。
        string imagePath1 = parseResult.GetValue(image1)!;
        string imagePath2 = parseResult.GetValue(image2)!;

        // Why not: Mat を CLI で扱わない。ライブラリの string パス版オーバーロードが画像デコードまで担うため、
        // CLI が OpenCvSharp に依存する理由がない(design §2)。
        using FaceRecognizer recognizer = new(
            parseResult.GetValue(detectorModel)!,
            parseResult.GetValue(embeddingModel)!);
        FaceComparisonResult result = await recognizer
            .CompareFacesAsync(
                imagePath1,
                imagePath2,
                parseResult.GetValue(detectionThreshold),
                parseResult.GetValue(nms),
                cancellationToken)
            .ConfigureAwait(false);

        // Why not: similarity を閾値と比較して同一人物か否かを判定しない。判定基準は呼び出し側が決める
        // ものであり、CLI は類似度を出すところまでに徹する(要件 5.4・design §1 の非ゴール)。
        // Why not: status / similarity / face1 / face2 を補正しない。NoFaceInImage1 のとき face2 は
        // (画像 2 に顔があっても)null になるが、ライブラリが画像 1 の未検出時点で早期返却して画像 2 を
        // 評価しないためであり、その返却値をそのまま反映する(要件 5.6・5.8。FaceRecognizer.cs:252)。
        CliJson.Write(
            parseResult.InvocationConfiguration.Output,
            CompareFaceOutput.From(imagePath1, imagePath2, result));

        // 顔未検出(NoFaceInImage1 / NoFaceInImage2)も成功。0 で終わる(要件 7.9)。
        return ExitCodes.Success;
    }
}
