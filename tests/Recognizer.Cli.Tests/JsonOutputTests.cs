using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Tests;

/// <summary>
/// JSON 出力契約(要件 6.2〜6.4)と、ライブラリ結果型 → 出力 DTO の変換を検証する。
/// </summary>
public sealed class JsonOutputTests
{
    private static FaceDetection Face(float confidence = 0.95f, FaceLandmarks? landmarks = null)
        => new(new RectangleF(1.5f, 2f, 3f, 4f), confidence, landmarks);

    private static FaceLandmarks Landmarks()
        => new(
            new PointF(10f, 11f),
            new PointF(20f, 21f),
            new PointF(30f, 31f),
            new PointF(40f, 41f),
            new PointF(50f, 51f));

    [Fact]
    public void detect_face_の_JSON_は_bbox_キーを出力する()
    {
        DetectFaceOutput output = DetectFaceOutput.From("a.jpg", [Face()]);

        string json = CliJson.Serialize(output);

        Assert.Contains("\"bbox\":", json);
        // Why not: 「bbox を含む」だけでは不十分。JsonNamingPolicy.CamelCase は先頭の連続大文字を
        // まとめて小文字化するため、プロパティ名を BBox にすると "bBox" になる(design §7 / research §8)。
        // 回帰の要なので不在を明示的に固定する。
        Assert.DoesNotContain("bBox", json);
    }

    [Fact]
    public void JSON_のプロパティ名は_camelCase_で出力される()
    {
        DetectObjectOutput output = DetectObjectOutput.From(
            "a.jpg",
            [new ObjectDetection(3, "car", 0.8f, new RectangleF(1f, 2f, 3f, 4f))]);

        string json = CliJson.Serialize(output);

        Assert.Equal(
            """{"image":"a.jpg","objects":[{"classId":3,"className":"car","confidence":0.8,"bbox":{"x":1,"y":2,"width":3,"height":4}}]}""",
            json);
    }

    [Fact]
    public void JSON_は改行を含まない_1_行で出力される()
    {
        DetectFaceOutput output = DetectFaceOutput.From("a.jpg", [Face(landmarks: Landmarks())]);

        string json = CliJson.Serialize(output);

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain('\r', json);
    }

    [Fact]
    public void Write_は_JSON_1_行と末尾の改行_1_個だけを書き出す()
    {
        using StringWriter writer = new();

        CliJson.Write(writer, new ErrorOutput("画像を読み込めませんでした。", "imageLoadFailed"));

        string written = writer.ToString();
        Assert.EndsWith(writer.NewLine, written);

        string body = written[..^writer.NewLine.Length];
        Assert.DoesNotContain('\n', body);
        Assert.DoesNotContain('\r', body);
        Assert.Equal("""{"error":"画像を読み込めませんでした。","code":"imageLoadFailed"}""", body);
    }

    [Fact]
    public void status_は列挙子名の文字列として出力される()
    {
        CompareFaceOutput output = CompareFaceOutput.From(
            "a.jpg",
            "b.jpg",
            new FaceComparisonResult(FaceComparisonStatus.NoFaceInImage1, 0f, null, null));

        string json = CliJson.Serialize(output);

        Assert.Contains("\"status\":\"NoFaceInImage1\"", json);
    }

    [Fact]
    public void landmarks_が_null_のとき_null_を省略せず出力する()
    {
        DetectFaceOutput output = DetectFaceOutput.From("a.jpg", [Face(landmarks: null)]);

        string json = CliJson.Serialize(output);

        Assert.Contains("\"landmarks\":null", json);
    }

    [Fact]
    public void 顔未検出のとき_face1_と_face2_は_null_を省略せず出力する()
    {
        CompareFaceOutput output = CompareFaceOutput.From(
            "a.jpg",
            "b.jpg",
            new FaceComparisonResult(FaceComparisonStatus.NoFaceInImage1, 0f, null, null));

        string json = CliJson.Serialize(output);

        Assert.Equal(
            """{"image1":"a.jpg","image2":"b.jpg","status":"NoFaceInImage1","similarity":0,"face1":null,"face2":null}""",
            json);
    }

    [Fact]
    public void 画像_2_で顔未検出のとき_face1_のみ出力し_face2_は_null_になる()
    {
        CompareFaceOutput output = CompareFaceOutput.From(
            "a.jpg",
            "b.jpg",
            new FaceComparisonResult(FaceComparisonStatus.NoFaceInImage2, 0f, Face(0.9f), null));

        string json = CliJson.Serialize(output);

        Assert.Equal(
            """{"image1":"a.jpg","image2":"b.jpg","status":"NoFaceInImage2","similarity":0,"face1":{"bbox":{"x":1.5,"y":2,"width":3,"height":4},"confidence":0.9},"face2":null}""",
            json);
    }

