namespace Recognizer.Cli;

/// <summary>プロセスの終了コード(design §8.3・要件 7.3)。</summary>
internal static class ExitCodes
{
    /// <summary>成功。検出 0 件・顔未検出・<c>--help</c> を含む(要件 7.9)。</summary>
    public const int Success = 0;

    /// <summary>実行時エラー。外部資源(画像・モデル・クラス名ファイル)の読み込み/解釈に失敗した(design §8.1)。</summary>
    public const int RuntimeError = 1;

    /// <summary>使用法エラー。引数の書式・組み合わせ・値域が不正でコマンドを開始できない(design §8.2)。</summary>
    public const int UsageError = 2;
}
