using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// 顔認証(顔埋め込みの抽出と比較)を提供する公開 API。
/// 顔検出は <see cref="FaceDetector"/> を内包して再利用し、埋め込みモデルの推論セッションを所有する(design §6)。
/// CompareFacesAsync と path/bytes オーバーロードは後続タスクで追加する。
/// </summary>
public sealed class FaceRecognizer : IDisposable
{
    private readonly FaceDetector _detector;
    private readonly InferenceSession _embeddingSession;

    // 埋め込み次元 D は API 契約(要件 3.6)の要。構築後不変で保持し、後続タスクの推論で参照する。
    private readonly EmbeddingModelSpec _embeddingSpec;

    // Why volatile: Dispose と(後続タスクの)推論呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する(FaceDetector と同一方針)。
    private volatile bool _disposed;

    /// <summary>
    /// 検出モデルと埋め込みモデルをそれぞれロードして構築する。検出側は <see cref="FaceDetector"/> に委譲し、
    /// 埋め込み側はメタデータから入出力仕様と次元 D を判別する(design §6)。
    /// </summary>
    /// <param name="detectorModelPath">顔検出 ONNX モデルのファイルパス。</param>
    /// <param name="embeddingModelPath">顔埋め込み ONNX モデルのファイルパス。</param>
    /// <exception cref="ArgumentNullException">いずれかのパスが null(要件 2.7)。</exception>
    /// <exception cref="FileNotFoundException">いずれかのファイルが存在しない(要件 2.4)。</exception>
    /// <exception cref="NotSupportedException">いずれかのモデル形式を判別できない(要件 2.6)。</exception>
    public FaceRecognizer(string detectorModelPath, string embeddingModelPath)
    {
        // 事前条件: 両パスの null 検査を最優先で行い、重いモデルロードに入る前に弾く(design §6)。
        ArgumentNullException.ThrowIfNull(detectorModelPath);
        ArgumentNullException.ThrowIfNull(embeddingModelPath);

        // 検出側を先に構築する。パス存在検査・FileNotFoundException・形式判別・ORT 透過は FaceDetector に一元化し重複させない。
        FaceDetector detector = new(detectorModelPath);
        try
        {
            if (!File.Exists(embeddingModelPath))
            {
                throw new FileNotFoundException("モデルファイルが見つかりません。", embeddingModelPath);
            }

            // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.5)。
            InferenceSession embeddingSession = new(embeddingModelPath);
            try
            {
                _embeddingSpec = ModelIntrospector.IntrospectEmbedding(embeddingSession);
            }
            catch
            {
                // Why: 形式判別失敗時に埋め込みセッションをリークさせないため、送出前に破棄する。
                embeddingSession.Dispose();
                throw;
            }

            _embeddingSession = embeddingSession;
        }
        catch
        {
            // Why: 部分構築を残さない。埋め込み側の構築が失敗したら、生成済みの内包 FaceDetector を破棄してから送出する(リーク防止。design §6)。
            detector.Dispose();
            throw;
        }

        _detector = detector;
    }

    /// <summary>
    /// 2 つの顔埋め込みのコサイン類似度を返す。値域は [-1, 1]。
    /// </summary>
    /// <param name="embedding1">埋め込みベクトル 1(モデル出力の生ベクトル)。</param>
    /// <param name="embedding2">埋め込みベクトル 2(モデル出力の生ベクトル)。</param>
    /// <returns>コサイン類似度 [-1, 1]。いずれかがゼロベクトルの場合は 0。</returns>
    /// <exception cref="ArgumentException">2 つの次元(長さ)が一致しない場合。</exception>
    public static float CompareEmbeddings(ReadOnlySpan<float> embedding1, ReadOnlySpan<float> embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException(
                $"埋め込みの次元が一致しません(embedding1={embedding1.Length}, embedding2={embedding2.Length})。",
                nameof(embedding2));
        }

        // 生ベクトルから内積とノルムを 1 パスで算出する(L2 正規化はここでのみ行う)。
        float dot = 0f;
        float squaredNorm1 = 0f;
        float squaredNorm2 = 0f;
        for (int i = 0; i < embedding1.Length; i++)
        {
            float v1 = embedding1[i];
            float v2 = embedding2[i];
            dot += v1 * v2;
            squaredNorm1 += v1 * v1;
            squaredNorm2 += v2 * v2;
        }

        // ゼロベクトルのコサイン類似度は数学的に未定義のため安全側の 0 を返す(要件 5.4)。
        if (squaredNorm1 == 0f || squaredNorm2 == 0f)
        {
            return 0f;
        }

        float similarity = dot / (MathF.Sqrt(squaredNorm1) * MathF.Sqrt(squaredNorm2));

