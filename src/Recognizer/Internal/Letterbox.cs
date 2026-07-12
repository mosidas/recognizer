using System.Drawing;

namespace Recognizer.Internal;

/// <summary>
/// アスペクト比維持リサイズ(letterbox)のパラメータと、letterbox 空間 → 元画像座標系の
/// 逆変換・画像境界クリップを所有する不変 record。無状態な純粋関数として利用する。
/// </summary>
/// <param name="Scale">元画像 → letterbox 入力への一様スケール(min(inW/srcW, inH/srcH))。</param>
/// <param name="PadX">左右方向のパディング量(片側)。</param>
/// <param name="PadY">上下方向のパディング量(片側)。</param>
internal sealed record LetterboxParams(float Scale, float PadX, float PadY)
{
    /// <summary>
    /// 元画像サイズとモデル入力サイズから letterbox パラメータを算出する。
    /// アスペクト比を保つ一様スケールを選び、余白を上下・左右で中央に配置する。
    /// </summary>
    public static LetterboxParams Create(int sourceWidth, int sourceHeight, int inputWidth, int inputHeight)
    {
        float scale = Math.Min((float)inputWidth / sourceWidth, (float)inputHeight / sourceHeight);

        float resizedWidth = sourceWidth * scale;
        float resizedHeight = sourceHeight * scale;

        // 余白を中央配置するため総パディングを 2 等分する。
        float padX = (inputWidth - resizedWidth) / 2f;
        float padY = (inputHeight - resizedHeight) / 2f;

        return new LetterboxParams(scale, padX, padY);
    }

    /// <summary>
    /// letterbox 空間の点を元画像ピクセル座標へ逆変換する: original = (letterboxed - pad) / scale。
    /// </summary>
    public PointF InverseTransform(PointF point)
    {
        return new PointF((point.X - PadX) / Scale, (point.Y - PadY) / Scale);
    }

    /// <summary>
    /// letterbox 空間の矩形を元画像ピクセル座標へ逆変換する。
    /// スケールは一様かつ正のため、左上と幅・高さを独立に割ってよい。
    /// </summary>
    public RectangleF InverseTransform(RectangleF rect)
    {
        return new RectangleF(
            (rect.X - PadX) / Scale,
            (rect.Y - PadY) / Scale,
            rect.Width / Scale,
            rect.Height / Scale);
    }

    /// <summary>
    /// 点を画像境界 [0, width] × [0, height] に収める。
    /// </summary>
    public static PointF ClampToBounds(PointF point, int width, int height)
    {
        return new PointF(
            Math.Clamp(point.X, 0f, width),
            Math.Clamp(point.Y, 0f, height));
    }

    /// <summary>
    /// 矩形を画像境界 [0, width] × [0, height] に収める。四隅を個別にクリップするため
    /// 境界をまたぐ矩形は切り詰められ、完全に範囲外の矩形は退化(幅高 0)する。
    /// </summary>
    public static RectangleF ClampToBounds(RectangleF rect, int width, int height)
    {
        float left = Math.Clamp(rect.Left, 0f, width);
        float top = Math.Clamp(rect.Top, 0f, height);
        float right = Math.Clamp(rect.Right, 0f, width);
        float bottom = Math.Clamp(rect.Bottom, 0f, height);

        // Why not: right < left になり得ないよう両端を同区間へクリップ済みだが、
        // 範囲外矩形では right==left となり幅 0(負幅を作らない)。
        return new RectangleF(left, top, right - left, bottom - top);
    }
}
