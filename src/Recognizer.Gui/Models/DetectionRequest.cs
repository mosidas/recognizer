namespace Recognizer.Gui.Models;

/// <summary>検出の入力。パス・閾値の不変条件は <see cref="Validate"/> で強制する。</summary>
public sealed record DetectionRequest(
    DetectionMode Mode,
    string ModelPath,
    string ImagePath,
    float ConfidenceThreshold,
    float NmsThreshold,
    string? ClassNamesPath)
{
    /// <summary>
    /// 不変条件(パス非空・閾値 [0,1])を検証する。問題なければ null、
    /// 違反時は <see cref="DetectionStatus.InvalidInput"/> のアウトカムを返す。
    /// </summary>
    /// <remarks>
    /// Why not throw: 閾値範囲外を GUI 側で事前に弾き、コアの ArgumentException と
    /// 衝突させずに人間可読な日本語メッセージで表出するため、結果型で表す。
    /// </remarks>
    public DetectionOutcome? Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            return DetectionOutcome.Failure(DetectionStatus.InvalidInput, "モデルファイルパスを指定してください。");
        }

        if (string.IsNullOrWhiteSpace(ImagePath))
        {
            return DetectionOutcome.Failure(DetectionStatus.InvalidInput, "入力画像パスを指定してください。");
        }

        if (ConfidenceThreshold is < 0f or > 1f)
        {
            return DetectionOutcome.Failure(DetectionStatus.InvalidInput, "信頼度閾値は 0 から 1 の範囲で指定してください。");
        }

        if (NmsThreshold is < 0f or > 1f)
        {
            return DetectionOutcome.Failure(DetectionStatus.InvalidInput, "NMS 閾値は 0 から 1 の範囲で指定してください。");
        }

        return null;
    }
}
