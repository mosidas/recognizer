using System.Drawing;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

namespace Recognizer.Internal;

/// <summary>
/// 対象矩形(検出 BBox または faceRegion)を中心保持の正方形へ拡張し、画像境界でクリップして
/// ROI を切り出す無状態な部品。切り出し規則は検出 BBox・faceRegion で共通(要件 3.4)。
/// </summary>
internal static class FaceCropper
{
    // 正方形の辺長 = 長辺 × (1 + 0.2 × 2)。各方向へパディング比率 0.2 ずつ拡張する(要件 3.4)。
    private const float SquareScale = 1.4f;

    /// <summary>
    /// faceRegion の妥当性を検査する。幅・高さが 0 以下、または画像矩形と交差しない場合は
    /// <see cref="ArgumentException"/> を送出する(要件 3.7)。
    /// </summary>
    public static void Validate(RectangleF faceRegion, int imageWidth, int imageHeight)
    {
        if (faceRegion.Width <= 0 || faceRegion.Height <= 0)
        {
            throw new ArgumentException("faceRegion の幅・高さは正の値でなければなりません。", nameof(faceRegion));
        }

        var imageRect = new RectangleF(0, 0, imageWidth, imageHeight);
        if (!faceRegion.IntersectsWith(imageRect))
        {
            throw new ArgumentException("faceRegion が画像と交差しません。", nameof(faceRegion));
        }
    }

    /// <summary>
    /// 対象矩形の中心を保った正方形(辺長 = 長辺 × 1.4)を画像境界でクリップし、その ROI の
    /// 複製を返す。返却 Mat は元画像と独立(呼び出し側が破棄する契約)。クリップ後に幅・高さが
    /// 0 へ退化した場合は交差なしと同義とみなし <see cref="ArgumentException"/> を送出する。
    /// </summary>
    public static Mat CropSquare(Mat image, RectangleF region)
    {
        float centerX = region.X + region.Width / 2f;
        float centerY = region.Y + region.Height / 2f;
        float side = Math.Max(region.Width, region.Height) * SquareScale;

        var square = new RectangleF(centerX - side / 2f, centerY - side / 2f, side, side);
        RectangleF clamped = LetterboxParams.ClampToBounds(square, image.Width, image.Height);

        // 整数 ROI へ丸め、画像範囲内に収める(ClampToBounds 済みだが丸め誤差で 1px 超えないよう再度上限を掛ける)。
        int x = Math.Clamp((int)MathF.Round(clamped.X), 0, Math.Max(image.Width - 1, 0));
        int y = Math.Clamp((int)MathF.Round(clamped.Y), 0, Math.Max(image.Height - 1, 0));
        int width = Math.Min((int)MathF.Round(clamped.Width), image.Width - x);
        int height = Math.Min((int)MathF.Round(clamped.Height), image.Height - y);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("切り出し領域が画像と交差しません。", nameof(region));
        }

        // Why not: ROI 参照(new Mat(image, roi))をそのまま返すと後段のリサイズが元 Mat を破壊し得るため複製する。
        using var roi = new Mat(image, new Rect(x, y, width, height));
        return roi.Clone();
    }
}
