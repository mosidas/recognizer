using System.Drawing;
using Recognizer.Internal;

namespace Recognizer.Tests;

public sealed class LetterboxTests
{
    // Why: scale/pad は除算を含むため浮動小数の厳密一致を避ける
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Create_横長画像_scaleは長辺基準_pad上下に付く()
    {
        // src 1280x720 → input 640x640。scale=min(0.5, 0.888…)=0.5、resized 640x360、上下に (640-360)/2=140
        var p = LetterboxParams.Create(sourceWidth: 1280, sourceHeight: 720, inputWidth: 640, inputHeight: 640);

        Assert.Equal(0.5f, p.Scale, Tolerance);
        Assert.Equal(0f, p.PadX, Tolerance);
        Assert.Equal(140f, p.PadY, Tolerance);
    }

    [Fact]
    public void Create_縦長画像_pad左右に付く()
    {
        // src 720x1280 → input 640x640。scale=0.5、resized 360x640、左右に (640-360)/2=140
        var p = LetterboxParams.Create(sourceWidth: 720, sourceHeight: 1280, inputWidth: 640, inputHeight: 640);

        Assert.Equal(0.5f, p.Scale, Tolerance);
        Assert.Equal(140f, p.PadX, Tolerance);
        Assert.Equal(0f, p.PadY, Tolerance);
    }

    [Fact]
    public void Create_等倍_scaleは1でpadなし()
    {
        var p = LetterboxParams.Create(sourceWidth: 640, sourceHeight: 640, inputWidth: 640, inputHeight: 640);

        Assert.Equal(1f, p.Scale, Tolerance);
        Assert.Equal(0f, p.PadX, Tolerance);
        Assert.Equal(0f, p.PadY, Tolerance);
    }

    [Fact]
    public void Create_小さい画像_拡大されscaleは1超()
    {
        // src 320x160 → input 640x640。scale=min(2, 4)=2、resized 640x320、上下に (640-320)/2=160
        var p = LetterboxParams.Create(sourceWidth: 320, sourceHeight: 160, inputWidth: 640, inputHeight: 640);

        Assert.Equal(2f, p.Scale, Tolerance);
        Assert.Equal(0f, p.PadX, Tolerance);
        Assert.Equal(160f, p.PadY, Tolerance);
    }

    [Fact]
    public void InverseTransform_点_元画像ピクセル座標へ復元する()
    {
        var p = new LetterboxParams(Scale: 0.5f, PadX: 0f, PadY: 140f);

        // (100, 240) → ((100-0)/0.5, (240-140)/0.5) = (200, 200)
        var original = p.InverseTransform(new PointF(100f, 240f));

        Assert.Equal(200f, original.X, Tolerance);
        Assert.Equal(200f, original.Y, Tolerance);
    }

    [Fact]
    public void InverseTransform_矩形_元画像ピクセル座標へ復元する()
    {
        var p = new LetterboxParams(Scale: 0.5f, PadX: 0f, PadY: 140f);

        // letterbox 矩形 (100,240,50,50) → 元画像 (200,200,100,100)
        var original = p.InverseTransform(new RectangleF(100f, 240f, 50f, 50f));

        Assert.Equal(200f, original.X, Tolerance);
        Assert.Equal(200f, original.Y, Tolerance);
        Assert.Equal(100f, original.Width, Tolerance);
        Assert.Equal(100f, original.Height, Tolerance);
    }

    [Fact]
    public void InverseTransform_pad左右あり_点を復元する()
    {
        var p = new LetterboxParams(Scale: 0.5f, PadX: 140f, PadY: 0f);

        // (240, 100) → ((240-140)/0.5, (100-0)/0.5) = (200, 200)
        var original = p.InverseTransform(new PointF(240f, 100f));

        Assert.Equal(200f, original.X, Tolerance);
        Assert.Equal(200f, original.Y, Tolerance);
    }

    [Fact]
    public void ClampToBounds_点_負座標は0へ_超過は境界へ丸める()
    {
        var clamped = LetterboxParams.ClampToBounds(new PointF(-5f, 250f), width: 200, height: 200);

        Assert.Equal(0f, clamped.X, Tolerance);
        Assert.Equal(200f, clamped.Y, Tolerance);
    }

    [Fact]
    public void ClampToBounds_矩形_負の左上は0へ丸める()
    {
        var clamped = LetterboxParams.ClampToBounds(new RectangleF(-10f, -10f, 50f, 50f), width: 200, height: 200);

        // Left/Top を 0 に丸めると Right/Bottom=40 は範囲内のため幅高は 40
        Assert.Equal(0f, clamped.Left, Tolerance);
        Assert.Equal(0f, clamped.Top, Tolerance);
        Assert.Equal(40f, clamped.Width, Tolerance);
        Assert.Equal(40f, clamped.Height, Tolerance);
    }

    [Fact]
    public void ClampToBounds_矩形_幅超過は境界で切り詰める()
    {
        var clamped = LetterboxParams.ClampToBounds(new RectangleF(180f, 180f, 50f, 50f), width: 200, height: 200);

        // Right=230→200, Bottom=230→200 に丸め、幅高は 20
        Assert.Equal(180f, clamped.Left, Tolerance);
        Assert.Equal(180f, clamped.Top, Tolerance);
        Assert.Equal(20f, clamped.Width, Tolerance);
        Assert.Equal(20f, clamped.Height, Tolerance);
    }

    [Fact]
    public void ClampToBounds_矩形_完全に範囲外なら退化矩形になる()
    {
        var clamped = LetterboxParams.ClampToBounds(new RectangleF(-100f, -100f, 50f, 50f), width: 200, height: 200);

        // Right=-50, Bottom=-50 も 0 に丸められ、幅高 0 の退化矩形(負の幅高を作らない)
        Assert.Equal(0f, clamped.Left, Tolerance);
        Assert.Equal(0f, clamped.Top, Tolerance);
        Assert.Equal(0f, clamped.Width, Tolerance);
        Assert.Equal(0f, clamped.Height, Tolerance);
    }
}
