using System.CommandLine;
using Recognizer.Cli.Errors;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Commands;

/// <summary>
/// <c>detect-object</c> コマンドの定義と Action(要件 4.1〜4.8)。
/// ライブラリ呼び出しと DTO 組み立てのみを持ち、JSON の形は <see cref="Output"/> に委ねる(design §2)。
/// </summary>
internal static class DetectObjectCommand
{
    /// <summary>
    /// <c>detect-object &lt;image&gt; --model &lt;path&gt; [--classes &lt;path&gt;] [--confidence 0.5] [--nms 0.5]</c> を組み立てる。
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

        // Why not: ファイルの存在を Option の段階で検証しない(Option<FileInfo> や Validators を使わない)。
        // --classes のファイル不在は使用法エラー(終了コード 2)ではなく実行時エラー(要件 7.6)であり、
        // 分類は ClassNamesFile が付けた code を RuntimeErrorMapper が採る経路に一本化する(design §8.1)。
        Option<string?> classes = new("--classes")
        {
            Description = "クラス名ファイル(1 行 1 クラス名)のパス。省略時はライブラリの既定解決に委ねる。",
        };

        // Why not: 既定値に detect-face の 0.7 を流用しない。detect-object の --confidence の既定は 0.5 で、
        // ライブラリ(ObjectDetector.DetectAsync)の既定とも一致する(要件 2.3)。
        Option<float> confidence = ThresholdOption.Create("--confidence", 0.5f, collector);
        Option<float> nms = ThresholdOption.Create("--nms", 0.5f, collector);

        Command command = new("detect-object", "画像から物体を検出し、クラス・bbox・信頼度を JSON で出力する。")
        {
            image,
            model,
            classes,
            confidence,
            nms,
        };

        command.SetAction((parseResult, cancellationToken)
            => ExecuteAsync(parseResult, image, model, classes, confidence, nms, cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        ParseResult parseResult,
        Argument<string> image,
        Option<string> model,
        Option<string?> classes,
        Option<float> confidence,
        Option<float> nms,
        CancellationToken cancellationToken)
    {
        // Why not: Path.GetFullPath などで正規化しない。image は位置引数の文字列をそのまま出力する契約
        // (要件 6.5)であり、検出にもこの文字列をそのまま使う。
        string imagePath = parseResult.GetValue(image)!;

        // Why not: ObjectDetector の生成後に読まない。モデルのロードは重く、クラス名ファイルの不在という
        // 先に分かる失敗をその後ろに置く理由がない(design §8.1)。
        // Why not: --classes 省略時に CLI が既定のクラス名を用意しない。null を渡してライブラリの既定解決
        // (80 クラスなら COCO 名、それ以外は class_{id})に委ねる(要件 4.5)。
        IReadOnlyList<string>? classNames = parseResult.GetValue(classes) is { } classesPath
            ? ClassNamesFile.Read(classesPath)
            : null;

        using ObjectDetector detector = new(parseResult.GetValue(model)!, classNames);
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
