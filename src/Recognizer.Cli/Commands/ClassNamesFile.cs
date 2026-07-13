using Recognizer.Cli.Errors;

namespace Recognizer.Cli.Commands;

/// <summary>
/// <c>--classes</c> で指定されたファイルを 1 行 1 クラス名として読み込む(要件 4.4・4.6・7.6 / design §6)。
/// </summary>
internal static class ClassNamesFile
{
    /// <summary>
    /// <paramref name="path"/> の各行を前後空白除去した文字列を、空行を除いて行順に返す。
    /// </summary>
    /// <exception cref="CliRuntimeException">
    /// 読み込みに失敗した場合。<c>Code</c> に <see cref="ErrorCodes.ClassesFileNotFound"/> または
    /// <see cref="ErrorCodes.ClassesFileReadFailed"/> を保持する。
    /// </exception>
    public static IReadOnlyList<string> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Why not 空パスを File.ReadAllLines に渡さない: ArgumentException が飛び、RuntimeErrorMapper の順 5
        // (ArgumentException → imageLoadFailed)に吸われて画像起因のエラーとして報告される。順 5 は「ここに
        // 到達する ArgumentException は画像起因に限られる」(前提 P1 / design §8.1)を根拠にしており、
        // クラス名ファイルの読み込みがその前提を破ってはならない。シェル変数の未展開(--classes "$UNSET")で現実に踏む。
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliRuntimeException(
                "クラス名ファイルのパスが空です。",
                ErrorCodes.ClassesFileNotFound);
        }

        // Why not 行数をモデルのクラス数と突き合わせない: 不一致でもエラーにせず、範囲外の ClassId は
        // ライブラリが class_{id} にフォールバックさせる(要件 4.6。ObjectDetector.ResolveClassName)。
        // ここでモデルを覗くと、CLI が推論ロジックを持たないという境界(design §2)も破れる。
        try
        {
            return
            [
                .. File.ReadAllLines(path)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
            ];
        }

        // Why not 素の FileNotFoundException を外へ漏らさない: モデルファイル不在も同じ型であり、漏らすと
        // RuntimeErrorMapper の順 2 に一致して modelNotFound と誤判定される(要件 7.6 違反 / design §8.1)。
        // 例外型では区別できない発生箇所の情報を、code として例外に載せて搬送する。
        // Why not catch を IOException 一本にまとめない: FileNotFoundException / DirectoryNotFoundException は
        // IOException の派生であり、基底を先に捕捉すると「見つからない」も readFailed に吸われる(派生 → 基底の順)。
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CliRuntimeException(
                $"クラス名ファイルが見つかりません: {path}",
                ErrorCodes.ClassesFileNotFound,
                exception);
        }
        // Why not ArgumentException を素通しさせない: 不正な文字を含むパス等でも File.ReadAllLines は
        // ArgumentException を投げる。漏らすと順 5 の imageLoadFailed に吸われ、前提 P1 が崩れる。
        // ClassNamesFile からは ArgumentException を一切外に出さない(発生箇所の情報を code に載せて搬送する)。
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new CliRuntimeException(
                $"クラス名ファイルを読み込めませんでした: {path}",
                ErrorCodes.ClassesFileReadFailed,
                exception);
        }
    }
}
