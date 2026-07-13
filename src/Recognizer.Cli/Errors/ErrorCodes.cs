namespace Recognizer.Cli.Errors;

/// <summary>
/// エラー JSON の <c>code</c>(機械可読な文字列識別子)。実行時エラーは design §8.1、
/// 使用法エラーは design §8.2 の対応表に対応する(要件 7.7・7.8)。
/// </summary>
// Why not: SCREAMING_SNAKE にしない。JSON 出力全体を camelCase で統一する契約のため(要件 6.1)。
internal static class ErrorCodes
{
    // 実行時エラー(design §8.1。終了コード 1)
    public const string ModelNotFound = "modelNotFound";
    public const string ModelLoadFailed = "modelLoadFailed";
    public const string UnsupportedModelFormat = "unsupportedModelFormat";
    public const string ImageLoadFailed = "imageLoadFailed";
    public const string ClassesFileNotFound = "classesFileNotFound";
    public const string ClassesFileReadFailed = "classesFileReadFailed";
    public const string UnexpectedError = "unexpectedError";

    // 使用法エラー(design §8.2。終了コード 2。分類器は UsageErrorClassifier が担う)
    public const string InvalidOptionValue = "invalidOptionValue";
    public const string OptionValueOutOfRange = "optionValueOutOfRange";
    public const string UnrecognizedArgument = "unrecognizedArgument";
    public const string MissingRequiredOption = "missingRequiredOption";
    public const string MissingArgument = "missingArgument";
    public const string MissingCommand = "missingCommand";
    public const string InvalidUsage = "invalidUsage";
}
