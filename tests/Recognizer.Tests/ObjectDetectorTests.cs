using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace Recognizer.Tests;

/// <summary>
/// ObjectDetector のコンストラクタ(ガード・物体用形式判別・classNames 保持)と Dispose の契約テスト。
/// DetectAsync 系(検出・オーバーロード・クラス単位 NMS・キャンセル・並行・Dispose 後呼び出し)は後続タスク(5.x)の責務。
/// </summary>
public sealed class ObjectDetectorTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    // 正常系: 対応 fixture(classNames 省略)で構築でき、物体用形式判別が例外なく完了する(要件 2.1)
    [Fact]
    public void Constructor_対応モデルで構築できる()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        Assert.NotNull(detector);
    }

    // 正常系: classNames を渡しても構築できる(要件 2.1・classNames 保持)
    [Fact]
    public void Constructor_classNames付きで構築できる()
    {
        string[] classNames = ["cat", "dog", "bird"];

        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"), classNames);

        Assert.NotNull(detector);
    }

    // 異常系: null の modelPath は ArgumentNullException(要件 2.9)
    [Fact]
    public void Constructor_nullパスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ObjectDetector(null!));
    }

    // 異常系: 存在しないパスは FileNotFoundException(要件 2.6)
    [Fact]
    public void Constructor_存在しないパスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(() => new ObjectDetector(missing));
    }

    // 異常系: ONNX として不正なバイト列は OnnxRuntime の例外を包まず透過する(要件 2.7)
    // 一時ファイルを用いるのは fixture(有効な ONNX のみ)にロード失敗ケースが存在しないため。
    [Fact]
    public void Constructor_不正なONNXはOnnxRuntimeExceptionを透過する()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03, 0x04]);
        try
        {
            // Assert.Throws は厳密型一致のため、ラップされていない(透過している)ことを保証する。
            _ = Assert.Throws<OnnxRuntimeException>(() => new ObjectDetector(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: 判別できない物体形式(F=4 → C=0)は NotSupportedException(要件 2.8)
    [Fact]
    public void Constructor_非対応形式はNotSupportedException()
    {
        string path = FixturePath("object_unsupported_f4.onnx");

        _ = Assert.Throws<NotSupportedException>(() => new ObjectDetector(path));
    }

    // 正常系: 出力 N 軸が動的な fixture でも構築できる(構築時は分類を保留する。要件 2.1)
    [Fact]
    public void Constructor_動的出力モデルで構築できる()
    {
        using ObjectDetector detector = new(FixturePath("object_dynamic_output_4c3.onnx"));

        Assert.NotNull(detector);
    }

    // 破棄: Dispose を呼べる(要件 5.4)
    [Fact]
    public void Dispose_呼び出せる()
    {
        ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // 破棄: 二重 Dispose が例外を投げず安全である(要件 5.4)
    [Fact]
    public void Dispose_二重呼び出しが安全()
    {
        ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        detector.Dispose();

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // --- DetectAsync(Mat)パイプライン統合(タスク 5.1) ---
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

    // オーバーロードが Mat 版と同一契約(件数・ClassId・信頼度・座標)であることを確認する(要件 1.2/1.3)。
    private static void AssertSameDetections(
        IReadOnlyList<ObjectDetection> expected,
        IReadOnlyList<ObjectDetection> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].ClassId, actual[i].ClassId);
            AssertClose(expected[i].Confidence, actual[i].Confidence);
            AssertBox(expected[i].BBox, actual[i].BBox);
        }
    }

    // 正常系: 既定閾値(引数省略 = 0.5/0.5)でクラス単位 NMS 後 P0→P2→P3 の 3 件を信頼度降順で返す。
    // 同クラス P1 は P0 に抑制され、異クラス同座標 P2 は残る(要件 1.1/4.2/4.3/4.6)。argmax の ClassId と
    // classNames 省略(C=3 → "class_{id}")の解決も併せて確認する(5.3 の先行確認を兼ねる)。
    [Fact]
    public async Task DetectAsync_既定閾値でクラス単位NMS後の3件を降順で返す()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<ObjectDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);

        // P0(class0, 0.90, 中心(100,100,50,50)→左上(75,75,50,50))
        AssertClose(0.90f, results[0].Confidence);
        Assert.Equal(0, results[0].ClassId);
        Assert.Equal("class_0", results[0].ClassName);
        AssertBox(new RectangleF(75f, 75f, 50f, 50f), results[0].BBox);

        // P2(class1, 0.85, P0 と同座標だが別クラスのため共存 = 要件 4.2 の核心)
        AssertClose(0.85f, results[1].Confidence);
        Assert.Equal(1, results[1].ClassId);
        Assert.Equal("class_1", results[1].ClassName);
        AssertBox(new RectangleF(75f, 75f, 50f, 50f), results[1].BBox);

        // P3(class2, 0.70, 中心(400,400,60,60)→左上(370,370,60,60))
        AssertClose(0.70f, results[2].Confidence);
        Assert.Equal(2, results[2].ClassId);
        Assert.Equal("class_2", results[2].ClassName);
        AssertBox(new RectangleF(370f, 370f, 60f, 60f), results[2].BBox);
    }

    // 正常系: 標準 5+C の信頼度 = objectness × 最大クラススコアで合成され Q0→Q1 の 2 件を返す(要件 2.4)。
    // Q2 は積 0.42 < 0.5 で除外。互いに独立・別クラスで NMS 抑制なし。
    [Fact]
    public async Task DetectAsync_標準形式はobjectness合成信頼度で返す()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_standard_5c3.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<ObjectDetection> results = await detector.DetectAsync(image);

        Assert.Equal(2, results.Count);

        // Q0(class0, 0.90×0.80=0.72, 中心(100,100,50,50)→左上(75,75,50,50))
        AssertClose(0.72f, results[0].Confidence);
        Assert.Equal(0, results[0].ClassId);
        AssertBox(new RectangleF(75f, 75f, 50f, 50f), results[0].BBox);

        // Q1(class1, 0.80×0.75=0.60, 中心(400,200,70,70)→左上(365,165,70,70))
        AssertClose(0.60f, results[1].Confidence);
        Assert.Equal(1, results[1].ClassId);
        AssertBox(new RectangleF(365f, 165f, 70f, 70f), results[1].BBox);
    }

    // 正常系: 高閾値では検出 0 件が空リストになり例外を投げない(要件 4.4)
    [Fact]
    public async Task DetectAsync_高閾値では空リストを返す()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<ObjectDetection> results = await detector.DetectAsync(image, confidenceThreshold: 0.99f);

        Assert.Empty(results);
    }

    // 正常系: 明示 0.5/0.5 が引数省略時と同一結果になる(要件 4.6 の既定値相当を明示指定側から確認)
    [Fact]
    public async Task DetectAsync_明示した既定値相当が省略時と同一結果()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<ObjectDetection> omitted = await detector.DetectAsync(image);
        IReadOnlyList<ObjectDetection> explicitThresholds =
            await detector.DetectAsync(image, confidenceThreshold: 0.5f, nmsThreshold: 0.5f);

        Assert.Equal(omitted.Count, explicitThresholds.Count);
        for (int i = 0; i < omitted.Count; i++)
        {
            Assert.Equal(omitted[i].ClassId, explicitThresholds[i].ClassId);
            AssertClose(omitted[i].Confidence, explicitThresholds[i].Confidence);
            AssertBox(omitted[i].BBox, explicitThresholds[i].BBox);
        }
    }

    // 正常系: 非正方入力で座標が元画像系へ逆変換され、境界外は画像内へクリップされる(要件 4.5)。
    // 1280x640(幅x高さ): scale=min(640/1280,640/640)=0.5, padX=0, padY=(640-320)/2=160。
    [Fact]
    public async Task DetectAsync_非正方入力で座標を逆変換しクリップする()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = new(640, 1280, MatType.CV_8UC3, Scalar.All(0));

        IReadOnlyList<ObjectDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);

        // P3(results[2]): 左上(370,370,60,60) → ((370-0)/0.5,(370-160)/0.5,60/0.5,60/0.5)=(740,420,120,120)、境界内。
        AssertBox(new RectangleF(740f, 420f, 120f, 120f), results[2].BBox);

        // P0(results[0]): 左上(75,75,50,50)。逆変換 top=(75-160)/0.5=-170 は上端外 → Y=0 にクリップ、
        // X=(75-0)/0.5=150。範囲外へはみ出す上辺が切り詰められる(高さ 0 に退化)。
        AssertClose(150f, results[0].BBox.X);
        AssertClose(0f, results[0].BBox.Y);
    }

    // 統合: 動的出力 N 軸の fixture でも初回 Run の実形状で分類・検出が成立する(要件 2.1/o-g の後段)。
    // ⑯ は ⑫ と同じ定数出力 [1,7,60] を流用するため既定閾値で 3 件になる。
    [Fact]
    public async Task DetectAsync_動的出力モデルでも検出が成立する()
    {
        using ObjectDetector detector = new(FixturePath("object_dynamic_output_4c3.onnx"));
        using Mat image = SquareImage();

        IReadOnlyList<ObjectDetection> results = await detector.DetectAsync(image);

        Assert.Equal(3, results.Count);
        AssertClose(0.90f, results[0].Confidence);
        Assert.Equal(0, results[0].ClassId);
        AssertClose(0.85f, results[1].Confidence);
        Assert.Equal(1, results[1].ClassId);
        AssertClose(0.70f, results[2].Confidence);
        Assert.Equal(2, results[2].ClassId);
    }

    // 異常系: 空の Mat は ArgumentException を同期送出する(要件 1.5)
    // Task を破棄(_ =)しても throw が起きることで「呼び出し時点の同期送出」を保証する。
    [Fact]
    public void DetectAsync_空のMatはArgumentException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat empty = new();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(empty);
        });
    }

    // 異常系: null の Mat は ArgumentNullException を同期送出する(要件 1.6 Mat 側)
    [Fact]
    public void DetectAsync_nullのMatはArgumentNullException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = detector.DetectAsync((Mat)null!);
        });
    }

    // 異常系: confidenceThreshold が範囲外なら ArgumentException を同期送出する(要件 4.7、1 ガード 1 テスト)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void DetectAsync_confidenceThreshold範囲外はArgumentException(float threshold)
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(image, confidenceThreshold: threshold);
        });
    }

    // 異常系: nmsThreshold が範囲外なら ArgumentException を同期送出する(要件 4.7、1 ガード 1 テスト)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void DetectAsync_nmsThreshold範囲外はArgumentException(float threshold)
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(image, nmsThreshold: threshold);
        });
    }

    // --- DetectAsync オーバーロード(パス / バイト列)(タスク 5.2) ---

    // 正常系: パス版が Mat 版と同一結果(件数・ClassId・信頼度・座標)を返す(要件 1.2)
    [Fact]
    public async Task DetectAsync_パス版がMat版と同一結果を返す()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();
        IReadOnlyList<ObjectDetection> expected = await detector.DetectAsync(image);

        string tempPath = Path.Combine(Path.GetTempPath(), $"object_{Guid.NewGuid():N}.png");
        Cv2.ImWrite(tempPath, image);
        try
        {
            IReadOnlyList<ObjectDetection> actual = await detector.DetectAsync(tempPath);

            AssertSameDetections(expected, actual);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 正常系: バイト列版が Mat 版と同一結果(件数・ClassId・信頼度・座標)を返す(要件 1.3)
    [Fact]
    public async Task DetectAsync_バイト列版がMat版と同一結果を返す()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();
        IReadOnlyList<ObjectDetection> expected = await detector.DetectAsync(image);

        Cv2.ImEncode(".png", image, out byte[] encoded);
        ReadOnlyMemory<byte> bytes = encoded;

        IReadOnlyList<ObjectDetection> actual = await detector.DetectAsync(bytes);

        AssertSameDetections(expected, actual);
    }

    // 異常系: 存在しないパスは ArgumentException を同期送出する(要件 1.4、1 ガード 1 テスト)
    [Fact]
    public void DetectAsync_存在しないパスはArgumentException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
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
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
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
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
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
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(ReadOnlyMemory<byte>.Empty);
        });
    }

    // 異常系: null の imagePath は ArgumentNullException を同期送出する(要件 1.6)
    [Fact]
    public void DetectAsync_nullパスはArgumentNullException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = detector.DetectAsync((string)null!);
        });
    }

    // 異常系: パス版でも閾値範囲外は ArgumentException を同期送出する(要件 4.7、オーバーロードへのガード波及)
    [Fact]
    public void DetectAsync_パス版の閾値範囲外はArgumentException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();
        string tempPath = Path.Combine(Path.GetTempPath(), $"object_{Guid.NewGuid():N}.png");
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

    // 異常系: バイト列版でも閾値範囲外は ArgumentException を同期送出する(要件 4.7、オーバーロードへのガード波及)
    [Fact]
    public void DetectAsync_バイト列版の閾値範囲外はArgumentException()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        using Mat image = SquareImage();
        Cv2.ImEncode(".png", image, out byte[] encoded);
        ReadOnlyMemory<byte> bytes = encoded;

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = detector.DetectAsync(bytes, nmsThreshold: 1.1f);
        });
    }
}
