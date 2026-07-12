using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// YOLO 形式 ONNX モデルによる物体検出。モデルのロードと物体用形式判別をコンストラクタで行い、
/// 推論セッションのライフサイクルを所有する(design §6)。同一インスタンスへの並行検出を許可する。
/// </summary>
public sealed class ObjectDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly DetectionModelSpec _modelSpec;

    // Why not: classNames は防御的コピーせず参照を保持する(design §6, §10)。コピーしない理由は
    // 大きなリストの複製コスト回避と YAGNI。呼び出し側が構築後にリストを変更しない前提を契約とする。
    private readonly IReadOnlyList<string>? _classNames;

    // Why volatile: Dispose と(後続タスクの)検出呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する。
    private volatile bool _disposed;

    /// <summary>
    /// モデルをロードし、ONNX メタデータから入力レイアウト・サイズ・物体検出の出力形式を判別する。
    /// </summary>
    /// <param name="modelPath">物体検出 ONNX モデルのファイルパス。</param>
    /// <param name="classNames">
    /// クラス名リスト(省略可)。省略時はクラス数に応じて COCO 80 名または <c>"class_{id}"</c> を解決する(後続タスク)。
    /// 指定した場合、このインスタンスは参照をそのまま保持する(防御的コピーをしない)。
    /// 構築後にリストの内容を変更しないこと(変更するとクラス名解決が変わり得る。契約違反として扱う)。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="modelPath"/> が null(要件 2.9)。</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない(要件 2.6)。</exception>
    /// <exception cref="NotSupportedException">判別できないモデル形式(要件 2.8)。</exception>
    public ObjectDetector(string modelPath, IReadOnlyList<string>? classNames = null)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("モデルファイルが見つかりません。", modelPath);
        }

        // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.7)。
        InferenceSession session = new(modelPath);
        try
        {
            _modelSpec = ModelIntrospector.IntrospectObject(session);
        }
        catch
        {
            // Why: 形式判別失敗時にセッションをリークさせないため、送出前に破棄する。
            session.Dispose();
            throw;
        }

        _session = session;
        _classNames = classNames;
    }

    /// <summary>
    /// 物体を検出し、信頼度降順・クラス単位 NMS 適用済みの結果を返す。検出 0 件は空リスト(例外にしない。要件 4.4)。
    /// </summary>
    /// <param name="image">BGR の入力画像。所有権は移動しない。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 5.5)。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> が null(要件 1.6)。</exception>
    /// <exception cref="ArgumentException">空の Mat、または閾値が範囲外(要件 1.5, 4.7)。</exception>
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        Mat image,
        float confidenceThreshold = 0.5f,
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
    /// ファイルパスから画像を読み込んで物体を検出する。フォーマットは OpenCV が自動判別する(要件 1.2)。
    /// </summary>
    /// <param name="imagePath">画像ファイルのパス。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 5.5)。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="imagePath"/> が null(要件 1.6)。</exception>
    /// <exception cref="ArgumentException">パスが存在しない・画像としてデコードできない、または閾値が範囲外(要件 1.4, 4.7)。</exception>
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        string imagePath,
        float confidenceThreshold = 0.5f,
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
    /// エンコード済み画像バイト列をデコードして物体を検出する。フォーマットは OpenCV が自動判別する(要件 1.3)。
    /// </summary>
    /// <param name="encodedImage">エンコード済み画像バイト列。</param>
    /// <param name="confidenceThreshold">この値未満の候補を除外する(0.0〜1.0)。</param>
    /// <param name="nmsThreshold">NMS の IoU 閾値(0.0〜1.0)。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <exception cref="ObjectDisposedException">破棄済みインスタンス(要件 5.5)。</exception>
    /// <exception cref="ArgumentException">空・画像としてデコードできないバイト列、または閾値が範囲外(要件 1.4, 4.7)。</exception>
    public Task<IReadOnlyList<ObjectDetection>> DetectAsync(
        ReadOnlyMemory<byte> encodedImage,
        float confidenceThreshold = 0.5f,
        float nmsThreshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // デコードは同期的に行う: 空・画像でないバイト列は「呼び出し時点で ArgumentException」が契約(design §6・要件 1.4)。
        Mat image = ImageDecoder.DecodeBytes(encodedImage);
        return DetectOwnedImageAsync(image, confidenceThreshold, nmsThreshold, cancellationToken);
    }

    // デコード済み Mat の所有権を引き取り、Mat 版へ委譲する。破棄済み・閾値ガードは Mat 版に一元化し重複させない。
    private Task<IReadOnlyList<ObjectDetection>> DetectOwnedImageAsync(
        Mat image,
        float confidenceThreshold,
        float nmsThreshold,
        CancellationToken cancellationToken)
    {
        try
        {
            // Why not: Mat 版の引数ガード(破棄済み・閾値範囲外)は Task.Run 前に同期送出されるため、
            // ここでの同期委譲によりオーバーロードでも「呼び出し時点の同期送出」が維持される(design §6)。
            Task<IReadOnlyList<ObjectDetection>> pipeline =
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
    private static async Task<IReadOnlyList<ObjectDetection>> AwaitAndDisposeAsync(
        Task<IReadOnlyList<ObjectDetection>> pipeline,
        Mat image)
    {
        using (image)
        {
            return await pipeline.ConfigureAwait(false);
        }
    }

    // 前処理 → 推論 → パース → クラス単位 NMS → 逆変換・クリップ → クラス名解決 の編成(design §4)。
    private IReadOnlyList<ObjectDetection> RunPipeline(
        Mat image,
        float confidenceThreshold,
        float nmsThreshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        (DenseTensor<float> tensor, LetterboxParams letterbox) = Preprocessor.Preprocess(image, _modelSpec);

        cancellationToken.ThrowIfCancellationRequested();

        NamedOnnxValue[] inputs = [NamedOnnxValue.CreateFromTensor(_modelSpec.InputName, tensor)];

        IReadOnlyList<ObjectCandidate> candidates;
        int classCount;
        using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Tensor<float> output = outputs.First(v => v.Name == _modelSpec.OutputName).AsTensor<float>();

            // Why not: 出力テンソルは outputs のネイティブメモリに紐づくため、破棄前にパースして候補へ写す。
            // クラス数 C は動的出力(構築時保留)でも実形状から確定するため、パース結果から受け取る(design §6)。
            (candidates, ObjectOutputSpec spec) = ObjectOutputParser.Parse(output, confidenceThreshold);
            classCount = spec.ClassCount;
        }

        return BuildDetections(candidates, classCount, letterbox, image.Width, image.Height, nmsThreshold);
    }

    // クラス単位 NMS(要件 4.2)→ 全クラス採用候補を信頼度降順マージ(要件 4.3)→ 逆変換・クリップ・クラス名解決して結果を生成する。
    private IReadOnlyList<ObjectDetection> BuildDetections(
        IReadOnlyList<ObjectCandidate> candidates,
        int classCount,
        LetterboxParams letterbox,
        int width,
        int height,
        float nmsThreshold)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<ObjectDetection>();
        }

        // クラス単位 NMS: ClassId でグルーピングし、各グループ内でのみ抑制する(要件 4.2)。
        // 異なるクラスの検出は IoU にかかわらず互いに抑制されない(同座標でも別クラスなら共存)。
        Dictionary<int, List<ObjectCandidate>> groups = new();
        foreach (ObjectCandidate candidate in candidates)
        {
            if (!groups.TryGetValue(candidate.ClassId, out List<ObjectCandidate>? group))
            {
                group = new List<ObjectCandidate>();
                groups[candidate.ClassId] = group;
            }

            group.Add(candidate);
        }

        List<ObjectCandidate> kept = new(candidates.Count);
        foreach (List<ObjectCandidate> group in groups.Values)
        {
            (RectangleF Box, float Confidence)[] boxes = new (RectangleF, float)[group.Count];
            for (int i = 0; i < group.Count; i++)
            {
                boxes[i] = (group[i].Box, group[i].Confidence);
            }

            foreach (int index in NonMaxSuppression.Apply(boxes, nmsThreshold))
            {
                kept.Add(group[index]);
            }
        }

        // 全クラスの採用候補を信頼度降順にマージ整列する(要件 4.3。グループ順に依存させない)。
        kept.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));

        List<ObjectDetection> detections = new(kept.Count);
        foreach (ObjectCandidate candidate in kept)
        {
            // bbox を元画像系へ逆変換し画像境界へクリップする(要件 4.5)。
            RectangleF box = LetterboxParams.ClampToBounds(letterbox.InverseTransform(candidate.Box), width, height);
            string className = ResolveClassName(candidate.ClassId, classCount);
            detections.Add(new ObjectDetection(candidate.ClassId, className, candidate.Confidence, box));
        }

        return detections;
    }

    // クラス名解決の 4 規則(design §6): classNames 指定 → 範囲内は参照 / 範囲外は class_{id}。
    // 省略 → クラス数 80 は COCO 名 / それ以外は class_{id}。
    private string ResolveClassName(int classId, int classCount)
    {
        if (_classNames is { } names)
        {
            return classId >= 0 && classId < names.Count ? names[classId] : $"class_{classId}";
        }

        if (classCount == CocoClassNames.Names.Count)
        {
            return CocoClassNames.Names[classId];
        }

        return $"class_{classId}";
    }

    // 要件 4.7: 閾値は 0.0〜1.0。範囲外は ArgumentException(api-spec 3.5 / design §8 に合わせ ArgumentOutOfRangeException にしない)。
    private static void EnsureThresholdInRange(float value, string paramName)
    {
        if (value is < 0f or > 1f)
        {
            throw new ArgumentException("閾値は 0.0〜1.0 の範囲で指定してください。", paramName);
        }
    }

    /// <summary>推論セッションを解放する。二重呼び出しは安全(要件 5.4)。</summary>
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
