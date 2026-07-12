using System.Drawing;

namespace Recognizer.Internal;

/// <summary>
/// IoU に基づく貪欲 NMS(単一クラス)。状態を持たない純粋関数として提供する。
/// </summary>
internal static class NonMaxSuppression
{
    /// <summary>
    /// 2 矩形の Intersection over Union を返す。重なりが無い場合は 0。
    /// </summary>
    public static float IntersectionOverUnion(RectangleF a, RectangleF b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);

        float interWidth = right - left;
        float interHeight = bottom - top;

        // Why not: 幅か高さが非正のとき交差は存在せず、負値を面積として扱うと IoU が破綻するため 0 を返す
        if (interWidth <= 0f || interHeight <= 0f)
        {
            return 0f;
        }

        float intersection = interWidth * interHeight;
        float union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;

        // Why not: 退化矩形(面積 0)で 0 除算になるのを避ける
        if (union <= 0f)
        {
            return 0f;
        }

        return intersection / union;
    }

    /// <summary>
    /// 貪欲 NMS を適用し、採用された候補のインデックスを信頼度降順で返す。
    /// 採用済み矩形との IoU が <paramref name="nmsThreshold"/> を超える候補を抑制する。
    /// </summary>
    /// <returns>採用インデックス列(元の候補配列に対する添字)。信頼度降順。</returns>
    public static IReadOnlyList<int> Apply(
        IReadOnlyList<(RectangleF Box, float Confidence)> candidates,
        float nmsThreshold)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return Array.Empty<int>();
        }

        // 信頼度降順にインデックスを整列(元配列は変更しない)。
        int[] order = new int[candidates.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (x, y) => candidates[y].Confidence.CompareTo(candidates[x].Confidence));

        var kept = new List<int>(candidates.Count);
        var suppressed = new bool[candidates.Count];

        for (int i = 0; i < order.Length; i++)
        {
            int current = order[i];
            if (suppressed[current])
            {
                continue;
            }

            kept.Add(current);

            // 採用した矩形と強く重なる後続候補を抑制する。
            for (int j = i + 1; j < order.Length; j++)
            {
                int other = order[j];
                if (suppressed[other])
                {
                    continue;
                }

                // Why not: 閾値と等しい IoU は残す(要件どおり「超える」場合のみ抑制)
                if (IntersectionOverUnion(candidates[current].Box, candidates[other].Box) > nmsThreshold)
                {
                    suppressed[other] = true;
                }
            }
        }

        return kept;
    }
}
