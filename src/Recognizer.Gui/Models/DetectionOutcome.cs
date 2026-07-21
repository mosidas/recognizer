namespace Recognizer.Gui.Models;

/// <summary>検出実行の結果種別。予期されるエラーは例外ではなくこの種別で表す。</summary>
public enum DetectionStatus
{
    Success,
    ModelLoadFailed,
    ImageLoadFailed,
    UnsupportedModel,
    ClassNamesFileFailed,
    Cancelled,
    InvalidInput,
}

/// <summary>検出実行の結果型。</summary>
public sealed record DetectionOutcome
{
    public DetectionStatus Status { get; init; }

    /// <summary>検出結果。非成功時は空。</summary>
    public IReadOnlyList<DetectionOverlay> Detections { get; init; }

    /// <summary>成功時のプレビュー元(通常は入力画像パス)。</summary>
    public string? ImageDisplayPath { get; init; }

    /// <summary>エラー時の日本語メッセージ。成功時は null。</summary>
    public string? Message { get; init; }

    public DetectionOutcome(
        DetectionStatus status,
        IReadOnlyList<DetectionOverlay> detections,
        string? imageDisplayPath,
        string? message)
    {
        // Why not: Success⇔Message==null は呼び出し側のロジック整合性の問題(プログラミングエラー)であり、
        // 予期されるエラーとは異なる。生成時点で契約破りを検出し、不整合な結果の伝播を防ぐ。
        if (status == DetectionStatus.Success && message is not null)
        {
            throw new ArgumentException("成功時に Message を保持できません。", nameof(message));
        }

        if (status != DetectionStatus.Success && message is null)
        {
            throw new ArgumentException("非成功時は Message が必要です。", nameof(message));
        }

        Status = status;
        Detections = detections;
        ImageDisplayPath = imageDisplayPath;
        Message = message;
    }

    /// <summary>成功アウトカムを生成する(検出 0 件でも成功)。</summary>
    public static DetectionOutcome Success(IReadOnlyList<DetectionOverlay> detections, string imageDisplayPath) =>
        new(DetectionStatus.Success, detections, imageDisplayPath, message: null);

    /// <summary>失敗アウトカムを生成する。検出は空にする。</summary>
    public static DetectionOutcome Failure(DetectionStatus status, string message) =>
        new(status, [], imageDisplayPath: null, message);
}
