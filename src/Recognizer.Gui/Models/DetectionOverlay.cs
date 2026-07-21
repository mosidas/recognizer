using System.Drawing;

namespace Recognizer.Gui.Models;

/// <summary>モード非依存の描画・一覧用モデル。座標は画像ピクセル座標(左上原点)。</summary>
public sealed record DetectionOverlay(
    RectangleF BBox,
    float Confidence,
    string Label,
    IReadOnlyList<PointF>? Landmarks);
