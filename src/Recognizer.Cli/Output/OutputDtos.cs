using System.Drawing;

namespace Recognizer.Cli.Output;

// Why not: プロパティ名を BBox にしない。JsonNamingPolicy.CamelCase は先頭の連続する大文字列を
// まとめて小文字化するため、BBox は "bbox" ではなく "bBox" になり、要件 3.3 / 4.3 / 5.5 に違反する
// (design §7 / research §8 の実測)。ライブラリ側の RectangleF BBox とは名前が異なるが、
// JSON の契約を優先して Bbox とする。

/// <summary>bounding box(左上原点のピクセル座標)。</summary>
internal sealed record BboxDto(float X, float Y, float Width, float Height)
{
    internal static BboxDto From(RectangleF bbox) => new(bbox.X, bbox.Y, bbox.Width, bbox.Height);
}

/// <summary>ランドマーク 1 点(ピクセル座標)。</summary>
internal sealed record PointDto(float X, float Y)
{
    internal static PointDto From(PointF point) => new(point.X, point.Y);
}

/// <summary>ランドマーク 5 点。</summary>
internal sealed record LandmarksDto(
    PointDto LeftEye,
    PointDto RightEye,
    PointDto Nose,
    PointDto LeftMouth,
    PointDto RightMouth)
{
    /// <summary>モデルがランドマークを出力しない場合は null を返す(要件 3.5)。</summary>
    internal static LandmarksDto? From(FaceLandmarks? landmarks)
        => landmarks is null
            ? null
            : new LandmarksDto(
                PointDto.From(landmarks.LeftEye),
                PointDto.From(landmarks.RightEye),
                PointDto.From(landmarks.Nose),
                PointDto.From(landmarks.LeftMouth),
                PointDto.From(landmarks.RightMouth));
}

/// <summary>detect-face の 1 顔(要件 3.3)。</summary>
internal sealed record FaceDto(BboxDto Bbox, float Confidence, LandmarksDto? Landmarks)
{
    internal static FaceDto From(FaceDetection face)
        => new(BboxDto.From(face.BBox), face.Confidence, LandmarksDto.From(face.Landmarks));
}

/// <summary>detect-object の 1 物体(要件 4.3)。</summary>
internal sealed record ObjectDto(int ClassId, string ClassName, float Confidence, BboxDto Bbox)
{
    internal static ObjectDto From(ObjectDetection detected)
        => new(detected.ClassId, detected.ClassName, detected.Confidence, BboxDto.From(detected.BBox));
}

/// <summary>compare-face で使用した顔(要件 5.5: bbox と confidence のみ)。</summary>
internal sealed record ComparedFaceDto(BboxDto Bbox, float Confidence)
{
    /// <summary>顔が未検出(null)の場合は null を返す(要件 5.6 / 5.7)。</summary>
    internal static ComparedFaceDto? From(FaceDetection? face)
        => face is null ? null : new ComparedFaceDto(BboxDto.From(face.BBox), face.Confidence);
}

/// <summary>detect-face の出力(要件 3.2)。</summary>
internal sealed record DetectFaceOutput(string Image, IReadOnlyList<FaceDto> Faces)
{
    internal static DetectFaceOutput From(string image, IReadOnlyList<FaceDetection> faces)
        => new(image, [.. faces.Select(FaceDto.From)]);
}

/// <summary>detect-object の出力(要件 4.2)。</summary>
internal sealed record DetectObjectOutput(string Image, IReadOnlyList<ObjectDto> Objects)
{
    internal static DetectObjectOutput From(string image, IReadOnlyList<ObjectDetection> objects)
        => new(image, [.. objects.Select(ObjectDto.From)]);
}

/// <summary>compare-face の出力(要件 5.2〜5.7)。</summary>
internal sealed record CompareFaceOutput(
    string Image1,
    string Image2,
    FaceComparisonStatus Status,
    float Similarity,
    ComparedFaceDto? Face1,
    ComparedFaceDto? Face2)
{
    internal static CompareFaceOutput From(string image1, string image2, FaceComparisonResult result)
        => new(
            image1,
            image2,
            result.Status,
            result.Similarity,
            ComparedFaceDto.From(result.Face1),
            ComparedFaceDto.From(result.Face2));
}

/// <summary>エラー出力(要件 7.1)。</summary>
internal sealed record ErrorOutput(string Error, string Code);
