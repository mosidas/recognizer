using System.Runtime.CompilerServices;
using OpenCvSharp;

namespace Recognizer.Internal;

/// <summary>
/// 画像入力(Mat / ファイルパス / エンコード済みバイト列)を検証し、検出パイプラインが扱う
/// BGR の <see cref="Mat"/> に解決する無状態ユーティリティ。フォーマット判別は OpenCV に委ねる。
/// </summary>
internal static class ImageDecoder
{
    /// <summary>
    /// Mat 入力を検証する。所有権は移動しないため、破棄は呼び出し側の責務。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> が null。</exception>
    /// <exception cref="ArgumentException"><paramref name="image"/> が空(要素数 0)。</exception>
    public static void EnsureValid(Mat image, [CallerArgumentExpression(nameof(image))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(image, paramName);

        if (image.Empty())
        {
            throw new ArgumentException("空の画像(要素数 0)は処理できません。", paramName);
        }
    }

    /// <summary>
    /// ファイルパスから画像を読み込む。フォーマットは OpenCV が自動判別する。
    /// </summary>
    /// <returns>BGR の <see cref="Mat"/>。呼び出し側が所有し、使用後に破棄する必要がある。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imagePath"/> が null。</exception>
    /// <exception cref="ArgumentException">パスが存在しない、または画像としてデコードできない。</exception>
    public static Mat DecodeFile(string imagePath, [CallerArgumentExpression(nameof(imagePath))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(imagePath, paramName);

        // Why: ImreadModes.Color で 3ch BGR を保証する(検出パイプラインの入力契約)。
        Mat image = Cv2.ImRead(imagePath, ImreadModes.Color);

        // Why not: ImRead は失敗時に例外や null ではなく空 Mat を返すため、存在しないパス・
        // 非画像・破損ファイルはここで空判定して弾く(要件 1.4)。空 Mat はリークさせず破棄する。
        if (image.Empty())
        {
            image.Dispose();
            throw new ArgumentException($"画像を読み込めませんでした: {imagePath}", paramName);
        }

        return image;
    }

    /// <summary>
    /// エンコード済みバイト列から画像をデコードする。フォーマットは OpenCV が自動判別する。
    /// </summary>
    /// <returns>BGR の <see cref="Mat"/>。呼び出し側が所有し、使用後に破棄する必要がある。</returns>
    /// <exception cref="ArgumentException">バイト列を画像としてデコードできない。</exception>
    public static Mat DecodeBytes(ReadOnlyMemory<byte> imageBytes, [CallerArgumentExpression(nameof(imageBytes))] string? paramName = null)
    {
        // Why not: 空スパンに対する ImDecode は空 Mat ではなく英語メッセージ・内部 paramName("span")の
        // ArgumentException を送出するため、ここで先に弾いて日本語メッセージ・正しい paramName に統一する
        // (空バイト列は「デコード不可」として要件 1.4 の契約に含まれる)。
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("空のバイト列は画像としてデコードできません。", paramName);
        }

        // Why not: ReadOnlySpan オーバーロードへ .Span を渡すことで byte[] へのコピーを避ける
        // (ImDecode(byte[]) 経路は ToArray 相当のコピーコストを伴うため)。
        Mat image = Cv2.ImDecode(imageBytes.Span, ImreadModes.Color);

        // Why not: ImDecode も失敗時(空・非画像・破損)は空 Mat を返す(要件 1.4)。破棄してから送出する。
        if (image.Empty())
        {
            image.Dispose();
            throw new ArgumentException("バイト列を画像としてデコードできませんでした。", paramName);
        }

        return image;
    }
}
