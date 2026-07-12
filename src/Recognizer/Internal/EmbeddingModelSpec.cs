namespace Recognizer.Internal;

/// <summary>
/// ONNX メタデータから判別した埋め込みモデルの仕様(構築後は不変)。
/// 次元 D は API 契約(要件 3.6)の要であり、構築時に確定させる(design §6 (e-c))。
/// </summary>
/// <param name="Layout">入力レイアウト(NCHW / NHWC)。</param>
/// <param name="InputWidth">入力幅(動的軸なら既定 112)。</param>
/// <param name="InputHeight">入力高さ(動的軸なら既定 112)。</param>
/// <param name="InputName">推論入力テンソル名。</param>
/// <param name="OutputName">推論出力テンソル名。</param>
/// <param name="Dimension">埋め込み次元 D(静的な正値)。</param>
internal sealed record EmbeddingModelSpec(
    TensorLayout Layout,
    int InputWidth,
    int InputHeight,
    string InputName,
    string OutputName,
    int Dimension);
