namespace Recognizer;

/// <summary>顔比較の結果ステータス(api-spec 3.4)。</summary>
public enum FaceComparisonStatus
{
    /// <summary>両画像で顔を検出し、類似度を算出した。</summary>
    Success,

    /// <summary>画像 1 で顔未検出。</summary>
    NoFaceInImage1,

    /// <summary>画像 2 で顔未検出。</summary>
    NoFaceInImage2
}
