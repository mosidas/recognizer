namespace Recognizer.Internal;

/// <summary>
/// モデル入力テンソルのチャネル軸配置。
/// </summary>
internal enum TensorLayout
{
    /// <summary>[N, 3, H, W](チャネル先頭)。</summary>
    Nchw,

    /// <summary>[N, H, W, 3](チャネル末尾)。</summary>
    Nhwc,
}
