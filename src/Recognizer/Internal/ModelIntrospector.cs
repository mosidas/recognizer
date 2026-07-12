using Microsoft.ML.OnnxRuntime;

namespace Recognizer.Internal;

/// <summary>検出モデル出力の形状形式(転置 [1,F,N] / 標準 [1,N,F])。</summary>
internal enum OutputFormat
{
    /// <summary>[1, F, N]。特徴軸が候補軸より前。</summary>
    Transposed,

    /// <summary>[1, N, F]。候補軸が特徴軸より前。</summary>
    Standard,
}

/// <summary>
/// 出力形状から判別した形式・特徴数 F・候補数 N。
/// </summary>
/// <param name="Format">転置 / 標準。</param>
/// <param name="FeatureCount">特徴数 F(5 または 20)。</param>
/// <param name="CandidateCount">候補数 N。</param>
internal readonly record struct OutputSpec(OutputFormat Format, int FeatureCount, int CandidateCount);

/// <summary>
/// 物体検出モデルの出力形状から判別した形式・特徴数 F・候補数 N・クラス数 C・objectness 有無。
/// </summary>
/// <param name="Format">転置(4+C)/ 標準(5+C)。</param>
/// <param name="FeatureCount">特徴数 F。</param>
/// <param name="CandidateCount">候補数 N。</param>
/// <param name="ClassCount">クラス数 C(転置 F−4 / 標準 F−5)。</param>
/// <param name="HasObjectness">objectness 列を持つか(標準形式のみ true)。</param>
internal readonly record struct ObjectOutputSpec(
    OutputFormat Format,
    int FeatureCount,
    int CandidateCount,
    int ClassCount,
    bool HasObjectness);

/// <summary>
/// ONNX メタデータから <see cref="DetectionModelSpec"/> を判別する無状態部品。
/// 判別規則は design.md §6 の (a)〜(f)。
/// </summary>
internal static class ModelIntrospector
{
    // 顔検出モデルの特徴数 F。5 = bbox(4) + conf、20 = bbox + conf + ランドマーク 5 点 × [x,y,conf]。
    private const int FeatureCountWithoutLandmarks = 5;
    private const int FeatureCountWithLandmarks = 20;

    // チャネル(RGB/BGR)軸の値。
    private const int ChannelCount = 3;

    // 動的軸が確定していないときに用いる既定の入力サイズ(検出は 640、埋め込みは 112)。
    private const int DefaultDetectionInputSize = 640;
    private const int DefaultEmbeddingInputSize = 112;

    // 物体検出の特徴数の基底。転置(YOLOv8/v11)は bbox(4)+C、標準(YOLOv5)は bbox(4)+objectness(1)+C。
    private const int ObjectFeatureBaseTransposed = 4;
    private const int ObjectFeatureBaseStandard = 5;

    /// <summary>
    /// 入力メタデータから入力レイアウト・サイズ・入出力名を判別する。
    /// 出力形状が静的な場合は規則 (d) で F を検査し、非対応形式を構築時に早期検出する(要件 2.6)。
    /// </summary>
    /// <exception cref="NotSupportedException">規則 (a)〜(f) のいずれにも当てはまらないモデル。</exception>
    public static DetectionModelSpec Introspect(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 規則 (a)〜(c): 入力レイアウト・サイズ・入力名を判別(顔/物体で共通)。
        (TensorLayout layout, int inputWidth, int inputHeight, string inputName) = IntrospectInput(session, DefaultDetectionInputSize);

        // 規則 (f): 出力は 1 個を要求(複数出力の YOLOv3 系はスコープ外)。
        IReadOnlyDictionary<string, NodeMetadata> outputMetadata = session.OutputMetadata;
        if (outputMetadata.Count != 1)
        {
            throw new NotSupportedException($"出力テンソルは 1 個を要求しますが {outputMetadata.Count} 個でした(複数出力モデルは非対応)。");
        }

        (string? outputName, NodeMetadata? outputMeta) = Single(outputMetadata);
        if (!outputMeta.IsTensor)
        {
            throw new NotSupportedException("出力がテンソルではありません。");
        }

        int[] outputDims = outputMeta.Dimensions;
        if (outputDims.Length != 3 || outputDims[0] != 1)
        {
            throw new NotSupportedException($"出力テンソルは [1, ...] の rank 3 を要求しますが形状 [{string.Join(",", outputDims)}] でした。");
        }

        // 規則 (d)(e): 特徴・候補軸が静的なら構築時に F を検査(早期検出)。
        // 動的軸を含む場合は確定を保留し、初回 Run の実形状で判定する(FaceOutputParser 側)。
        bool outputIsDynamic = outputDims[1] <= 0 || outputDims[2] <= 0;
        if (!outputIsDynamic)
        {
            _ = ClassifyOutput(outputDims);
        }

        return new DetectionModelSpec(layout, inputWidth, inputHeight, inputName, outputName);
    }

