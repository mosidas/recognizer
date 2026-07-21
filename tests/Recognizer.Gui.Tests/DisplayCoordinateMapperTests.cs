using System.Drawing;
using Recognizer.Gui.Rendering;

namespace Recognizer.Gui.Tests;

public sealed class DisplayCoordinateMapperTests
{
    private const float 許容誤差 = 1e-3f;

    [Theory]
    // 横長画像を正方形 viewport に収める: 幅方向が律速で min は幅比
    [InlineData(200f, 100f, 100f, 100f, 0.5f)]
    // 縦長画像を正方形 viewport に収める: 高さ方向が律速で min は高さ比
    [InlineData(100f, 200f, 100f, 100f, 0.5f)]
    // 正方形画像: どちらの比も等しい
    [InlineData(100f, 100f, 50f, 50f, 0.5f)]
    // viewport が画像より大きい(拡大)ケースでも min を選ぶ
    [InlineData(100f, 100f, 400f, 200f, 2f)]
    public void Compute_スケール係数は幅比と高さ比の最小値になる(
        float imageWidth,
        float imageHeight,
        float viewportWidth,
        float viewportHeight,
        float 期待スケール)
    {
        var t = DisplayTransform.Compute(imageWidth, imageHeight, viewportWidth, viewportHeight);

        Assert.Equal(期待スケール, t.Scale, 許容誤差);
    }

    [Fact]
    public void Compute_表示画像を中央に置くオフセットを与える()
    {
        // 200x100 の画像を 100x100 viewport へ。scale=0.5 → 表示画像は 100x50。
        // 横は viewport 一杯(offsetX=0)、縦は上下に 25 ずつのレターボックス。
        var t = DisplayTransform.Compute(200f, 100f, 100f, 100f);

        Assert.Equal(0f, t.OffsetX, 許容誤差);
        Assert.Equal(25f, t.OffsetY, 許容誤差);
    }

    [Fact]
    public void Compute_縦長画像を横長viewportへ置くと左右にオフセットを与える()
    {
        // 100x200 の画像を 400x200 viewport へ。scale=1.0 → 表示画像は 100x200。
        // 縦は viewport 一杯(offsetY=0)、横は左右に 150 ずつの pillarbox。
        var t = DisplayTransform.Compute(100f, 200f, 400f, 200f);

        Assert.Equal(1f, t.Scale, 許容誤差);
        Assert.Equal(150f, t.OffsetX, 許容誤差);
        Assert.Equal(0f, t.OffsetY, 許容誤差);
    }

    [Fact]
    public void Apply_点はスケールとオフセットで写る()
    {
        var t = DisplayTransform.Compute(200f, 100f, 100f, 100f); // scale=0.5, offset=(0,25)

        var p = t.Apply(new PointF(40f, 60f));

        Assert.Equal(20f, p.X, 許容誤差);   // 40*0.5 + 0
        Assert.Equal(55f, p.Y, 許容誤差);   // 60*0.5 + 25
    }

    [Fact]
    public void Apply_矩形はスケールとオフセットで写る()
    {
        var t = DisplayTransform.Compute(200f, 100f, 100f, 100f); // scale=0.5, offset=(0,25)

        var r = t.Apply(new RectangleF(20f, 10f, 60f, 40f));

        Assert.Equal(10f, r.X, 許容誤差);      // 20*0.5 + 0
        Assert.Equal(30f, r.Y, 許容誤差);      // 10*0.5 + 25
        Assert.Equal(30f, r.Width, 許容誤差);  // 60*0.5
        Assert.Equal(20f, r.Height, 許容誤差); // 40*0.5
    }

    [Fact]
    public void Apply_画像全体の矩形は表示画像の外接矩形に一致する()
    {
        const float imageWidth = 640f;
        const float imageHeight = 480f;
        const float viewportWidth = 320f;
        const float viewportHeight = 320f;

        var t = DisplayTransform.Compute(imageWidth, imageHeight, viewportWidth, viewportHeight);
        var 全体 = t.Apply(new RectangleF(0f, 0f, imageWidth, imageHeight));

        Assert.Equal(t.OffsetX, 全体.X, 許容誤差);
        Assert.Equal(t.OffsetY, 全体.Y, 許容誤差);
        Assert.Equal(imageWidth * t.Scale, 全体.Width, 許容誤差);
        Assert.Equal(imageHeight * t.Scale, 全体.Height, 許容誤差);
    }

    [Theory]
    [InlineData(0f, 100f, 100f, 100f)]   // 画像幅が非正
    [InlineData(100f, -1f, 100f, 100f)]  // 画像高が非正
    [InlineData(100f, 100f, 0f, 100f)]   // viewport 幅が非正
    [InlineData(100f, 100f, 100f, -5f)]  // viewport 高が非正
    public void Compute_非正サイズは例外を送出する(
        float imageWidth,
        float imageHeight,
        float viewportWidth,
        float viewportHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DisplayTransform.Compute(imageWidth, imageHeight, viewportWidth, viewportHeight));
    }
}
