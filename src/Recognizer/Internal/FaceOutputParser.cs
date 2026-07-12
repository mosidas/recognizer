using System.Drawing;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Recognizer.Internal;

/// <summary>
/// パース済みの検出候補(レターボックス空間・左上形式)。
/// レターボックス逆変換とクリップは呼び出し側(<c>Letterbox</c>)の責務。
/// </summary>
/// <param name="Box">左上形式 bbox(レターボックス空間)。</param>
/// <param name="Confidence">信頼度。<c>confidenceThreshold</c> 以上であることが保証される。</param>
/// <param name="Landmarks">F=20 のときランドマーク 5 点。F=5 のとき null。</param>
internal readonly record struct FaceCandidate(RectangleF Box, float Confidence, FaceLandmarks? Landmarks);

/// <summary>
/// 検出モデル出力テンソル → 候補列。レイアウト吸収(転置/標準)、bbox 中心形式 → 左上形式、
/// 信頼度フィルタ(要件 3.1)、F=20 のランドマーク読み出し(各点 conf は破棄)を担う無状態部品。
/// 形式・F の確定は <see cref="ModelIntrospector.ClassifyOutput"/>(実形状判定)に委譲する。
/// </summary>
internal static class FaceOutputParser
{
    // F=20 のレイアウト: [cx,cy,w,h,conf, (lmX,lmY,lmConf)×5]。ランドマーク開始オフセットと点数。
    private const int LandmarkOffset = 5;
    private const int LandmarkCount = 5;
    private const int FeatureCountWithLandmarks = 20;
    private const int ConfidenceIndex = 4;

    /// <summary>
    /// 出力テンソルをパースし、信頼度が <paramref name="confidenceThreshold"/> 以上の候補のみを返す。
    /// </summary>
    /// <param name="output">推論出力(実形状が確定済みであること)。</param>
    /// <param name="confidenceThreshold">この値未満の候補は除外する。</param>
    /// <returns>候補列(整列・NMS は未適用。すべて信頼度 ≥ threshold)。</returns>
    /// <exception cref="NotSupportedException">出力形状が非対応(rank≠3・先頭次元≠1・F∉{5,20})。</exception>
    public static IReadOnlyList<FaceCandidate> Parse(Tensor<float> output, float confidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(output);

        OutputSpec spec = ModelIntrospector.ClassifyOutput(output.Dimensions);
        int featureCount = spec.FeatureCount;
        int candidateCount = spec.CandidateCount;
        bool hasLandmarks = featureCount == FeatureCountWithLandmarks;

        List<FaceCandidate> candidates = new();
        for (int n = 0; n < candidateCount; n++)
        {
            float confidence = FeatureAt(output, spec, n, ConfidenceIndex);

            // 要件 3.1: 閾値未満を除外(事後条件: 返却候補はすべて threshold 以上)。
            if (confidence < confidenceThreshold)
            {
                continue;
            }

            float cx = FeatureAt(output, spec, n, 0);
            float cy = FeatureAt(output, spec, n, 1);
            float w = FeatureAt(output, spec, n, 2);
            float h = FeatureAt(output, spec, n, 3);

            // 中心形式 (cx,cy,w,h) → 左上形式(レターボックス空間のまま。逆変換は Letterbox の責務)。
            RectangleF box = new(cx - (w / 2f), cy - (h / 2f), w, h);

            FaceLandmarks? landmarks = hasLandmarks ? ReadLandmarks(output, spec, n) : null;
            candidates.Add(new FaceCandidate(box, confidence, landmarks));
        }

        return candidates;
    }

    // F=20: ランドマーク 5 点(x,y)を読む。各点 3 番目の conf は FaceLandmarks に存在しないため破棄する。
    private static FaceLandmarks ReadLandmarks(Tensor<float> output, OutputSpec spec, int n)
    {
        Span<PointF> points = stackalloc PointF[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            int baseFeature = LandmarkOffset + (i * 3);
            float x = FeatureAt(output, spec, n, baseFeature);
            float y = FeatureAt(output, spec, n, baseFeature + 1);
            points[i] = new PointF(x, y);
        }

        return new FaceLandmarks(points[0], points[1], points[2], points[3], points[4]);
    }

    // レイアウトを吸収して候補 n・特徴 f の値を返す。行優先の平坦インデックスで参照する。
    private static float FeatureAt(Tensor<float> output, OutputSpec spec, int n, int feature)
    {
        int flatIndex = spec.Format == OutputFormat.Transposed
            ? (feature * spec.CandidateCount) + n  // [1, F, N]
            : (n * spec.FeatureCount) + feature;   // [1, N, F]
        return output.GetValue(flatIndex);
    }
}