    /// <summary>
    /// 出力の実形状に規則 (d) を適用して形式・F・N を判別する純粋関数。
    /// 構築時の早期検出と、初回 Run 時の <c>FaceOutputParser</c> の双方から再利用する。
    /// </summary>
    /// <param name="shape">出力テンソルの確定済み形状(動的軸を含まないこと)。</param>
    /// <exception cref="NotSupportedException">rank ≠ 3・先頭次元 ≠ 1・F が {5, 20} 外。</exception>
    public static OutputSpec ClassifyOutput(ReadOnlySpan<int> shape)
    {
        if (shape.Length != 3 || shape[0] != 1)
        {
            throw new NotSupportedException($"出力テンソルは [1, ...] の rank 3 を要求します(rank {shape.Length})。");
        }

        int d1 = shape[1];
        int d2 = shape[2];

        // 規則 (d): 両方一致は転置優先のため d1 を先に判定する。
        if (IsFeatureCount(d1))
        {
            return new OutputSpec(OutputFormat.Transposed, d1, d2);
        }

        if (IsFeatureCount(d2))
        {
            return new OutputSpec(OutputFormat.Standard, d2, d1);
        }

        throw new NotSupportedException($"出力特徴数 F を判別できません(d1={d1}, d2={d2}、対応は {FeatureCountWithoutLandmarks} または {FeatureCountWithLandmarks})。");
    }

    private static bool IsFeatureCount(int value)
        => value == FeatureCountWithoutLandmarks || value == FeatureCountWithLandmarks;

    /// <summary>
    /// 物体検出モデルのメタデータから入力レイアウト・サイズ・入出力名を判別する(要件 2.1, 2.2)。
    /// 入力判別 (a)〜(c) は <see cref="Introspect"/> と共通。出力静的なら規則 (o-d) で早期検証する(要件 2.8)。
    /// </summary>
    /// <exception cref="NotSupportedException">規則 (a)〜(c)・(o-d)〜(o-f) のいずれにも当てはまらないモデル。</exception>
    public static DetectionModelSpec IntrospectObject(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 規則 (a)〜(c): 入力レイアウト・サイズ・入力名を判別(顔/物体で共通)。
        (TensorLayout layout, int inputWidth, int inputHeight, string inputName) = IntrospectInput(session, DefaultDetectionInputSize);

        // 規則 (o-f): 出力は 1 個を要求(複数出力の YOLOv3 系はスコープ外)。
        IReadOnlyDictionary<string, NodeMetadata> outputMetadata = session.OutputMetadata;
        if (outputMetadata.Count != 1)
        {
            throw new NotSupportedException($"出力テンソルは 1 個を要求しますが {outputMetadata.Count} 個でした(複数出力モデルは非対応)。");
        }

        (string? outputName, NodeMetadata? outputMeta) = Single(outputMetadata);
        if (!outputMeta.IsTensor)
        {
            throw new NotSupportedException("出力がテンソルではありません。");
        }

        int[] outputDims = outputMeta.Dimensions;
        if (outputDims.Length != 3 || outputDims[0] != 1)
        {
            throw new NotSupportedException($"出力テンソルは [1, ...] の rank 3 を要求しますが形状 [{string.Join(",", outputDims)}] でした。");
        }

        // 規則 (o-d)〜(o-g): 特徴・候補軸が静的なら構築時に早期検証。
        // 動的軸を含む場合は確定を保留し、初回 Run の実形状で ClassifyObjectOutput を適用する(呼び出し側)。
        bool outputIsDynamic = outputDims[1] <= 0 || outputDims[2] <= 0;
        if (!outputIsDynamic)
        {
            _ = ClassifyObjectOutput(outputDims);
        }

        return new DetectionModelSpec(layout, inputWidth, inputHeight, inputName, outputName);
    }

