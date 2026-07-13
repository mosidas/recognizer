using Microsoft.ML.OnnxRuntime;
using Recognizer.Cli.Output;

namespace Recognizer.Cli.Errors;

/// <summary>
/// 実行時に送出された例外を、エラー JSON(<c>error</c> / <c>code</c>)へ写す(design §8.1・要件 7.1・7.7)。
/// </summary>
internal static class RuntimeErrorMapper
{
    /// <summary>例外を design §8.1 の対応表で分類する。表を上から順に評価し、最初に一致した行の <c>code</c> を返す。</summary>
    public static ErrorOutput Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Why not 型の並びを入れ替えない: 順 1 の CliRuntimeException を先頭に置くことで、--classes の
        // ファイル不在(ClassNamesFile が code を付けて包む)が順 2 の modelNotFound に吸われるのを防ぐ
        // (要件 7.6)。順 5 の ArgumentException が最後尾なのは、閾値の範囲外を CLI が事前検証済みで
        // ここに到達する ArgumentException が画像起因に限られるという前提(P1・research §3)に依るため。
        return exception switch
        {
            CliRuntimeException cli => new ErrorOutput(cli.Message, cli.Code),
            FileNotFoundException notFound => new ErrorOutput(
                $"モデルファイルが見つかりません: {DescribeMissingFile(notFound)}",
                ErrorCodes.ModelNotFound),
            OnnxRuntimeException onnx => new ErrorOutput(
                $"モデルを読み込めませんでした: {onnx.Message}",
                ErrorCodes.ModelLoadFailed),
            NotSupportedException notSupported => new ErrorOutput(
                $"非対応のモデル形式です: {notSupported.Message}",
                ErrorCodes.UnsupportedModelFormat),
            ArgumentException argument => new ErrorOutput(
                DescribeImageFailure(argument),
                ErrorCodes.ImageLoadFailed),
            _ => new ErrorOutput(
                $"予期しないエラーが発生しました: {exception.Message}",
                ErrorCodes.UnexpectedError),
        };
    }

    // Why not Message をそのまま使わない: FileNotFoundException.Message は .NET が "File name: '...'" を
    // 改行付きで連結するため、パスだけを取り出せる FileName を優先する(FileName が空の実装に備えて Message へ退避)。
    private static string DescribeMissingFile(FileNotFoundException exception)
        => string.IsNullOrEmpty(exception.FileName) ? exception.Message : exception.FileName;

    private const string ImageFailurePrefix = "画像を読み込めませんでした";

    // ライブラリの ArgumentException は ".NET が付ける ' (Parameter ...)' 接尾辞" を持ち、メッセージ自体が
    // 既に「画像を読み込めませんでした: <path>」で始まることがある(ImageDecoder)。素朴に接頭辞を足すと
    // 「画像を読み込めませんでした: 画像を読み込めませんでした: ...」と二重になるため、原因部分だけを取り出す。
    private static string DescribeImageFailure(ArgumentException exception)
    {
        string reason = StripParameterSuffix(exception.Message).TrimEnd();

        return reason.StartsWith(ImageFailurePrefix, StringComparison.Ordinal)
            ? reason
            : $"{ImageFailurePrefix}: {reason}";
    }

    // Why not IndexOf を使わない: 画像パス自体が " (Parameter" を含むと(利用者が付けたファイル名は任意)
    // メッセージ本体を途中で切り落とし、どのファイルで失敗したか分からなくなる。接尾辞は必ず末尾にあり
    // ")" で閉じるため、末尾からの一致に限定する。
    private static string StripParameterSuffix(string message)
    {
        if (!message.EndsWith(')'))
        {
            return message;
        }

        int suffix = message.LastIndexOf(" (Parameter", StringComparison.Ordinal);
        return suffix < 0 ? message : message[..suffix];
    }
}
