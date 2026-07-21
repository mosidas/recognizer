namespace Recognizer.Gui.Services;

/// <summary>
/// クラス名ファイルを 1 行 1 クラス名として読み込む。CLI の <c>--classes</c> と同一形式に揃える。
/// </summary>
public static class ClassNamesFile
{
    /// <summary>
    /// <paramref name="path"/> の各行を前後空白除去し、空行を除いて行順に返す。
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> が null・空・空白のみ。</exception>
    /// <exception cref="IOException">ファイルが存在しない・読み込みに失敗した。</exception>
    public static IReadOnlyList<string> Read(string path)
    {
        // Why not 空パスを File.ReadAllLines に渡さない: 発生する例外が呼び出し側で
        // 画像・モデル起因の失敗と区別しづらくなるため、読み込み失敗をここで明示的に表出する。
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Why not 行数をモデルのクラス数と突き合わせない: 不一致でもエラーにせず、範囲外 ID は
        // コアが class_{id} にフォールバックする(spec §3 前提・要件 3.3)。ここで判定するとその契約を壊す。
        return
        [
            .. File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
        ];
    }
}
