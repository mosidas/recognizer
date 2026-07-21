using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Recognizer.Gui.Models;

namespace Recognizer.Gui.Services;

/// <summary>
/// コアの <see cref="FaceDetector"/> / <see cref="ObjectDetector"/> を呼び、結果を
/// モード非依存の <see cref="DetectionOverlay"/> へ写す(spec §5.2)。コアが送出する例外は
/// 結果型の <see cref="DetectionStatus"/> に写し、予期されるエラーを呼び出し側へ伝播させない。
/// </summary>
public sealed class DetectionService : IDetectionService
{
    public async Task<DetectionOutcome> RunAsync(DetectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 事前検証を先に行う: 閾値範囲外・パス空はコアの ArgumentException と衝突させず、ここで弾く(要件 1.4・1.5)。
        DetectionOutcome? invalid = request.Validate();
        if (invalid is not null)
        {
            return invalid;
        }

        // クラス名ファイルは検出前に読み込む: 失敗時は検出を実行しない(要件 3.4)。未指定はコア既定解決に委ねる(要件 3.3)。
        IReadOnlyList<string>? classNames = null;
        if (request.Mode == DetectionMode.Object && !string.IsNullOrWhiteSpace(request.ClassNamesPath))
        {
            try
            {
                classNames = ClassNamesFile.Read(request.ClassNamesPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return DetectionOutcome.Failure(
                    DetectionStatus.ClassNamesFileFailed,
                    $"クラス名ファイルを読み込めませんでした: {request.ClassNamesPath}");
            }
        }

        try
        {
            // Why: モデルロード(InferenceSession 構築)と画像デコードは同期・CPU/IO 束縛のため、
            // UI スレッドを塞がないようスレッドプールへ退避する(要件 4.1)。
            return await Task.Run(
                () => DetectAndMapAsync(request, classNames, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return DetectionOutcome.Failure(DetectionStatus.Cancelled, "検出がキャンセルされました。");
        }
        catch (FileNotFoundException)
        {
            return DetectionOutcome.Failure(
                DetectionStatus.ModelLoadFailed,
                $"モデルを読み込めませんでした: {request.ModelPath}");
        }
        // Why: 存在するが壊れた・非 ONNX のモデルファイルは InferenceSession 構築で OnnxRuntimeException を送出する。
        // これもモデルロード失敗として扱う(spec §5.2「モデルファイル不在・ロード失敗」)。
        catch (OnnxRuntimeException)
        {
            return DetectionOutcome.Failure(
                DetectionStatus.ModelLoadFailed,
                $"モデルを読み込めませんでした: {request.ModelPath}");
        }
        catch (NotSupportedException)
        {
            return DetectionOutcome.Failure(
                DetectionStatus.UnsupportedModel,
                "このモデル形式には対応していません。");
        }
        // Why not ArgumentException を先に置かない: パス空・閾値範囲外は Validate で除外済みのため、
        // ここへ到達する ArgumentException は画像デコード失敗に限られる(コアの ImageDecoder が送出)。
        catch (ArgumentException)
        {
            return DetectionOutcome.Failure(
                DetectionStatus.ImageLoadFailed,
                $"画像を読み込めませんでした: {request.ImagePath}");
        }
    }

    private static async Task<DetectionOutcome> DetectAndMapAsync(
        DetectionRequest request,
        IReadOnlyList<string>? classNames,
        CancellationToken cancellationToken)
    {
        if (request.Mode == DetectionMode.Face)
        {
            // using: 実行ごとに推論セッションを確実に破棄する(要件 4.2)。
            using FaceDetector detector = new(request.ModelPath);
            IReadOnlyList<FaceDetection> results = await detector
                .DetectAsync(request.ImagePath, request.ConfidenceThreshold, request.NmsThreshold, cancellationToken)
                .ConfigureAwait(false);
            return DetectionOutcome.Success(MapFaces(results), request.ImagePath);
        }

        using ObjectDetector objectDetector = new(request.ModelPath, classNames);
        IReadOnlyList<ObjectDetection> objectResults = await objectDetector
            .DetectAsync(request.ImagePath, request.ConfidenceThreshold, request.NmsThreshold, cancellationToken)
            .ConfigureAwait(false);
        return DetectionOutcome.Success(MapObjects(objectResults), request.ImagePath);
    }

    private static IReadOnlyList<DetectionOverlay> MapFaces(IReadOnlyList<FaceDetection> detections)
    {
        List<DetectionOverlay> overlays = new(detections.Count);
        for (int index = 0; index < detections.Count; index++)
        {
            FaceDetection detection = detections[index];

            // ランドマークは 5 点を [左目, 右目, 鼻, 口左, 口右] の順で並べる。無ければ null のまま(要件 2.3・2.4)。
            IReadOnlyList<PointF>? landmarks = detection.Landmarks is { } lm
                ? new[] { lm.LeftEye, lm.RightEye, lm.Nose, lm.LeftMouth, lm.RightMouth }
                : null;

            overlays.Add(new DetectionOverlay(detection.BBox, detection.Confidence, $"face #{index + 1}", landmarks));
        }

        return overlays;
    }

    private static IReadOnlyList<DetectionOverlay> MapObjects(IReadOnlyList<ObjectDetection> detections)
    {
        List<DetectionOverlay> overlays = new(detections.Count);
        foreach (ObjectDetection detection in detections)
        {
            overlays.Add(new DetectionOverlay(detection.BBox, detection.Confidence, detection.ClassName, Landmarks: null));
        }

        return overlays;
    }
}
