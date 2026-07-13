using Recognizer.Cli.Output;

namespace Recognizer.Cli.Errors;

/// <summary>
/// パース中に <c>CustomParser</c> が検出した値エラー(解釈不能 / 値域外)を、実行単位で収集する
/// (design §6・§8.2 の順序 1・2)。<c>UsageErrorClassifier</c> がここの記録を最優先で参照する。
/// </summary>
// Why not 静的にしない: 記録は可変状態であり、実行単位ごとに新規生成して破棄する(design §6 CliApplication)。
// 静的にするとテストの並行実行や連続実行で記録が混線する。
// Why not 独自の記録型を作らない: 必要な情報は「日本語メッセージ」と「code」の 2 つで、
// エラー JSON の ErrorOutput と同一。分類器(UsageErrorClassifier)の戻り値もこの型のため、変換が要らない。
internal sealed class UsageErrorCollector
{
    private readonly List<ErrorOutput> _errors = [];

    /// <summary>記録された値エラー(検出順)。</summary>
    public IReadOnlyList<ErrorOutput> Errors => _errors;

    /// <summary>値エラーを記録する。<paramref name="code"/> は <see cref="ErrorCodes"/> の使用法エラー定数。</summary>
    public void Add(string message, string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentException.ThrowIfNullOrEmpty(code);

        _errors.Add(new ErrorOutput(message, code));
    }
}
