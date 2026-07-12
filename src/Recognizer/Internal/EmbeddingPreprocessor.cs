using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Recognizer.Internal;

/// <summary>
/// 切り出し済み顔 <see cref="Mat"/>(BGR)を単純リサイズ・BGR→RGB 変換・<c>(x−127.5)/128</c> 正規化し、
/// モデルのレイアウト(NCHW / NHWC)に従った <see cref="DenseTensor{T}"/> に詰める無状態ユーティリティ。
/// ArcFace 系埋め込みモデルの標準前処理(design §6 / research.md §2)。
/// </summary>
internal static class EmbeddingPreprocessor
{
    /// <summary>正規化のオフセット。ArcFace 系慣習 <c>(x − 127.5) / 128</c>(≈ [-1, 1])の中心。</summary>
    private const float NormalizationOffset = 127.5f;

    /// <summary>正規化のスケール(1/128)。出典: research.md §2(InsightFace 公式)。</summary>
    private const float NormalizationScale = 1f / 128f;

    /// <summary>
    /// 切り出し顔を前処理して推論入力テンソルを生成する。
    /// 事前条件: <paramref name="croppedFace"/> は非 null・非空(FaceCropper の事後条件を信頼し再検査しない。design §6)。
    /// 事後条件: テンソル形状は <paramref name="spec"/> のレイアウト・入力サイズと一致する。
    /// </summary>
    public static DenseTensor<float> Preprocess(Mat croppedFace, EmbeddingModelSpec spec)
    {
        int inputWidth = spec.InputWidth;
        int inputHeight = spec.InputHeight;

        // Why not letterbox: 切り出しは正方形前提でアスペクト歪みが生じないため、
        // ArcFace 系慣習どおり単純リサイズ(パディングなし)にする(research.md §2)。
        // 中間 Mat は using で確実に破棄する(ネイティブメモリのリーク防止)。
        using Mat resized = new();
        Cv2.Resize(croppedFace, resized, new Size(inputWidth, inputHeight), 0, 0, InterpolationFlags.Linear);

        using Mat rgb = new();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        // row-major(y*W+x)で全画素を取得。要素は RGB 順の Vec3b(Item0=R, Item1=G, Item2=B)。
        rgb.GetArray(out Vec3b[] pixels);

        // DenseTensor は既定で row-major の連続バッファを持つため Buffer へ線形 index で直接書き込む。
        int[] dimensions = spec.Layout == TensorLayout.Nchw
            ? [1, 3, inputHeight, inputWidth]
            : [1, inputHeight, inputWidth, 3];
        DenseTensor<float> tensor = new(dimensions);
        Span<float> buffer = tensor.Buffer.Span;

        int pixelCount = inputHeight * inputWidth;

        if (spec.Layout == TensorLayout.Nchw)
        {
            // NCHW: index = c * pixelCount + (y*W+x)。チャネルごとに平面が連続する。
            for (int i = 0; i < pixelCount; i++)
            {
                Vec3b pixel = pixels[i];
                buffer[i] = (pixel.Item0 - NormalizationOffset) * NormalizationScale;
                buffer[pixelCount + i] = (pixel.Item1 - NormalizationOffset) * NormalizationScale;
                buffer[(2 * pixelCount) + i] = (pixel.Item2 - NormalizationOffset) * NormalizationScale;
            }
        }
        else
        {
            // NHWC: index = (y*W+x) * 3 + c。画素ごとに RGB が連続する。
            for (int i = 0; i < pixelCount; i++)
            {
                Vec3b pixel = pixels[i];
                int offset = i * 3;
                buffer[offset] = (pixel.Item0 - NormalizationOffset) * NormalizationScale;
                buffer[offset + 1] = (pixel.Item1 - NormalizationOffset) * NormalizationScale;
                buffer[offset + 2] = (pixel.Item2 - NormalizationOffset) * NormalizationScale;
            }
        }

        return tensor;
    }
}
