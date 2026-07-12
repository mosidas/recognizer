namespace Recognizer;

/// <summary>
/// 顔認証(顔埋め込みの抽出と比較)を提供する公開 API。
/// 本ファイルでは埋め込み比較の static メソッドのみを定義する
/// (コンストラクタ・非同期メソッド・Dispose は後続タスクで追加)。
/// </summary>
public sealed class FaceRecognizer
{
    /// <summary>
    /// 2 つの顔埋め込みのコサイン類似度を返す。値域は [-1, 1]。
    /// </summary>
    /// <param name="embedding1">埋め込みベクトル 1(モデル出力の生ベクトル)。</param>
    /// <param name="embedding2">埋め込みベクトル 2(モデル出力の生ベクトル)。</param>
    /// <returns>コサイン類似度 [-1, 1]。いずれかがゼロベクトルの場合は 0。</returns>
    /// <exception cref="ArgumentException">2 つの次元(長さ)が一致しない場合。</exception>
    public static float CompareEmbeddings(ReadOnlySpan<float> embedding1, ReadOnlySpan<float> embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException(
                $"埋め込みの次元が一致しません(embedding1={embedding1.Length}, embedding2={embedding2.Length})。",
                nameof(embedding2));
        }

        // 生ベクトルから内積とノルムを 1 パスで算出する(L2 正規化はここでのみ行う)。
        float dot = 0f;
        float squaredNorm1 = 0f;
        float squaredNorm2 = 0f;
        for (int i = 0; i < embedding1.Length; i++)
        {
            float v1 = embedding1[i];
            float v2 = embedding2[i];
            dot += v1 * v2;
            squaredNorm1 += v1 * v1;
            squaredNorm2 += v2 * v2;
        }

        // ゼロベクトルのコサイン類似度は数学的に未定義のため安全側の 0 を返す(要件 5.4)。
        if (squaredNorm1 == 0f || squaredNorm2 == 0f)
        {
            return 0f;
        }

        float similarity = dot / (MathF.Sqrt(squaredNorm1) * MathF.Sqrt(squaredNorm2));

        // 浮動小数点誤差で [-1, 1] を僅かに超えうるためクランプする(要件 5.2)。
        return Math.Clamp(similarity, -1f, 1f);
    }
}