    /// <summary>
    /// 物体検出出力の実形状に規則 (o-d)〜(o-f) を適用して形式・F・N・C・objectness 有無を判別する純粋関数。
    /// 構築時の早期検出と、初回 Run 時の実形状判定の双方から再利用する(要件 2.3, 2.8)。
    /// </summary>
    /// <param name="shape">出力テンソルの確定済み形状(動的軸を含まないこと)。</param>
    /// <exception cref="NotSupportedException">rank ≠ 3・先頭次元 ≠ 1・転置 F&lt;5・標準 F&lt;6(C &lt; 1)。</exception>
    public static ObjectOutputSpec ClassifyObjectOutput(ReadOnlySpan<int> shape)
    {
        if (shape.Length != 3 || shape[0] != 1)
        {
            throw new NotSupportedException($"出力テンソルは [1, ...] の rank 3 を要求します(rank {shape.Length})。");
        }

        int d1 = shape[1];
        int d2 = shape[2];

        // 規則 (o-d): 小さい方の次元を F、大きい方を N とする。d1 = d2 は転置形式を優先する。
        OutputFormat format;
        int featureCount;
        int candidateCount;
        if (d1 <= d2)
        {
            format = OutputFormat.Transposed;
            featureCount = d1;
            candidateCount = d2;
        }
        else
        {
            format = OutputFormat.Standard;
            featureCount = d2;
            candidateCount = d1;
        }

        // 規則 (o-e): 標準(YOLOv5)のみ objectness 列を持つ。C = 転置 F−4 / 標準 F−5。
        bool hasObjectness = format == OutputFormat.Standard;
        int featureBase = hasObjectness ? ObjectFeatureBaseStandard : ObjectFeatureBaseTransposed;
        int classCount = featureCount - featureBase;

        // 規則 (o-f): C ≧ 1 が成立しない(転置 F<5 / 標準 F<6)形状は非対応。
        if (classCount < 1)
        {
            throw new NotSupportedException($"出力クラス数 C を判別できません(d1={d1}, d2={d2}、{format} で F={featureCount} は C≧1 を満たしません)。");
        }

        return new ObjectOutputSpec(format, featureCount, candidateCount, classCount, hasObjectness);
    }

