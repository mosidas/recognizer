using System.Drawing;
using Recognizer.Internal;

namespace Recognizer.Tests;

public sealed class NonMaxSuppressionTests
{
    // Why: 浮動小数の比較には許容誤差が要る。IoU は除算を含むため厳密一致を避ける
    private const float Tolerance = 1e-4f;

    [Fact]
    public void IoU_重なりのある矩形_期待値を返す()
    {
        var a = new RectangleF(100f, 100f, 50f, 50f);
        var b = new RectangleF(105f, 105f, 50f, 50f);

        // 交差 45x45=2025、和 2500+2500-2025=2975、IoU=2025/2975≈0.6807
        float iou = NonMaxSuppression.IntersectionOverUnion(a, b);

        Assert.Equal(2025f / 2975f, iou, Tolerance);
    }

    [Fact]
    public void IoU_重なりのない矩形_ゼロを返す()
    {
        var a = new RectangleF(0f, 0f, 10f, 10f);
        var b = new RectangleF(100f, 100f, 10f, 10f);

        Assert.Equal(0f, NonMaxSuppression.IntersectionOverUnion(a, b), Tolerance);
    }

    [Fact]
    public void IoU_完全一致する矩形_1を返す()
    {
        var a = new RectangleF(10f, 20f, 30f, 40f);

        Assert.Equal(1f, NonMaxSuppression.IntersectionOverUnion(a, a), Tolerance);
    }

    [Fact]
    public void IoU_辺で接するだけの矩形_ゼロを返す()
    {
        var a = new RectangleF(0f, 0f, 10f, 10f);
        var b = new RectangleF(10f, 0f, 10f, 10f);

        // 交差幅 0 のため重なり面積なし
        Assert.Equal(0f, NonMaxSuppression.IntersectionOverUnion(a, b), Tolerance);
    }

    [Fact]
    public void Apply_高IoUの重複候補_低信頼度側が抑制される()
    {
        var candidates = new (RectangleF Box, float Confidence)[]
        {
            (new RectangleF(100f, 100f, 50f, 50f), 0.95f),
            (new RectangleF(105f, 105f, 50f, 50f), 0.90f),
        };

        // IoU≈0.6807 > 0.5 のため低信頼度の候補 1 を抑制
        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 0.5f);

        Assert.Equal(new[] { 0 }, kept);
    }

    [Fact]
    public void Apply_IoUが閾値以下の候補_両方採用される()
    {
        var candidates = new (RectangleF Box, float Confidence)[]
        {
            (new RectangleF(100f, 100f, 50f, 50f), 0.95f),
            (new RectangleF(105f, 105f, 50f, 50f), 0.90f),
        };

        // IoU≈0.6807 は閾値 0.8 を超えないため両方採用(降順維持)
        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 0.8f);

        Assert.Equal(new[] { 0, 1 }, kept);
    }

    [Fact]
    public void Apply_閾値境界_IoUが閾値と等しいとき抑制しない()
    {
        var box = new RectangleF(0f, 0f, 10f, 10f);
        var candidates = new (RectangleF Box, float Confidence)[]
        {
            (box, 0.9f),
            (box, 0.8f),
        };

        // IoU=1.0 は閾値 1.0 を「超えない」ため抑制しない(判定は厳密な超過)
        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 1.0f);

        Assert.Equal(new[] { 0, 1 }, kept);
    }

    [Fact]
    public void Apply_閾値境界_IoUが閾値を超えるとき抑制する()
    {
        var box = new RectangleF(0f, 0f, 10f, 10f);
        var candidates = new (RectangleF Box, float Confidence)[]
        {
            (box, 0.9f),
            (box, 0.8f),
        };

        // IoU=1.0 > 0.99 のため低信頼度側を抑制
        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 0.99f);

        Assert.Equal(new[] { 0 }, kept);
    }

    [Fact]
    public void Apply_未整列の入力_信頼度降順で返す()
    {
        // 互いに重ならないため抑制は発生せず、順序のみを検証できる
        var candidates = new (RectangleF Box, float Confidence)[]
        {
            (new RectangleF(0f, 0f, 10f, 10f), 0.30f),
            (new RectangleF(100f, 0f, 10f, 10f), 0.90f),
            (new RectangleF(200f, 0f, 10f, 10f), 0.60f),
        };

        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 0.5f);

        // 信頼度 0.90(idx1) > 0.60(idx2) > 0.30(idx0)
        Assert.Equal(new[] { 1, 2, 0 }, kept);
    }

    [Fact]
    public void Apply_空入力_空を返す()
    {
        var candidates = Array.Empty<(RectangleF Box, float Confidence)>();

        var kept = NonMaxSuppression.Apply(candidates, nmsThreshold: 0.5f);

        Assert.Empty(kept);
    }
}
