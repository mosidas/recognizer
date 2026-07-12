using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// YOLO 形式 ONNX モデルによる顔検出。モデルのロードと形式判別をコンストラクタで行い、
/// 推論セッションのライフサイクルを所有する(design §6)。同一インスタンスへの並行検出を許可する。
/// </summary>
public sealed class FaceDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly DetectionModelSpec _modelSpec;

    // Why volatile: Dispose と(後続タスクの)検出呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する。
    private volatile bool _disposed;

    /// <summary>
    /// モデルをロードし、ONNX メタデータから入力レイアウト・サイズ・出力形式を判別する。
    /// </summary>
    /// <param name="modelPath">顔検出 ONNX モデルのファイルパス。</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelPath"/> が null(要件 2.7)。</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない(要件 2.4)。</exception>
    /// <exception cref="NotSupportedException">判別できないモデル形式(要件 2.6)。</exception>
    public FaceDetector(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("モデルファイルが見つかりません。", modelPath);
        }

        // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.5)。
        InferenceSession session = new(modelPath);
        try
        {
            _modelSpec = ModelIntrospector.Introspect(session);
        }
        catch
        {
            // Why: 形式判別失敗時にセッションをリークさせないため、送出前に破棄する。
            session.Dispose();
            throw;
        }

        _session = session;
    }

    /// <summary>
    /// 顔を検出し、信頼度降順の結果を返す。検出 0 件は空リスト(例外にしない。要件 3.4)。
    /// </summary>
    /// <param name="image">BGR の入力画像。所有権は移動しない。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 4.5)。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> が null(要件 1.6)。</exception>
    /// <exception cref="ArgumentException">空の Mat、または閾値が範囲外(要件 1.5, 3.9)。</exception>
    public Task<IReadOnlyList<FaceDetection>> DetectAsync(
        Mat image,
        float confidenceThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // 引数ガードは同期的に検査し、呼び出し時点で送出する(design §6。Task へ回さない)。
        ObjectDisposedException.ThrowIf(_disposed, this);
        ImageDecoder.EnsureValid(image);
        EnsureThresholdInRange(confidenceThreshold, nameof(confidenceThreshold));
        EnsureThresholdInRange(nmsThreshold, nameof(nmsThreshold));

        // CPU 束縛のパイプラインをスレッドプールへ退避する(design §6 実装方針)。
        return Task.Run(
            () => RunPipeline(image, confidenceThreshold, nmsThreshold, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// ファイルパスから画像を読み込んで顔を検出する。フォーマットは OpenCV が自動判別する(要件 1.2)。
    /// </summary>
    /// <param name="imagePath">画像ファイルのパス。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 4.5)。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="imagePath"/> が null(要件 1.6)。</exception>
    /// <exception cref="ArgumentException">パスが存在しない・画像としてデコードできない、または閾値が範囲外(要件 1.4, 3.9)。</exception>
    public Task<IReadOnlyList<FaceDetection>> DetectAsync(
        string imagePath,
        float confidenceThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // null ガードは同期(design §6 事前条件)。ImageDecoder も同一契約だが、パス固有の null をここで明示的に弾く。
        ArgumentNullException.ThrowIfNull(imagePath);

        // デコードは同期的に行う: 存在しない・画像でないパスは「呼び出し時点で ArgumentException」が契約(design §6・要件 1.4)。
        Mat image = ImageDecoder.DecodeFile(imagePath);
        return DetectOwnedImageAsync(image, confidenceThreshold, nmsThreshold, cancellationToken);
    }

    /// <summary>
    /// エンコード済み画像バイト列をデコードして顔を検出する。フォーマットは OpenCV が自動判別する(要件 1.3)。
    /// </summary>
    /// <param name="encodedImage">エンコード済み画像バイト列。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 4.5)。</exception>
    /// <exception cref="ArgumentException">空・画像としてデコードできないバイト列、または閾値が範囲外(要件 1.4, 3.9)。</exception>
    public Task<IReadOnlyList<FaceDetection>> DetectAsync(
        ReadOnlyMemory<byte> encodedImage,
        float confidenceThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // デコードは同期的に行う: 空・画像でないバイト列は「呼び出し時点で ArgumentException」が契約(design §6・要件 1.4)。
        Mat image = ImageDecoder.DecodeBytes(encodedImage);
        return DetectOwnedImageAsync(image, confidenceThreshold, nmsThreshold, cancellationToken);
    }

    // デコード済み Mat の所有権を引き取り、Mat 版へ委譲する。破棄済み・閾値ガードは Mat 版に一元化し重複させない。
    private Task<IReadOnlyList<FaceDetection>> DetectOwnedImageAsync(
        Mat image,
        float confidenceThreshold,
        float nmsThreshold,
        CancellationToken cancellationToken)
    {
        try
        {
            // Why not: Mat 版の引数ガード(破棄済み・閾値範囲外)は Task.Run 前に同期送出されるため、
            // ここでの同期委譲によりオーバーロードでも「呼び出し時点の同期送出」が維持される(design §6)。
            Task<IReadOnlyList<FaceDetection>> pipeline =
                DetectAsync(image, confidenceThreshold, nmsThreshold, cancellationToken);
            return AwaitAndDisposeAsync(pipeline, image);
        }
        catch
        {
            // Why not: Mat 版の同期ガード違反時に、このメソッドが所有する Mat をリークさせないため破棄して再送出する。
            image.Dispose();
            throw;
        }
    }

    // 所有 Mat をパイプライン完了後(正常・例外・キャンセルのいずれでも)に確実に破棄する。
    private static async Task<IReadOnlyList<FaceDetection>> AwaitAndDisposeAsync(
        Task<IReadOnlyList<FaceDetection>> pipeline,
        Mat image)
    {
        using (image)
        {
            return await pipeline.ConfigureAwait(false);
        }
    }

    // 前処理 → 推論 → パース → NMS → 逆変換・クリップ の編成。計算ロジック自体は内部部品に集約(design §7)。
    private IReadOnlyList<FaceDetection> RunPipeline(
        Mat image,
        float confidenceThreshold,
        float nmsThreshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        (DenseTensor<float> tensor, LetterboxParams letterbox) = Preprocessor.Preprocess(image, _modelSpec);

        cancellationToken.ThrowIfCancellationRequested();

        NamedOnnxValue[] inputs = [NamedOnnxValue.CreateFromTensor(_modelSpec.InputName, tensor)];

        IReadOnlyList<FaceCandidate> candidates;
        using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Tensor<float> output = outputs.First(v => v.Name == _modelSpec.OutputName).AsTensor<float>();

            // Why not: 出力テンソルは outputs のネイティブメモリに紐づくため、破棄前にパースして候補へ写す。
            candidates = FaceOutputParser.Parse(output, confidenceThreshold);
        }

        return BuildDetections(candidates, letterbox, image.Width, image.Height, nmsThreshold);
    }

    // NMS 採用(信頼度降順)候補を元画像系へ逆変換・クリップして結果 record を生成する(生成箇所を終端に限定。design §7)。
    private static IReadOnlyList<FaceDetection> BuildDetections(
        IReadOnlyList<FaceCandidate> candidates,
        LetterboxParams letterbox,
        int width,
        int height,
        float nmsThreshold)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<FaceDetection>();
        }

        (RectangleF Box, float Confidence)[] boxes = new (RectangleF, float)[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            boxes[i] = (candidates[i].Box, candidates[i].Confidence);
        }

        // NMS は採用インデックスを信頼度降順で返すため、この順序をそのまま結果に維持する(要件 3.3)。
        IReadOnlyList<int> kept = NonMaxSuppression.Apply(boxes, nmsThreshold);

        List<FaceDetection> detections = new(kept.Count);
        foreach (int index in kept)
        {
            FaceCandidate candidate = candidates[index];

            // BBox・ランドマークとも逆変換して画像境界へクリップする(要件 3.5 の座標系・境界前提)。
            RectangleF box = LetterboxParams.ClampToBounds(letterbox.InverseTransform(candidate.Box), width, height);
            FaceLandmarks? landmarks = candidate.Landmarks is { } lm
                ? MapLandmarks(lm, letterbox, width, height)
                : null;

            detections.Add(new FaceDetection(box, candidate.Confidence, landmarks));
        }

        return detections;
    }

    private static FaceLandmarks MapLandmarks(FaceLandmarks lm, LetterboxParams letterbox, int width, int height)
        => new(
            MapPoint(lm.LeftEye, letterbox, width, height),
            MapPoint(lm.RightEye, letterbox, width, height),
            MapPoint(lm.Nose, letterbox, width, height),
            MapPoint(lm.LeftMouth, letterbox, width, height),
            MapPoint(lm.RightMouth, letterbox, width, height));

    private static PointF MapPoint(PointF point, LetterboxParams letterbox, int width, int height)
        => LetterboxParams.ClampToBounds(letterbox.InverseTransform(point), width, height);

    // 要件 3.9: 閾値は 0.0〜1.0。範囲外は ArgumentException(api-spec 3.6 / design §8 の指定に合わせ ArgumentOutOfRangeException にしない)。
    private static void EnsureThresholdInRange(float value, string paramName)
    {
        if (value is < 0f or > 1f)
        {
            throw new ArgumentException("閾値は 0.0〜1.0 の範囲で指定してください。", paramName);
        }
    }

    /// <summary>推論セッションを解放する。二重呼び出しは安全(要件 4.4)。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
