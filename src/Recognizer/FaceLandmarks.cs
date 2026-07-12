using System.Drawing;

namespace Recognizer;

/// <summary>顔ランドマーク 5 点(モデルが出力する場合のみ)。座標は入力画像のピクセル座標(左上原点)。</summary>
/// <param name="LeftEye">左目。</param>
/// <param name="RightEye">右目。</param>
/// <param name="Nose">鼻。</param>
/// <param name="LeftMouth">口左端。</param>
/// <param name="RightMouth">口右端。</param>
public sealed record FaceLandmarks(
    PointF LeftEye,
    PointF RightEye,
    PointF Nose,
    PointF LeftMouth,
    PointF RightMouth);
