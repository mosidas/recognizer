using Recognizer;

namespace Recognizer.Tests;

/// <summary>
/// FaceRecognizer.CompareEmbeddings(static コサイン類似度)の単体テスト。
/// 要件 5.1〜5.5 / design §6 の CompareEmbeddings 契約を検証する。
/// </summary>
public sealed class FaceRecognizerTests
{
    // 要件 5.5 で規定される許容誤差(浮動小数点演算のため厳密比較しない)。
    private const float Epsilon = 1e-5f;

    [Fact]
    public void CompareEmbeddings_IdenticalVectors_ReturnsOne()
    {
        // 要件 5.5: 同一ベクトル同士の類似度は 1.0(許容誤差内)。
        float[] embedding = [1f, 2f, 3f, 4f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding, embedding);

        Assert.Equal(1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_ScaledSameDirection_ReturnsOne()
    {
        // コサイン類似度はスケール不変であり、向きが同じなら 1.0。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [2f, 4f, 6f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_OppositeVectors_ReturnsMinusOne()
    {
        // 要件 5.5: 逆向きベクトル同士は -1.0(許容誤差内)。
        float[] embedding1 = [1f, 2f, 3f, 4f];
        float[] embedding2 = [-1f, -2f, -3f, -4f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(-1f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_OrthogonalVectors_ReturnsZero()
    {
        // 直交ベクトルのコサイン類似度は 0。
        float[] embedding1 = [1f, 0f];
        float[] embedding2 = [0f, 1f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_DimensionMismatch_ThrowsArgumentException()
    {
        // 要件 5.3: 次元(長さ)不一致は ArgumentException。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [1f, 2f];

        Assert.Throws<ArgumentException>(() =>
        {
            // ReadOnlySpan はラムダのクロージャに載せられないためローカルで取得する。
            _ = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);
        });
    }

    [Fact]
    public void CompareEmbeddings_FirstIsZeroVector_ReturnsZero()
    {
        // 要件 5.4: いずれかがゼロベクトル(ノルム 0)なら 0。
        float[] embedding1 = [0f, 0f, 0f];
        float[] embedding2 = [1f, 2f, 3f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_SecondIsZeroVector_ReturnsZero()
    {
        // 要件 5.4: 第 2 引数がゼロベクトルの場合も 0。
        float[] embedding1 = [1f, 2f, 3f];
        float[] embedding2 = [0f, 0f, 0f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_BothZeroVectors_ReturnsZero()
    {
        // 要件 5.4: 両方ゼロベクトルでも 0(0/0 の未定義を安全側に倒す)。
        float[] embedding1 = [0f, 0f];
        float[] embedding2 = [0f, 0f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.Equal(0f, similarity, Epsilon);
    }

    [Fact]
    public void CompareEmbeddings_Result_IsWithinValidRange()
    {
        // 要件 5.2: 返却値は常に [-1, 1]。浮動小数点誤差でこの範囲を超えないこと。
        float[] embedding1 = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f];
        float[] embedding2 = [0.5f, 0.4f, 0.3f, 0.2f, 0.1f];

        float similarity = FaceRecognizer.CompareEmbeddings(embedding1, embedding2);

        Assert.InRange(similarity, -1f, 1f);
    }
}
