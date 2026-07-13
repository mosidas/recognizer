namespace Recognizer.Cli.Errors;

/// <summary>
/// 発生箇所でしか判別できない実行時エラーを、<c>code</c> ごと <c>CliApplication</c> の catch へ搬送する
/// CLI 内部専用の例外(design §8.1)。
/// </summary>
// Why not IOException 系から派生させない: --classes のファイル不在は FileNotFoundException だが、
// モデルファイル不在も同じ型であり、型だけでは区別できない(要件 7.6・7.7)。IOException 系を継承すると
// RuntimeErrorMapper の順 2(FileNotFoundException → modelNotFound)に吸われ、発生箇所の情報が失われる。
// Why not 結果型で表さない: Action の戻り値は int 固定(SetAction)で ErrorOutput を返す経路が無く、
// 搬送手段は例外に限られる。プロセスをそのまま非 0 終了させる終端エラーでもある(design §8.1)。
internal sealed class CliRuntimeException : Exception
{
    /// <param name="message">利用者に見せる日本語メッセージ。</param>
    /// <param name="code">エラー JSON の <c>code</c>(<see cref="ErrorCodes"/> のいずれか)。</param>
    // Why not 引数なし/message のみのコンストラクタを併置しない: code の無い CliRuntimeException は
    // 分類できず存在してはならない状態のため、完全コンストラクタだけを提供して不正な状態を作れなくする。
    public CliRuntimeException(string message, string code)
        : base(message)
    {
        Code = code;
    }

    public CliRuntimeException(string message, string code, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>この実行時エラーの <c>code</c>。<see cref="RuntimeErrorMapper"/> がそのまま採用する。</summary>
    public string Code { get; }
}