    /// <summary>
    /// 埋め込みモデルのメタデータから入力仕様・出力名・埋め込み次元 D を判別する(要件 2.1, 2.2, 2.3, 2.6)。
    /// 入力判別 (e-a) は検出/物体と共通部を再利用し、既定サイズは 112(design §6 (e-a))。
    /// 出力は rank 1 [D] または rank 2 [1, D](D は静的正値)を要求する(規則 (e-b))。
    /// 次元は API 契約(要件 3.6)の要のため構築時に確定させ、動的 D は非対応とする(規則 (e-d))。
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// 入力判別不能・複数出力・出力 rank 3 以上・rank 2 で先頭次元 ≠ 1・D が動的(≤ 0)。
    /// </exception>
    public static EmbeddingModelSpec IntrospectEmbedding(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 規則 (e-a): 入力レイアウト・サイズ・入力名を判別(既定サイズは埋め込みの 112)。
        (TensorLayout layout, int inputWidth, int inputHeight, string inputName) = IntrospectInput(session, DefaultEmbeddingInputSize);

        // 規則 (e-d): 出力は 1 個を要求(複数出力はスコープ外)。
        IReadOnlyDictionary<string, NodeMetadata> outputMetadata = session.OutputMetadata;
        if (outputMetadata.Count != 1)
        {
            throw new NotSupportedException($"出力テンソルは 1 個を要求しますが {outputMetadata.Count} 個でした(複数出力モデルは非対応)。");
        }

        (string? outputName, NodeMetadata? outputMeta) = Single(outputMetadata);
        if (!outputMeta.IsTensor)
        {
            throw new NotSupportedException("出力がテンソルではありません。");
        }

        // 規則 (e-b): rank 1 [D] または rank 2 [1, D] の末尾を次元 D とする。
        int[] outputDims = outputMeta.Dimensions;
        int dimension;
        if (outputDims.Length == 1)
        {
            dimension = outputDims[0];
        }
        else if (outputDims.Length == 2 && outputDims[0] == 1)
        {
            dimension = outputDims[1];
        }
        else
        {
            throw new NotSupportedException($"埋め込み出力は [D] または [1, D] を要求しますが形状 [{string.Join(",", outputDims)}] でした。");
        }

        // 規則 (e-d): D が動的軸(≤ 0)なら構築時に次元を確定できず非対応。
        if (dimension <= 0)
        {
            throw new NotSupportedException($"埋め込み次元 D を確定できません(形状 [{string.Join(",", outputDims)}] の D が動的軸です)。");
        }

        return new EmbeddingModelSpec(layout, inputWidth, inputHeight, inputName, outputName, dimension);
    }

    /// <summary>
    /// 入力メタデータから規則 (a)〜(c) でレイアウト・サイズ・入力名を判別する共通部。
    /// <see cref="Introspect"/> / <see cref="IntrospectObject"/> の双方から呼ぶ(挙動は従来の Introspect と同一)。
    /// </summary>
    /// <exception cref="NotSupportedException">入力が 1 個でない・非テンソル・rank ≠ 4・チャネル軸不明。</exception>
    private static (TensorLayout Layout, int InputWidth, int InputHeight, string InputName) IntrospectInput(InferenceSession session, int defaultInputSize)
    {
        // 規則 (a): 入力はテンソル 1 個・rank 4 を要求する。
        IReadOnlyDictionary<string, NodeMetadata> inputMetadata = session.InputMetadata;
        if (inputMetadata.Count != 1)
        {
            throw new NotSupportedException($"入力テンソルは 1 個を要求しますが {inputMetadata.Count} 個でした。");
        }

        (string? inputName, NodeMetadata? inputMeta) = Single(inputMetadata);
        if (!inputMeta.IsTensor)
        {
            throw new NotSupportedException("入力がテンソルではありません。");
        }

        int[] inputDims = inputMeta.Dimensions;
        if (inputDims.Length != 4)
        {
            throw new NotSupportedException($"入力テンソルは rank 4 を要求しますが rank {inputDims.Length} でした。");
        }

        // 規則 (a): チャネル軸(値 3)の位置でレイアウトを判別。両形一致(H=W=3 等)は NCHW を優先。
        TensorLayout layout;
        int height;
        int width;
        if (inputDims[1] == ChannelCount)
        {
            layout = TensorLayout.Nchw;
            height = inputDims[2];
            width = inputDims[3];
        }
        else if (inputDims[3] == ChannelCount)
        {
            layout = TensorLayout.Nhwc;
            height = inputDims[1];
            width = inputDims[2];
        }
        else
        {
            throw new NotSupportedException("入力テンソルのチャネル軸(値 3)を特定できませんでした。");
        }

        // 規則 (b)(c): 静的軸はその値、動的軸(≤ 0)は既定サイズ(検出 640 / 埋め込み 112)にする。
        int inputHeight = height > 0 ? height : defaultInputSize;
        int inputWidth = width > 0 ? width : defaultInputSize;

        return (layout, inputWidth, inputHeight, inputName);
    }

    private static (string Name, NodeMetadata Metadata) Single(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        foreach (KeyValuePair<string, NodeMetadata> kv in metadata)
        {
            return (kv.Key, kv.Value);
        }

        // Why not: 呼び出し側で Count == 1 を検証済みのため到達不能。
        throw new InvalidOperationException();
    }
}
