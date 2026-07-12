using System.Drawing;

namespace Recognizer;

/// <summary>顔検出 1 件の結果。座標は入力画像のピクセル座標(左上原点)で、パイプライン終端で不変に確定する。</summary>
/// <param name="BBox">顔の bounding box(左上形式・画像境界内)。</param>
/// <param name="Confidence">検出信頼度(0.0〜1.0)。</param>
/// <param name="Landmarks">ランドマーク 5 点。モデルが出力しない(F=5)場合は null。</param>
public sealed record FaceDetection(RectangleF BBox, float Confidence, FaceLandmarks? Landmarks);
