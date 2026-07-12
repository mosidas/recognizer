namespace Recognizer;

/// <summary>
/// 2 枚の顔画像を比較した不変な結果(api-spec 3.4)。
/// </summary>
/// <param name="Status">比較ステータス。<see cref="FaceComparisonStatus.Success"/> のときのみ両顔が非 null。</param>
/// <param name="Similarity">コサイン類似度 [-1, 1]。顔未検出時は 0。</param>
/// <param name="Face1">画像 1 で使用した顔(未検出時は null)。</param>
/// <param name="Face2">画像 2 で使用した顔(未検出時は null)。</param>
public sealed record FaceComparisonResult(
    FaceComparisonStatus Status,
    float Similarity,
    FaceDetection? Face1,
    FaceDetection? Face2);
