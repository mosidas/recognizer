using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace Recognizer.Tests;

/// <summary>
/// FaceDetector のコンストラクタ(ガード・モデルロード・形式判別)と Dispose の契約テスト。
/// DetectAsync 系(検出・オーバーロード・キャンセル・並行・Dispose 後呼び出し)は後続タスクの責務。
/// </summary>
public sealed class FaceDetectorTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    // 正常系: 対応 fixture で構築でき、形式判別が例外なく完了する(要件 2.1)
    [Fact]
    public void Constructor_対応モデルで構築できる()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        Assert.NotNull(detector);
    }

    // 異常系: null の modelPath は ArgumentNullException(要件 2.7)
    [Fact]
    public void Constructor_nullパスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new FaceDetector(null!));
    }

    // 異常系: 存在しないパスは FileNotFoundException(要件 2.4)
    [Fact]
    public void Constructor_存在しないパスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(() => new FaceDetector(missing));
    }

    // 異常系: ONNX として不正なバイト列は OnnxRuntime の例外を包まず透過する(要件 2.5)
    // 一時ファイルを用いるのは fixture(有効な ONNX のみ)にロード失敗ケースが存在しないため。
    [Fact]
    public void Constructor_不正なONNXはOnnxRuntimeExceptionを透過する()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03, 0x04]);
        try
        {
            // Assert.Throws は厳密型一致のため、ラップされていない(透過している)ことを保証する。
            _ = Assert.Throws<OnnxRuntimeException>(() => new FaceDetector(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: 判別できない形式(F=7)は NotSupportedException(要件 2.6)
    [Fact]
    public void Constructor_非対応形式はNotSupportedException()
    {
        string path = FixturePath("face_unsupported_f7.onnx");

        _ = Assert.Throws<NotSupportedException>(() => new FaceDetector(path));
    }

    // 破棄: Dispose を呼べる(要件 4.4)
    [Fact]
    public void Dispose_呼び出せる()
    {
        FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // 破棄: 二重 Dispose が例外を投げず安全である(要件 4.4)
    [Fact]
    public void Dispose_二重呼び出しが安全()
    {
        FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        detector.Dispose();

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // --- DetectAsync(Mat)パイプライン統合(タスク 6.2) ---
    // fixture は入力非依存の定数出力。640x640 の Mat なら scale=1/pad=0 で座標が素通しになる。
    private static Mat SquareImage() => new(640, 640, MatType.CV_8UC3, Scalar.All(0));

    private static void AssertClose(float expected, float actual)
        => Assert.True(Math.Abs(expected - actual) <= 0.05f, $"期待 {expected} に対し実際 {actual}");

    private static void AssertBox(RectangleF expected, RectangleF actual)
    {
        AssertClose(expected.X, actual.X);
        AssertClose(expected.Y, actual.Y);
        AssertClose(expected.Width, actual.Width);
        AssertClose(expected.Height, actual.Height);
    }

    private static void AssertPoint(PointF expected, PointF actual)
    {
        AssertClose(expected.X, actual.X);
        AssertClose(expected.Y, actual.Y);
    }

    // 正常系: 既定閾値(0.7/0.5)を引数省略で使い、A→B→D の 3 件を信頼度降順で返す(要件 1.1/3.1/3.2/3.3/3.5/3.8)
    [Fact]
    public async Task DetectAsync_既定閾値で信頼度降順の3件を返す()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);
        AssertClose(0.95f, results[0].Confidence);
        AssertClose(0.85f, results[1].Confidence);
        AssertClose(0.75f, results[2].Confidence);
        // 左上形式(中心形式から変換済み)。A=(75,75,50,50), B=(270,270,60,60), D=(160,460,80,80)
        AssertBox(new RectangleF(75f, 75f, 50f, 50f), results[0].BBox);
        AssertBox(new RectangleF(270f, 270f, 60f, 60f), results[1].BBox);
        AssertBox(new RectangleF(160f, 460f, 80f, 80f), results[2].BBox);
    }

    // 正常系: 高閾値では検出 0 件が空リストになり例外を投げない(要件 3.4)
    [Fact]
    public async Task DetectAsync_高閾値では空リストを返す()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image, confidenceThreshold: 0.99f);

        Assert.Empty(results);
    }

    // 正常系: 非正方入力で座標が元画像系へ逆変換される(要件 3.5)
    // 1280x640(幅x高さ): scale=min(640/1280,640/640)=0.5, padX=0, padY=(640-320)/2=160。
    [Fact]
    public async Task DetectAsync_非正方入力で座標を元画像系へ逆変換する()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = new(640, 1280, MatType.CV_8UC3, Scalar.All(0));

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);
        // B のレターボックス左上 (270,270,60,60) → ((270-0)/0.5,(270-160)/0.5,60/0.5,60/0.5)=(540,220,120,120)、境界内。
        AssertBox(new RectangleF(540f, 220f, 120f, 120f), results[1].BBox);
    }

    // 正常系: F=20 モデルはランドマーク 5 点を含み座標が検証できる(要件 3.6)
    [Fact]
    public async Task DetectAsync_F20モデルはランドマーク5点を返す()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f20.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image);

        FaceDetection top = results[0]; // A(cx=100,cy=100,w=50,h=50)。dx=10, up=7.5, down=10
        Assert.NotNull(top.Landmarks);
        FaceLandmarks lm = top.Landmarks!;
        AssertPoint(new PointF(90f, 92.5f), lm.LeftEye);
        AssertPoint(new PointF(110f, 92.5f), lm.RightEye);
        AssertPoint(new PointF(100f, 100f), lm.Nose);
        AssertPoint(new PointF(90f, 110f), lm.LeftMouth);
        AssertPoint(new PointF(110f, 110f), lm.RightMouth);
    }

    // 正常系: F=5 モデルはランドマークが null(要件 3.7)
    [Fact]
    public async Task DetectAsync_F5モデルはランドマークがnull()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image);

        Assert.All(results, r => Assert.Null(r.Landmarks));
    }

    // 統合: レイアウト差異(標準形式・NHWC・動的軸)を吸収し同一結果を返す(要件 2.2/2.3 の統合面)
    [Theory]
    [InlineData("face_nchw_standard_f5.onnx")]
    [InlineData("face_nhwc_transposed_f5.onnx")]
    [InlineData("face_dynamic_input_f5.onnx")]
    public async Task DetectAsync_レイアウト差異を吸収して同一結果を返す(string fixture)
    {
        using FaceDetector detector = new(FixturePath(fixture));
        using Mat image = SquareImage();

        IReadOnlyList<FaceDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);
        AssertClose(0.95f, results[0].Confidence);
        AssertBox(new RectangleF(75f, 75f, 50f, 50f), results[0].BBox);
        AssertBox(new RectangleF(270f, 270f, 60f, 60f), results[1].BBox);
        AssertBox(new RectangleF(160f, 460f, 80f, 80f), results[2].BBox);
    }

    // 異常系: 空の Mat は ArgumentException を同期送出する(要件 1.5)
    // Task を破棄(_ =)しても throw が起きることで「呼び出し時点の同期送出」を保証する。
    [Fact]
    public void DetectAsync_空のMatはArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat empty = new();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(empty);
        });
    }

    // 異常系: confidenceThreshold が範囲外なら ArgumentException を同期送出する(要件 3.9、1 ガード 1 テスト)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void DetectAsync_confidenceThreshold範囲外はArgumentException(float threshold)
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(image, confidenceThreshold: threshold);
        });
    }

    // 異常系: nmsThreshold が範囲外なら ArgumentException を同期送出する(要件 3.9、1 ガード 1 テスト)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void DetectAsync_nmsThreshold範囲外はArgumentException(float threshold)
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(image, nmsThreshold: threshold);
        });
    }

    // --- DetectAsync パス / バイト列オーバーロード(タスク 6.3) ---
    // fixture は入力非依存の定数出力。PNG を経由しても 640x640 なら Mat 版と同一結果になる。
    private static void AssertSameDetections(
        IReadOnlyList<FaceDetection> expected,
        IReadOnlyList<FaceDetection> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            AssertClose(expected[i].Confidence, actual[i].Confidence);
            AssertBox(expected[i].BBox, actual[i].BBox);
        }
    }

    // 正常系: パス版が Mat 版と同一結果(件数・座標・信頼度)を返す(要件 1.2)
    [Fact]
    public async Task DetectAsync_パス版がMat版と同一結果を返す()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();
        IReadOnlyList<FaceDetection> expected = await detector.DetectAsync(image);

        string tempPath = Path.Combine(Path.GetTempPath(), $"face_{Guid.NewGuid():N}.png");
        Cv2.ImWrite(tempPath, image);
        try
        {
            IReadOnlyList<FaceDetection> actual = await detector.DetectAsync(tempPath);

            AssertSameDetections(expected, actual);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 正常系: バイト列版が Mat 版と同一結果を返す(要件 1.3)
    [Fact]
    public async Task DetectAsync_バイト列版がMat版と同一結果を返す()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();
        IReadOnlyList<FaceDetection> expected = await detector.DetectAsync(image);

        Cv2.ImEncode(".png", image, out byte[] encoded);
        ReadOnlyMemory<byte> bytes = encoded;

        IReadOnlyList<FaceDetection> actual = await detector.DetectAsync(bytes);

        AssertSameDetections(expected, actual);
    }

    // 異常系: 存在しないパスは ArgumentException を同期送出する(要件 1.4、1 ガード 1 テスト)
    [Fact]
    public void DetectAsync_存在しないパスはArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        string missing = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(missing);
        });
    }

    // 異常系: 画像でないファイルは ArgumentException を同期送出する(要件 1.4、1 ガード 1 テスト)
    [Fact]
    public void DetectAsync_画像でないファイルはArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        string tempPath = Path.Combine(Path.GetTempPath(), $"notimage_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempPath, "これは画像ではありません");
        try
        {
            _ = Assert.Throws<ArgumentException>(() =>
            {
                _ = detector.DetectAsync(tempPath);
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: 画像としてデコードできない不正バイト列は ArgumentException を同期送出する(要件 1.4、1 ガード 1 テスト)
    [Fact]
    public void DetectAsync_不正バイト列はArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        ReadOnlyMemory<byte> garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(garbage);
        });
    }

    // 異常系: 空バイト列は ArgumentException を同期送出する(要件 1.4、1 ガード 1 テスト)
    [Fact]
    public void DetectAsync_空バイト列はArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(ReadOnlyMemory<byte>.Empty);
        });
    }

    // 異常系: null の imagePath は ArgumentNullException を同期送出する(要件 1.6)
    [Fact]
    public void DetectAsync_nullパスはArgumentNullException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = detector.DetectAsync((string)null!);
        });
    }

    // 異常系: パス版でも閾値範囲外は ArgumentException を同期送出する(要件 3.9、オーバーロードへのガード波及)
    [Fact]
    public void DetectAsync_パス版の閾値範囲外はArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();
        string tempPath = Path.Combine(Path.GetTempPath(), $"face_{Guid.NewGuid():N}.png");
        Cv2.ImWrite(tempPath, image);
        try
        {
            _ = Assert.Throws<ArgumentException>(() =>
            {
                _ = detector.DetectAsync(tempPath, confidenceThreshold: 1.1f);
            });
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: バイト列版でも閾値範囲外は ArgumentException を同期送出する(要件 3.9、オーバーロードへのガード波及)
    [Fact]
    public void DetectAsync_バイト列版の閾値範囲外はArgumentException()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        using Mat image = SquareImage();
        Cv2.ImEncode(".png", image, out byte[] encoded);
        ReadOnlyMemory<byte> bytes = encoded;

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(bytes, nmsThreshold: 1.1f);
        });
    }
}
