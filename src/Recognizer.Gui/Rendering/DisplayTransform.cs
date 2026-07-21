using System.Drawing;

namespace Recognizer.Gui.Rendering;

/// <summary>
/// 画像ピクセル座標(左上原点)を、アスペクト比維持で表示領域に収めた
/// プレビュー表示座標へ写す等倍スケール + 中央レターボックスの変換。
/// </summary>
public readonly record struct DisplayTransform(float Scale, float OffsetX, float OffsetY)
{
    /// <summary>
    /// uniform フィットのスケール係数と中央寄せオフセットを算出する。
    /// </summary>
    public static DisplayTransform Compute(
        float imageWidth,
        float imageHeight,
        float viewportWidth,
        float viewportHeight)
    {
        // 描画前に呼ばれた場合の不正サイズを即座に弾く(ゼロ除算・負スケールの防止)。
        if (imageWidth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), imageWidth, "画像幅は正でなければならない。");
        }

        if (imageHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(imageHeight), imageHeight, "画像高は正でなければならない。");
        }

        if (viewportWidth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), viewportWidth, "表示領域幅は正でなければならない。");
        }

        if (viewportHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight), viewportHeight, "表示領域高は正でなければならない。");
        }

        var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
        var offsetX = (viewportWidth - imageWidth * scale) / 2f;
        var offsetY = (viewportHeight - imageHeight * scale) / 2f;

        return new DisplayTransform(scale, offsetX, offsetY);
    }

    /// <summary>ピクセル座標の点を表示座標へ写す。</summary>
    public PointF Apply(PointF pixelPoint) =>
        new(pixelPoint.X * Scale + OffsetX, pixelPoint.Y * Scale + OffsetY);

    /// <summary>ピクセル座標の矩形を表示座標へ写す(位置はスケール + オフセット、サイズはスケールのみ)。</summary>
    public RectangleF Apply(RectangleF pixelBox) =>
        new(
            pixelBox.X * Scale + OffsetX,
            pixelBox.Y * Scale + OffsetY,
            pixelBox.Width * Scale,
            pixelBox.Height * Scale);
}
