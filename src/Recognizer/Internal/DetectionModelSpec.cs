namespace Recognizer.Internal;

/// <summary>
/// ONNX メタデータから判別した顔検出モデルの入力仕様(構築後は不変)。
/// 出力形式(転置/標準・F)はここに保持しない。design §6 の責務分担に従い、
/// パースに用いる形式は常に初回 Run の実形状に規則 (d) を適用した結果を正とするため。
/// </summary>
/// <param name="Layout">入力レイアウト(NCHW / NHWC)。</param>
/// <param name="InputWidth">入力幅(動的軸なら既定 640)。</param>
/// <param name="InputHeight">入力高さ(動的軸なら既定 640)。</param>
/// <param name="InputName">推論入力テンソル名。</param>
/// <param name="OutputName">推論出力テンソル名。</param>
internal sealed record DetectionModelSpec(
    TensorLayout Layout,
    int InputWidth,
    int InputHeight,
    string InputName,
    string OutputName);
