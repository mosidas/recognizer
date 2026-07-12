using System.Drawing;
using OpenCvSharp;
using Recognizer;

namespace Recognizer.Tests;

/// <summary>
/// FaceRecognizer.CompareEmbeddings(static コサイン類似度)の単体テスト。
/// 要件 5.1〜5.5 / design §6 の CompareEmbeddings 契約を検証する。
/// </summary>
public sealed class FaceRecognizerTests
{
    // 要件 5.5 で規定される許容誤差(浮動小数点演算のため厳密比較しない)。
    private const float Epsilon = 1e-5f;

    [Fact]
    public void CompareEmbeddings_IdenticalVectors_ReturnsOne()
    {
        // 要件 5.5: 同一ベクトル同士の類似度は 1.0(許容誤差内)。
        float[] embedding = [1f, 2f, 3f, 4f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding, embedding);

        Assert.Equal(1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_ScaledSameDirection_ReturnsOne()
    {
        // コサイン類似度はスケール不変であり、向きが同じなら 1.0。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [2f, 4f, 6f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_OppositeVectors_ReturnsMinusOne()
    {
        // 要件 5.5: 逆向きベクトル同士は -1.0(許容誤差内)。
        float[] embedding1 = [1f, 2f, 3f, 4f];
        float[] embedding2 = [-1f, -2f, -3f, -4f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(-1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_OrthogonalVectors_ReturnsZero()
    {
        // 直交ベクトルのコサイン類似度は 0。
        float[] embedding1 = [1f, 0f];
        float[] embedding2 = [0f, 1f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_DimensionMismatch_ThrowsArgumentException()
    {
        // 要件 5.3: 次元(長さ)不一致は ArgumentException。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [1f, 2f];

        Assert.Throws<ArgumentException>(() =>
        {
            // ReadOnlySpan はラムダのクロージャに載せられないためローカルで取得する。
            _ = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);
        });
    }

    [Fact]
    public void CompareEmbeddings_FirstIsZeroVector_ReturnsZero()
    {
        // 要件 5.4: いずれかがゼロベクトル(ノルム 0)なら 0。
        float[] embedding1 = [0f, 0f, 0f];
        float[] embedding2 = [1f, 2f, 3f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_SecondIsZeroVector_ReturnsZero()
    {
        // 要件 5.4: 第 2 引数がゼロベクトルの場合も 0。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [0f, 0f, 0f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_BothZeroVectors_ReturnsZero()
    {
        // 要件 5.4: 両方ゼロベクトルでも 0(0/0 の未定義を安全側に倒す)。
        float[] embedding1 = [0f, 0f];
        float[] embedding2 = [0f, 0f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_Result_IsWithinValidRange()
    {
        // 要件 5.2: 返却値は常に [-1, 1]。浮動小数点誤差でこの範囲を超えないこと。
        float[] embedding1 = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f];
        float[] embedding2 = [0.5f, 0.4f, 0.3f, 0.2f, 0.1f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.InRange(similarity, -1f, 1f);
    }

    // --- コンストラクタ(2 モデル判別)と Dispose の契約テスト(タスク 5.2) ---
    // 非同期メソッド(CompareFacesAsync/ExtractEmbeddingAsync)とその契約は後続タスク(5.3 以降)の責務。
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private const string ValidDetectorFixture = "face_nchw_standard_f5.onnx";
    private const string ValidEmbeddingFixture = "embed_nchw_meanrgb_d4.onnx";

    // 正常系: 対応する検出+埋め込みモデルで構築でき、両モデルの判別が例外なく完了する(要件 2.1)
    [Fact]
    public void Constructor_対応する2モデルで構築できる()
    {
        using FaceRecognizer recognizer = new(
            FixturePath(ValidDetectorFixture),
            FixturePath(ValidEmbeddingFixture));

        Assert.NotNull(recognizer);
    }

    // 異常系: null の detectorModelPath は ArgumentNullException(要件 2.7)
    [Fact]
    public void Constructor_null検出パスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new FaceRecognizer(null!, FixturePath(ValidEmbeddingFixture)));
    }

    // 異常系: null の embeddingModelPath は ArgumentNullException(要件 2.7)
    [Fact]
    public void Constructor_null埋め込みパスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new FaceRecognizer(FixturePath(ValidDetectorFixture), null!));
    }

    // 異常系: 存在しない検出パスは FileNotFoundException(要件 2.4)
    [Fact]
    public void Constructor_存在しない検出パスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(
            () => new FaceRecognizer(missing, FixturePath(ValidEmbeddingFixture)));
    }

    // 異常系: 存在しない埋め込みパスは FileNotFoundException(要件 2.4)
    [Fact]
    public void Constructor_存在しない埋め込みパスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(
            () => new FaceRecognizer(FixturePath(ValidDetectorFixture), missing));
    }

    // 異常系: 判別できない埋め込みモデル(rank3)は NotSupportedException(要件 2.6)
    [Fact]
    public void Constructor_判別不能な埋め込みモデルはNotSupportedException()
    {
        string unsupported = FixturePath("embed_unsupported_rank3.onnx");

        _ = Assert.Throws<NotSupportedException>(
            () => new FaceRecognizer(FixturePath(ValidDetectorFixture), unsupported));
    }

    // 破棄: Dispose を呼べる(要件 6.4)
    [Fact]
    public void Dispose_呼び出せる()
    {
        FaceRecognizer recognizer = new(
            FixturePath(ValidDetectorFixture),
            FixturePath(ValidEmbeddingFixture));

        Exception? ex = Record.Exception(recognizer.Dispose);

        Assert.Null(ex);
    }

    // 破棄: 二重 Dispose が例外を投げず安全である(要件 6.4)
    [Fact]
    public void Dispose_二重呼び出しが安全()
    {
        FaceRecognizer recognizer = new(
            FixturePath(ValidDetectorFixture),
            FixturePath(ValidEmbeddingFixture));
        recognizer.Dispose();

        Exception? ex = Record.Exception(recognizer.Dispose);

        Assert.Null(ex);
    }

    // --- ExtractEmbeddingAsync(Mat)パイプラインと分岐(タスク 5.3) ---
    // 検出 fixture ㉑(入力平均 = conf。640x640 白→検出/黒→未検出)と埋め込み fixture ⑰(出力 = [mean(R),mean(G),mean(B),1.0])を用いる。
    private const string InputConfDetectorFixture = "face_inputconf_f5.onnx";

    // 埋め込み分岐テスト用の recognizer。検出は入力依存 fixture ㉑、埋め込みは次元 4 の fixture ⑰。
    private static FaceRecognizer ExtractRecognizer()
        => new(FixturePath(InputConfDetectorFixture), FixturePath(ValidEmbeddingFixture));

    // 単色 BGR 画像。Scalar は (B, G, R) 順。
    private static Mat SolidBgr(int width, int height, byte b, byte g, byte r)
        => new(height, width, MatType.CV_8UC3, new Scalar(b, g, r));

    // ㉑ は入力全体の平均を conf に使うため 640x640 の白/黒で検出あり/未検出を作り分ける。
    private static Mat WhiteSquare() => new(640, 640, MatType.CV_8UC3, Scalar.All(255));
    private static Mat BlackSquare() => new(640, 640, MatType.CV_8UC3, Scalar.All(0));

    // (x − 127.5) / 128 の期待値(design §6)。
    private static float Normalize(byte value) => (value - 127.5f) / 128f;

    // 正常系: faceRegion 省略・白画像(検出あり)→ Embedding 長 = 4・Face 非 null(要件 3.1, 3.2, 3.6)
    [Fact]
    public async Task ExtractEmbeddingAsync_faceRegion省略_検出ありで埋め込みとFaceを返す()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = WhiteSquare();

        FaceEmbeddingResult result = await recognizer.ExtractEmbeddingAsync(image);

        Assert.NotNull(result.Embedding);
        Assert.Equal(4, result.Embedding!.Length);
        Assert.NotNull(result.Face);
    }

    // 正常系: faceRegion 指定(有効矩形)→ 検出せず Face=null・Embedding 長 = 4(要件 3.3, 3.6)
    [Fact]
    public async Task ExtractEmbeddingAsync_faceRegion指定_検出せずFaceはnull()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        FaceEmbeddingResult result = await recognizer.ExtractEmbeddingAsync(
            image, faceRegion: new RectangleF(50, 50, 100, 100));

        Assert.NotNull(result.Embedding);
        Assert.Equal(4, result.Embedding!.Length);
        Assert.Null(result.Face);
    }

    // 統合検証: 単色画像の埋め込みが (x−127.5)/128 の解析値(RGB 順・出力 = [mean(R),mean(G),mean(B),1.0])になる
    [Fact]
    public async Task ExtractEmbeddingAsync_単色画像で埋め込みが解析値になる()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        // BGR (B=64, G=128, R=192)。単色のため切り出し・リサイズ後も画素値は不変。
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        FaceEmbeddingResult result = await recognizer.ExtractEmbeddingAsync(
            image, faceRegion: new RectangleF(50, 50, 100, 100));

        Assert.NotNull(result.Embedding);
        float[] embedding = result.Embedding!;
        Assert.Equal(Normalize(192), embedding[0], 4); // mean(R)
        Assert.Equal(Normalize(128), embedding[1], 4); // mean(G)
        Assert.Equal(Normalize(64), embedding[2], 4);  // mean(B)
        Assert.Equal(1f, embedding[3], 4);             // ⑰ の定数チャネル
    }

    // 異常系: faceRegion 省略・黒画像(未検出)→ Embedding・Face とも null・例外なし(要件 3.5)
    [Fact]
    public async Task ExtractEmbeddingAsync_faceRegion省略_未検出でnull結果を返す()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = BlackSquare();

        FaceEmbeddingResult result = await recognizer.ExtractEmbeddingAsync(image);

        Assert.Null(result.Embedding);
        Assert.Null(result.Face);
    }

    // 要件 3.8: 既定閾値 0.7 / 0.5 が使われる(省略呼び出しが成立し、白画像は既定 0.7 超で検出される)
    [Fact]
    public async Task ExtractEmbeddingAsync_既定閾値で検出が成立する()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = WhiteSquare();

        FaceEmbeddingResult result = await recognizer.ExtractEmbeddingAsync(image);

        // 白画像 conf ≈ 1.0 は既定 detectionThreshold=0.7 を超えるため顔が採用される。
        Assert.NotNull(result.Face);
        Assert.InRange(result.Face!.Confidence, 0.7f, 1f);
    }

    // 異常系: detectionThreshold 範囲外 → ArgumentException(faceRegion 指定で検出省略の呼び出しでも送出。要件 3.9)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void ExtractEmbeddingAsync_detectionThreshold範囲外はArgumentException(float threshold)
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(
                image, faceRegion: new RectangleF(50, 50, 100, 100), detectionThreshold: threshold);
        });
    }

    // 異常系: nmsThreshold 範囲外 → ArgumentException(faceRegion 指定で検出省略の呼び出しでも送出。要件 3.9)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void ExtractEmbeddingAsync_nmsThreshold範囲外はArgumentException(float threshold)
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(
                image, faceRegion: new RectangleF(50, 50, 100, 100), nmsThreshold: threshold);
        });
    }

    // 異常系: faceRegion が空(幅・高さ 0)→ ArgumentException(要件 3.7 を呼び出し経路でも確認)
    [Fact]
    public void ExtractEmbeddingAsync_faceRegion空はArgumentException()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(image, faceRegion: new RectangleF(50, 50, 0, 0));
        });
    }

    // 異常系: faceRegion が画像と交差しない → ArgumentException(要件 3.7)
    [Fact]
    public void ExtractEmbeddingAsync_faceRegion非交差はArgumentException()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = SolidBgr(200, 200, b: 64, g: 128, r: 192);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(image, faceRegion: new RectangleF(500, 500, 50, 50));
        });
    }

    // 異常系: null 画像 → ArgumentNullException(要件 1.6)
    [Fact]
    public void ExtractEmbeddingAsync_null画像はArgumentNullException()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(null!);
        });
    }

    // 破棄: Dispose 後の ExtractEmbeddingAsync 呼び出し → ObjectDisposedException(要件 6.5)
    [Fact]
    public void ExtractEmbeddingAsync_Dispose後はObjectDisposedException()
    {
        FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image = WhiteSquare();
        recognizer.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = recognizer.ExtractEmbeddingAsync(image);
        });
    }

    // --- CompareFacesAsync(Mat, Mat)パイプラインと Status 3 分岐(タスク 5.4) ---
    // 検出 fixture ㉑ は 640x640 単色の平均を conf に使う。明色(≥ 約 179)は検出、黒(0)は未検出。
    // 埋め込み fixture ⑰ は単色画像で [mean(R), mean(G), mean(B), 1.0] = [(x−127.5)/128 ×3, 1] を出力する。
    private static Mat BrightSquare(byte value) => new(640, 640, MatType.CV_8UC3, Scalar.All(value));

    // 正常系: 双方が明色(検出あり)→ Status=Success・Similarity は埋め込みの解析的コサイン類似度・Face1/Face2 非 null(要件 4.1, 4.2)。
    // 要件 4.5: 返却は類似度のみで同一人物判定を持たない(FaceComparisonResult に判定フィールドが無いことは型で担保。Success 時 Similarity のみ設定を確認)。
    [Fact]
    public async Task CompareFacesAsync_双方検出_Successと解析的類似度を返す()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        // 200/255 ≈ 0.78・190/255 ≈ 0.75 はいずれも既定 detectionThreshold=0.7 を超える。
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BrightSquare(190);

        FaceComparisonResult result = await recognizer.CompareFacesAsync(image1, image2);

        // 単色のため各埋め込みは [Normalize(v)×3, 1.0]。CompareEmbeddings の期待値と照合する(解析検証)。
        float[] expected1 = [Normalize(200), Normalize(200), Normalize(200), 1f];
        float[] expected2 = [Normalize(190), Normalize(190), Normalize(190), 1f];
        float expected = FaceRecognizer.CompareEmbeddings(expected1, expected2);

        Assert.Equal(FaceComparisonStatus.Success, result.Status);
        Assert.Equal(expected, result.Similarity, Epsilon);
        Assert.InRange(result.Similarity, -1f, 1f);
        Assert.NotNull(result.Face1);
        Assert.NotNull(result.Face2);
    }

    // 異常系: 画像 1 が黒(未検出)→ Status=NoFaceInImage1・Similarity=0・Face1=null(画像 2 が明色でも)(要件 4.3)
    [Fact]
    public async Task CompareFacesAsync_画像1未検出_NoFaceInImage1()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BlackSquare();
        using Mat image2 = BrightSquare(200);

        FaceComparisonResult result = await recognizer.CompareFacesAsync(image1, image2);

        Assert.Equal(FaceComparisonStatus.NoFaceInImage1, result.Status);
        Assert.Equal(0f, result.Similarity);
        Assert.Null(result.Face1);
    }

    // 異常系: 両画像とも黒(いずれも未検出)→ NoFaceInImage1(要件 4.3 の但し書き。画像 1 未検出を優先)
    [Fact]
    public async Task CompareFacesAsync_両画像未検出_NoFaceInImage1()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BlackSquare();
        using Mat image2 = BlackSquare();

        FaceComparisonResult result = await recognizer.CompareFacesAsync(image1, image2);

        Assert.Equal(FaceComparisonStatus.NoFaceInImage1, result.Status);
        Assert.Equal(0f, result.Similarity);
    }

    // 異常系: 画像 1 明色・画像 2 黒(画像 2 のみ未検出)→ Status=NoFaceInImage2・Similarity=0・Face2=null(要件 4.4)
    [Fact]
    public async Task CompareFacesAsync_画像2未検出_NoFaceInImage2()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BlackSquare();

        FaceComparisonResult result = await recognizer.CompareFacesAsync(image1, image2);

        Assert.Equal(FaceComparisonStatus.NoFaceInImage2, result.Status);
        Assert.Equal(0f, result.Similarity);
        Assert.Null(result.Face2);
    }

    // 要件 4.6: 既定閾値 0.7 / 0.5 が使われる(閾値を渡さない呼び出しが成立し、明色 2 枚が検出される)
    [Fact]
    public async Task CompareFacesAsync_既定閾値で双方検出が成立する()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BrightSquare(190);

        FaceComparisonResult result = await recognizer.CompareFacesAsync(image1, image2);

        Assert.Equal(FaceComparisonStatus.Success, result.Status);
        Assert.InRange(result.Face1!.Confidence, 0.7f, 1f);
        Assert.InRange(result.Face2!.Confidence, 0.7f, 1f);
    }

    // 異常系: detectionThreshold 範囲外 → ArgumentException(検出前の同期送出。要件 4.7)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void CompareFacesAsync_detectionThreshold範囲外はArgumentException(float threshold)
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BrightSquare(190);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.CompareFacesAsync(image1, image2, detectionThreshold: threshold);
        });
    }

    // 異常系: nmsThreshold 範囲外 → ArgumentException(検出前の同期送出。要件 4.7)
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void CompareFacesAsync_nmsThreshold範囲外はArgumentException(float threshold)
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BrightSquare(190);

        _ = Assert.Throws<ArgumentException>(() =>
        {
            _ = recognizer.CompareFacesAsync(image1, image2, nmsThreshold: threshold);
        });
    }

    // 異常系: null 画像 1 → ArgumentNullException(要件 1.6 と同契約)
    [Fact]
    public void CompareFacesAsync_null画像1はArgumentNullException()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image2 = BrightSquare(200);

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = recognizer.CompareFacesAsync(null!, image2);
        });
    }

    // 異常系: null 画像 2 → ArgumentNullException(画像 1 が有効でも同期送出)
    [Fact]
    public void CompareFacesAsync_null画像2はArgumentNullException()
    {
        using FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);

        _ = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = recognizer.CompareFacesAsync(image1, null!);
        });
    }

    // 破棄: Dispose 後の CompareFacesAsync 呼び出し → ObjectDisposedException(要件 6.5)
    [Fact]
    public void CompareFacesAsync_Dispose後はObjectDisposedException()
    {
        FaceRecognizer recognizer = ExtractRecognizer();
        using Mat image1 = BrightSquare(200);
        using Mat image2 = BrightSquare(190);
        recognizer.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = recognizer.CompareFacesAsync(image1, image2);
        });
    }
}
