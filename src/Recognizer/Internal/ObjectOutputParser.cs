using System.Drawing;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Recognizer.Internal;

/// <summary>
/// パース済みの物体検出候補(レターボックス空間・左上形式)。
/// レターボックス逆変換・クリップとクラス名解決は呼び出し側(<c>ObjectDetector</c>)の責務。
/// </summary>
/// <param name="Box">左上形式 bbox(レターボックス空間)。</param>
/// <param name="Confidence">合成後の信頼度。<c>confidenceThreshold</c> 以上であることが保証される。</param>
/// <param name="ClassId">最大クラススコアの添字(argmax)。</param>
internal readonly record struct ObjectCandidate(RectangleF Box, float Confidence, int ClassId);

/// <summary>
/// 物体検出モデル出力テンソル → 候補列。レイアウト吸収(転置 4+C / 標準 5+C)、bbox 中心形式 → 左上形式、
/// argmax によるクラス判定・信頼度合成、信頼度フィルタ(要件 2.4, 2.5, 4.1)を担う無状態部品。
/// 形式・F・C・objectness 有無の確定は <see cref="ModelIntrospector.ClassifyObjectOutput"/>(実形状判定)に委譲する。
/// </summary>
internal static class ObjectOutputParser
{
    // bbox の特徴数(cx, cy, w, h)。クラススコアはこの直後、または objectness の直後から始まる。
    private const int BoxFeatureCount = 4;

    /// <summary>
    /// 出力テンソルをパースし、合成後の信頼度が <paramref name="confidenceThreshold"/> 以上の候補のみを返す。
    /// </summary>
    /// <param name="output">推論出力(実形状が確定済みであること)。</param>
    /// <param name="confidenceThreshold">この値未満の候補は除外する(合成後の信頼度で比較)。</param>
    /// <returns>
    /// 候補列(整列・クラス単位 NMS は未適用。すべて信頼度 ≥ threshold)と、判別した出力仕様。
    /// クラス数 C(<see cref="ObjectOutputSpec.ClassCount"/>)は呼び出し側のクラス名解決に用いる(design §6)。
    /// </returns>
    /// <exception cref="NotSupportedException">出力形状が非対応(rank≠3・先頭次元≠1・C&lt;1)。</exception>
    public static (IReadOnlyList<ObjectCandidate> Candidates, ObjectOutputSpec Spec) Parse(
        Tensor<float> output,
        float confidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(output);

        ObjectOutputSpec spec = ModelIntrospector.ClassifyObjectOutput(output.Dimensions);

        // クラススコアの開始オフセット: 4+C は 4、5+C(objectness あり)は 5。
        int classOffset = spec.HasObjectness ? BoxFeatureCount + 1 : BoxFeatureCount;

        List<ObjectCandidate> candidates = new();
        for (int n = 0; n < spec.CandidateCount; n++)
        {
            // argmax: クラススコア C 個を走査して最大スコアと添字を得る(要件 2.4, 2.5)。
            float maxClassScore = float.NegativeInfinity;
            int classId = 0;
            for (int c = 0; c < spec.ClassCount; c++)
            {
                float score = FeatureAt(output, spec, n, classOffset + c);
                if (score > maxClassScore)
                {
                    maxClassScore = score;
                    classId = c;
                }
            }

            // 4+C: 信頼度 = 最大クラススコア。5+C: objectness × 最大クラススコア。
            float confidence = spec.HasObjectness
                ? FeatureAt(output, spec, n, BoxFeatureCount) * maxClassScore
                : maxClassScore;

            // 要件 4.1: 閾値未満を除外(事後条件: 返却候補はすべて合成後 threshold 以上)。
            if (confidence < confidenceThreshold)
            {
                continue;
            }

            float cx = FeatureAt(output, spec, n, 0);
            float cy = FeatureAt(output, spec, n, 1);
            float w = FeatureAt(output, spec, n, 2);
            float h = FeatureAt(output, spec, n, 3);

            // 中心形式 (cx,cy,w,h) → 左上形式(レターボックス空間のまま。逆変換は ObjectDetector の責務)。
            RectangleF box = new(cx - (w / 2f), cy - (h / 2f), w, h);

            candidates.Add(new ObjectCandidate(box, confidence, classId));
        }

        return (candidates, spec);
    }

    // レイアウトを吸収して候補 n・特徴 f の値を返す。行優先の平坦インデックスで参照する(FaceOutputParser と同方式)。
    private static float FeatureAt(Tensor<float> output, ObjectOutputSpec spec, int n, int feature)
    {
        int flatIndex = spec.Format == OutputFormat.Transposed
            ? (feature * spec.CandidateCount) + n  // [1, F, N]
            : (n * spec.FeatureCount) + feature;   // [1, N, F]
        return output.GetValue(flatIndex);
    }
}
