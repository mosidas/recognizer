using System.Drawing;
using OpenCvSharp;
using Recognizer.Internal;
using Rect = OpenCvSharp.Rect;

namespace Recognizer.Tests;

public sealed class FaceCropperTests
{
    private static Mat Image(int width = 640, int height = 640)
        => new(height, width, MatType.CV_8UC3, Scalar.All(0));

    // 正常系: 内部領域では辺長 = 長辺 × 1.4 の正方形になる(要件 3.4)
    [Fact]
    public void CropSquare_辺長は長辺の1_4倍()
    {
        using Mat image = Image();
        // region 100x60 @ (200,200)。長辺 100 → 辺長 140。境界内なのでクリップされない。
        using Mat cropped = FaceCropper.CropSquare(image, new RectangleF(200, 200, 100, 60));

        Assert.Equal(140, cropped.Width);
        Assert.Equal(140, cropped.Height);
    }

    // 正常系: 中心を保った正方形になる(width==height かつ画素サンプリングで中心一致を確認)
    [Fact]
    public void CropSquare_中心を保持する()
    {
        using Mat image = Image();
        // region の中心 = (250, 230)。辺長 140 → 左上 (180, 160)。
        using Mat cropped = FaceCropper.CropSquare(image, new RectangleF(200, 200, 100, 60));

        Assert.Equal(cropped.Width, cropped.Height);
        // 参照 ROI と同一領域を切り出して一致を検証する。
        using var expected = new Mat(image, new Rect(180, 160, 140, 140));
        using var diff = new Mat();
        Cv2.Absdiff(cropped, expected, diff);
        Assert.Equal(0, Cv2.CountNonZero(diff.Reshape(1)));
    }

    // 正常系: 画像境界をはみ出す正方形はクリップされ、非正方形の非空 Mat になる(要件 3.4)
    [Fact]
    public void CropSquare_境界でクリップされる()
    {
        using Mat image = Image();
        // region 100x60 @ (10,10)。中心 (60,40)、辺長 140 → (-10,-30)〜(130,110) を [0,640] にクリップ。
        using Mat cropped = FaceCropper.CropSquare(image, new RectangleF(10, 10, 100, 60));

        Assert.False(cropped.Empty());
        Assert.Equal(130, cropped.Width);
        Assert.Equal(110, cropped.Height);
    }

    // 正常系: 返却 Mat は元 Mat と独立(複製)である
    [Fact]
    public void CropSquare_返却は元Matと独立()
    {
        using Mat image = Image();
        using Mat cropped = FaceCropper.CropSquare(image, new RectangleF(200, 200, 100, 60));

        cropped.SetTo(Scalar.All(255));
        // 元画像は 0 のまま(ROI 参照ではなく複製のため)。
        Assert.Equal(0, Cv2.CountNonZero(image.Reshape(1)));
    }

    // 異常系: 画像外の領域を CropSquare すると退化し ArgumentException(交差なしと同義)
    [Fact]
    public void CropSquare_画像外領域は退化してArgumentException()
    {
        using Mat image = Image();
        Assert.Throws<ArgumentException>(
            () => FaceCropper.CropSquare(image, new RectangleF(1000, 1000, 10, 10)));
    }

    // 異常系: 幅 0 以下は ArgumentException(要件 3.7)
    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void Validate_幅0以下はArgumentException(float width)
        => Assert.Throws<ArgumentException>(
            () => FaceCropper.Validate(new RectangleF(10, 10, width, 50), 640, 640));

    // 異常系: 高さ 0 以下は ArgumentException(要件 3.7)
    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    public void Validate_高さ0以下はArgumentException(float height)
        => Assert.Throws<ArgumentException>(
            () => FaceCropper.Validate(new RectangleF(10, 10, 50, height), 640, 640));

    // 異常系: 画像と交差しない領域は ArgumentException(要件 3.7)
    [Fact]
    public void Validate_非交差はArgumentException()
        => Assert.Throws<ArgumentException>(
            () => FaceCropper.Validate(new RectangleF(700, 700, 50, 50), 640, 640));

    // 正常系: 画像内で交差する妥当な領域は例外を送出しない
    [Fact]
    public void Validate_交差する領域は例外なし()
    {
        var ex = Record.Exception(
            () => FaceCropper.Validate(new RectangleF(10, 10, 100, 100), 640, 640));
        Assert.Null(ex);
    }
}
