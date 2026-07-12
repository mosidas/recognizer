namespace Recognizer;

/// <summary>
/// 顔の埋め込みベクトルを抽出した不変な結果(api-spec 3.4)。
/// </summary>
/// <param name="Embedding">埋め込みベクトル。顔未検出時は null。</param>
/// <param name="Face">使用した顔(faceRegion 指定時・未検出時は null)。</param>
public sealed record FaceEmbeddingResult(
    float[]? Embedding,
    FaceDetection? Face);
