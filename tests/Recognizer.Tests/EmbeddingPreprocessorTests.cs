using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Recognizer.Internal;

namespace Recognizer.Tests;

public sealed class EmbeddingPreprocessorTests
{
    // 単色 BGR 画像を生成する。Scalar は (B, G, R) 順。
    private static Mat SolidBgr(int width, int height, byte b, byte g, byte r)
        => new(height, width, MatType.CV_8UC3, new Scalar(b, g, r));

    private static EmbeddingModelSpec Spec(TensorLayout layout, int width, int height)
        => new(layout, width, height, "input", "output", 4);

    // (x − 127.5) / 128 の期待値(research.md §2 / design §6)。
    private static float Normalize(byte value) => (value - 127.5f) / 128f;

    // 正常系: NCHW で形状 [1,3,H,W]・チャネルは RGB 順・値は (x−127.5)/128(要件 2.2 / design §6)
    [Fact]
    public void Preprocess_Nchw_形状とRGBチャネルと正規化値()
    {
        // BGR (B=200, G=100, R=50) → RGB 順で ch0=R, ch1=G, ch2=B
        using Mat image = SolidBgr(4, 4, b: 200, g: 100, r: 50);
        EmbeddingModelSpec spec = Spec(TensorLayout.Nchw, width: 4, height: 4);

        DenseTensor<float> tensor = EmbeddingPreprocessor.Preprocess(image, spec);

        Assert.Equal(new[] { 1, 3, 4, 4 }, tensor.Dimensions.ToArray());
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(Normalize(50), tensor[0, 0, y, x], 5);   // ch0 = R
                Assert.Equal(Normalize(100), tensor[0, 1, y, x], 5);  // ch1 = G
                Assert.Equal(Normalize(200), tensor[0, 2, y, x], 5);  // ch2 = B
            }
        }
    }

    // 正常系: NHWC で形状 [1,H,W,3]・画素ごとに RGB 順で連続する(design §6 の詰め順検証)
    [Fact]
    public void Preprocess_Nhwc_形状と詰め順()
    {
        using Mat image = SolidBgr(4, 4, b: 200, g: 100, r: 50);
        EmbeddingModelSpec spec = Spec(TensorLayout.Nhwc, width: 4, height: 4);

        DenseTensor<float> tensor = EmbeddingPreprocessor.Preprocess(image, spec);

        Assert.Equal(new[] { 1, 4, 4, 3 }, tensor.Dimensions.ToArray());
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(Normalize(50), tensor[0, y, x, 0], 5);   // R
                Assert.Equal(Normalize(100), tensor[0, y, x, 1], 5);  // G
                Assert.Equal(Normalize(200), tensor[0, y, x, 2], 5);  // B
            }
        }
    }

    // 正常系: 入力サイズと異なる Mat は spec の入力サイズへ単純リサイズされる(要件 2.2 / design §6)
    [Fact]
    public void Preprocess_入力サイズへリサイズされる()
    {
        // 8x16 の Mat を 4x4 spec で前処理 → 出力は入力サイズに一致
        using Mat image = SolidBgr(8, 16, b: 10, g: 20, r: 30);
        EmbeddingModelSpec spec = Spec(TensorLayout.Nchw, width: 4, height: 4);

        DenseTensor<float> tensor = EmbeddingPreprocessor.Preprocess(image, spec);

        Assert.Equal(new[] { 1, 3, 4, 4 }, tensor.Dimensions.ToArray());
        // 単色のため単純リサイズ後も値は不変(letterbox 余白が混じらないことの確認)
        Assert.Equal(Normalize(30), tensor[0, 0, 0, 0], 5); // R
        Assert.Equal(Normalize(20), tensor[0, 1, 0, 0], 5); // G
        Assert.Equal(Normalize(10), tensor[0, 2, 0, 0], 5); // B
    }
}