    [Fact]
    public void 浮動小数点数はカルチャに依存しない不変形式で出力される()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // 小数点をカンマで表記するカルチャ。要件 6.4 の回帰を固定する。
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            CompareFaceOutput output = CompareFaceOutput.From(
                "a.jpg",
                "b.jpg",
                new FaceComparisonResult(FaceComparisonStatus.Success, 0.7f, Face(0.5f), Face(0.5f)));

            string json = CliJson.Serialize(output);

            Assert.Contains("\"similarity\":0.7", json);
            Assert.DoesNotContain("0,7", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 顔検出結果は_DTO_へ写像される()
    {
        DetectFaceOutput output = DetectFaceOutput.From("a.jpg", [Face(0.95f, Landmarks())]);

        Assert.Equal("a.jpg", output.Image);
        FaceDto face = Assert.Single(output.Faces);
        Assert.Equal(new BboxDto(1.5f, 2f, 3f, 4f), face.Bbox);
        Assert.Equal(0.95f, face.Confidence);

        LandmarksDto landmarks = Assert.IsType<LandmarksDto>(face.Landmarks);
        Assert.Equal(new PointDto(10f, 11f), landmarks.LeftEye);
        Assert.Equal(new PointDto(20f, 21f), landmarks.RightEye);
        Assert.Equal(new PointDto(30f, 31f), landmarks.Nose);
        Assert.Equal(new PointDto(40f, 41f), landmarks.LeftMouth);
        Assert.Equal(new PointDto(50f, 51f), landmarks.RightMouth);
    }

    [Fact]
    public void 物体検出結果は_DTO_へ写像される()
    {
        DetectObjectOutput output = DetectObjectOutput.From(
            "a.jpg",
            [new ObjectDetection(7, "person", 0.42f, new RectangleF(5f, 6f, 7f, 8f))]);

        Assert.Equal("a.jpg", output.Image);
        ObjectDto detected = Assert.Single(output.Objects);
        Assert.Equal(7, detected.ClassId);
        Assert.Equal("person", detected.ClassName);
        Assert.Equal(0.42f, detected.Confidence);
        Assert.Equal(new BboxDto(5f, 6f, 7f, 8f), detected.Bbox);
    }

    [Fact]
    public void 顔比較結果は_DTO_へ写像される()
    {
        CompareFaceOutput output = CompareFaceOutput.From(
            "a.jpg",
            "b.jpg",
            new FaceComparisonResult(
                FaceComparisonStatus.Success,
                0.7f,
                new FaceDetection(new RectangleF(1f, 2f, 3f, 4f), 0.9f, Landmarks()),
                new FaceDetection(new RectangleF(5f, 6f, 7f, 8f), 0.8f, null)));

        Assert.Equal("a.jpg", output.Image1);
        Assert.Equal("b.jpg", output.Image2);
        Assert.Equal(FaceComparisonStatus.Success, output.Status);
        Assert.Equal(0.7f, output.Similarity);

        // 要件 5.5: 比較に使用した顔は bbox と confidence のみを出力する(landmarks は持たない)。
        Assert.Equal(new ComparedFaceDto(new BboxDto(1f, 2f, 3f, 4f), 0.9f), output.Face1);
        Assert.Equal(new ComparedFaceDto(new BboxDto(5f, 6f, 7f, 8f), 0.8f), output.Face2);
    }

    [Fact]
    public void 検出件数_0_件は空配列として出力される()
    {
        string faceJson = CliJson.Serialize(DetectFaceOutput.From("a.jpg", []));
        string objectJson = CliJson.Serialize(DetectObjectOutput.From("a.jpg", []));

        Assert.Equal("""{"image":"a.jpg","faces":[]}""", faceJson);
        Assert.Equal("""{"image":"a.jpg","objects":[]}""", objectJson);
    }

    [Fact]
    public void 日本語は_uXXXX_へエスケープせずそのまま出力される()
    {
        // 既定のエンコーダは非 ASCII をすべてエスケープする。エラーメッセージは人間可読でなければ
        // ならない(要件 7.1)ため、日本語がそのまま出ることを固定する。
        string json = CliJson.Serialize(new ErrorOutput("モデルを読み込めませんでした。", "modelLoadFailed"));

        Assert.Contains("モデルを読み込めませんでした。", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void ソース生成コンテキストが型情報リゾルバとして結線されている()
    {
        // Why not: context を直接 Serialize(v, CliJsonContext.Default.X) に渡さない。命名ポリシーと
        // 列挙子変換が効かず PascalCase・enum が数値になる(design §7 / research §8)。
        Assert.Same(CliJsonContext.Default, CliJson.Options.TypeInfoResolver);
        Assert.Equal(JsonNamingPolicy.CamelCase, CliJson.Options.PropertyNamingPolicy);
        Assert.False(CliJson.Options.WriteIndented);
    }
}