        // 浮動小数点誤差で [-1, 1] を僅かに超えうるためクランプする(要件 5.2)。
        return Math.Clamp(similarity, -1f, 1f);
    }

    /// <summary>
    /// 画像から顔埋め込みベクトルを抽出する。<paramref name="faceRegion"/> 省略時は顔検出を行い最高信頼度の顔を、
    /// 指定時は検出せずその領域を対象に切り出す(design §4・§6)。
    /// </summary>
    /// <param name="image">BGR の入力画像。所有権は移動しない。</param>
    /// <param name="faceRegion">埋め込み対象の顔領域。省略時は検出結果の最高信頼度の顔を使う。</param>
    /// <param name="detectionThreshold">顔検出の信頼度閾値(0.0〜1.0)。faceRegion 省略時のみ検出に使用。</param>
    /// <param name="nmsThreshold">顔検出の NMS IoU 閾値(0.0〜1.0)。faceRegion 省略時のみ検出に使用。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>埋め込みと使用した顔。faceRegion 省略で未検出のときは両者 null(要件 3.5)。faceRegion 指定時は Face=null(要件 3.3)。</returns>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 6.5)。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> が null(要件 1.6)。</exception>
    /// <exception cref="ArgumentException">空の Mat、閾値が範囲外(要件 3.9)、または faceRegion が空・非交差(要件 3.7)。</exception>
    public Task<FaceEmbeddingResult> ExtractEmbeddingAsync(
        Mat image,
        RectangleF? faceRegion = null,
        float detectionThreshold = 0.7f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // 検出結果に依存しない事前条件はすべて呼び出し時点で同期送出する(design §6。Task へ回さない)。
        // 順序: 破棄済み → 画像 null/空 → 閾値範囲 → faceRegion 妥当性(指定時)。
        ThrowIfDisposed();
        ImageDecoder.EnsureValid(image);
        EnsureThresholdInRange(detectionThreshold, nameof(detectionThreshold));
        EnsureThresholdInRange(nmsThreshold, nameof(nmsThreshold));
        if (faceRegion is { } region)
        {
            // 要件 3.9 と同様、faceRegion 指定で検出を省略する経路でも妥当性を検出前に検査する(要件 3.7)。
            FaceCropper.Validate(region, image.Width, image.Height);
        }

        return ExtractEmbeddingCoreAsync(image, faceRegion, detectionThreshold, nmsThreshold, cancellationToken);
    }

    // 同期ガード通過後の非同期本体。検出(省略時)→ 未検出の早期返却 → Task.Run 内で切り出し・前処理・推論(design §6)。
    private async Task<FaceEmbeddingResult> ExtractEmbeddingCoreAsync(
        Mat image,
        RectangleF? faceRegion,
        float detectionThreshold,
        float nmsThreshold,
        CancellationToken cancellationToken)
    {
        RectangleF region;
        FaceDetection? face;

        if (faceRegion is { } specified)
        {
            // faceRegion 指定時は検出をスキップし Face=null とする(要件 3.3)。
            region = specified;
            face = null;
        }
        else
        {
            // faceRegion 省略時は検出し、信頼度降順の先頭(最高信頼度)を使う(要件 3.1・3.2。DetectAsync は降順契約)。
            IReadOnlyList<FaceDetection> detections = await _detector
                .DetectAsync(image, detectionThreshold, nmsThreshold, cancellationToken)
                .ConfigureAwait(false);
            if (detections.Count == 0)
            {
                // 未検出は例外ではなく両者 null の結果で返す(要件 3.5)。
                return new FaceEmbeddingResult(null, null);
            }

            face = detections[0];
            region = face.BBox;
        }

        // CPU 束縛の切り出し・前処理・推論をスレッドプールへ退避する(design §6 実装方針)。
        float[] embedding = await Task
            .Run(() => RunEmbedding(image, region, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        return new FaceEmbeddingResult(embedding, face);
    }

    // 切り出し → 前処理 → 埋め込み推論 → float[] 抽出。前後にキャンセルチェックポイントを置く(design §6)。
    private float[] RunEmbedding(Mat image, RectangleF region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 中心保持の正方形で切り出す(要件 3.4)。返却 ROI は独立 Mat のため使用後に破棄する。
        using Mat cropped = FaceCropper.CropSquare(image, region);

        DenseTensor<float> tensor = EmbeddingPreprocessor.Preprocess(cropped, _embeddingSpec);

        cancellationToken.ThrowIfCancellationRequested();

        NamedOnnxValue[] inputs = [NamedOnnxValue.CreateFromTensor(_embeddingSpec.InputName, tensor)];

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _embeddingSession.Run(inputs);

        cancellationToken.ThrowIfCancellationRequested();

        Tensor<float> output = outputs.First(v => v.Name == _embeddingSpec.OutputName).AsTensor<float>();

        // Why not: 出力テンソルは outputs のネイティブメモリに紐づくため、破棄前に float[] へ写す(長さ = Dimension。要件 3.6)。
        return output.ToArray();
    }

    // 要件 3.9: 閾値は 0.0〜1.0。範囲外は ArgumentException(FaceDetector と同一契約。ArgumentOutOfRangeException にしない)。
    private static void EnsureThresholdInRange(float value, string paramName)
    {
        if (value is < 0f or > 1f)
        {
            throw new ArgumentException("閾値は 0.0〜1.0 の範囲で指定してください。", paramName);
        }
    }

    /// <summary>内包する検出器と埋め込み推論セッションを解放する。二重呼び出しは安全(要件 6.4)。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector.Dispose();
        _embeddingSession.Dispose();
    }

    // 破棄済みインスタンスへの非同期メソッド呼び出しを ObjectDisposedException で弾く(要件 6.5)。
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
