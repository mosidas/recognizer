using System.Drawing;

namespace Recognizer;

/// <summary>
/// 物体検出 1 件の不変な結果(api-spec 3.5)。BBox は入力画像のピクセル座標(左上原点)。
/// </summary>
/// <param name="ClassId">検出クラスの添字(モデル出力の argmax)。</param>
/// <param name="ClassName">解決済みのクラス名(常に非 null。解決規則は design §6)。</param>
/// <param name="Confidence">信頼度 0.0〜1.0。</param>
/// <param name="BBox">入力画像のピクセル座標系の外接矩形(左上原点)。</param>
public sealed record ObjectDetection(int ClassId, string ClassName, float Confidence, RectangleF BBox);
