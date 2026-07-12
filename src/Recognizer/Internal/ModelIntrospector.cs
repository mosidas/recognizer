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

    // 動的軸が確定していないときに用いる既定の入力サイズ。
    private const int DefaultInputSize = 640;

    /// <summary>
    /// 入力メタデータから入力レイアウト・サイズ・入出力名を判別する。
    /// 出力形状が静的な場合は規則 (d) で F を検査し、非対応形式を構築時に早期検出する(要件 2.6)。
    /// </summary>
    /// <exception cref="NotSupportedException">規則 (a)〜(f) のいずれにも当てはまらないモデル。</exception>
    public static DetectionModelSpec Introspect(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

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

        // 規則 (b)(c): 静的軸はその値、動的軸(≤ 0)は 640 を既定にする。
        int inputHeight = height > 0 ? height : DefaultInputSize;
        int inputWidth = width > 0 ? width : DefaultInputSize;

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
