using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Recognizer.Internal;

/// <summary>
/// BGR の <see cref="Mat"/> を letterbox リサイズ・BGR→RGB 変換・/255 正規化し、
/// モデルのレイアウト(NCHW / NHWC)に従った <see cref="DenseTensor{T}"/> に詰める無状態ユーティリティ。
/// YOLO 系の標準前処理(design §6)。座標逆変換に用いる <see cref="LetterboxParams"/> も併せて返す。
/// </summary>
internal static class Preprocessor
{
    /// <summary>letterbox 余白の定数パディング値(YOLO 系標準の 114)。</summary>
    private const int PaddingValue = 114;

    /// <summary>uint8 画素を [0, 1] の float32 に正規化する係数。</summary>
    private const float NormalizationScale = 1f / 255f;

    /// <summary>
    /// 入力画像を前処理して推論入力テンソルと letterbox パラメータを生成する。
    /// 事前条件: <paramref name="image"/> は非 null・非空(上流のガード済みのため再検査しない。design §6)。
    /// 事後条件: テンソル形状は <paramref name="spec"/> のレイアウト・入力サイズと一致する。
    /// </summary>
    public static (DenseTensor<float> Tensor, LetterboxParams Params) Preprocess(Mat image, DetectionModelSpec spec)
    {
        int sourceWidth = image.Width;
        int sourceHeight = image.Height;
        int inputWidth = spec.InputWidth;
        int inputHeight = spec.InputHeight;

        LetterboxParams letterbox = LetterboxParams.Create(sourceWidth, sourceHeight, inputWidth, inputHeight);

        // アスペクト比維持のリサイズ後サイズ。scale は min 比のため両辺とも入力サイズ以下になる。
        int resizedWidth = (int)Math.Round(sourceWidth * letterbox.Scale, MidpointRounding.AwayFromZero);
        int resizedHeight = (int)Math.Round(sourceHeight * letterbox.Scale, MidpointRounding.AwayFromZero);

        // Why not: 極端な縮小率で 0 になると Cv2.Resize が例外を投げるため下限 1 に退避する。
        resizedWidth = Math.Clamp(resizedWidth, 1, inputWidth);
        resizedHeight = Math.Clamp(resizedHeight, 1, inputHeight);

        // Why not: 余白を整数で中央配置し、反対側で辻褄を合わせることで letterbox 画像を入力サイズに
        // 厳密一致させる(丸め起因の 1px ずれでテンソル形状が食い違うのを防ぐ)。
        int padLeft = (inputWidth - resizedWidth) / 2;
        int padRight = inputWidth - resizedWidth - padLeft;
        int padTop = (inputHeight - resizedHeight) / 2;
        int padBottom = inputHeight - resizedHeight - padTop;

        // 中間 Mat は using で確実に破棄する(ネイティブメモリのリーク防止)。
        using Mat resized = new();
        Cv2.Resize(image, resized, new Size(resizedWidth, resizedHeight), 0, 0, InterpolationFlags.Linear);

        using Mat letterboxed = new();
        Cv2.CopyMakeBorder(
            resized, letterboxed, padTop, padBottom, padLeft, padRight,
            BorderTypes.Constant, new Scalar(PaddingValue, PaddingValue, PaddingValue));

        using Mat rgb = new();
        Cv2.CvtColor(letterboxed, rgb, ColorConversionCodes.BGR2RGB);

        // row-major(y*W+x)で全画素を取得。要素は RGB 順の Vec3b(Item0=R, Item1=G, Item2=B)。
        rgb.GetArray(out Vec3b[] pixels);

        // DenseTensor は既定で row-major(先頭次元が最上位)の連続バッファを持つため、
        // Buffer へ線形 index で直接書き込む(インデクサ経由のストライド計算を避ける)。
        int[] dimensions = spec.Layout == TensorLayout.Nchw
            ? [1, 3, inputHeight, inputWidth]
            : [1, inputHeight, inputWidth, 3];
        DenseTensor<float> tensor = new(dimensions);
        Span<float> buffer = tensor.Buffer.Span;

        int pixelCount = inputHeight * inputWidth;

        if (spec.Layout == TensorLayout.Nchw)
        {
            // NCHW: index = (c * H + y) * W + x = c * pixelCount + (y*W+x)。チャネルごとに平面が連続する。
            for (int i = 0; i < pixelCount; i++)
            {
                Vec3b pixel = pixels[i];
                buffer[i] = pixel.Item0 * NormalizationScale;
                buffer[pixelCount + i] = pixel.Item1 * NormalizationScale;
                buffer[(2 * pixelCount) + i] = pixel.Item2 * NormalizationScale;
            }
        }
        else
        {
            // NHWC: index = (y*W+x) * 3 + c。画素ごとに RGB が連続する。
            for (int i = 0; i < pixelCount; i++)
            {
                Vec3b pixel = pixels[i];
                int offset = i * 3;
                buffer[offset] = pixel.Item0 * NormalizationScale;
                buffer[offset + 1] = pixel.Item1 * NormalizationScale;
                buffer[offset + 2] = pixel.Item2 * NormalizationScale;
            }
        }

        return (tensor, letterbox);
    }
}
