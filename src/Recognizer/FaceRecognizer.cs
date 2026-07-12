using Microsoft.ML.OnnxRuntime;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// 顔認証(顔埋め込みの抽出と比較)を提供する公開 API。
/// 顔検出は <see cref="FaceDetector"/> を内包して再利用し、埋め込みモデルの推論セッションを所有する(design §6)。
/// 非同期メソッド(CompareFacesAsync / ExtractEmbeddingAsync)は後続タスクで追加する。
/// </summary>
public sealed class FaceRecognizer : IDisposable
{
    private readonly FaceDetector _detector;
    private readonly InferenceSession _embeddingSession;

    // 埋め込み次元 D は API 契約(要件 3.6)の要。構築後不変で保持し、後続タスクの推論で参照する。
    private readonly EmbeddingModelSpec _embeddingSpec;

    // Why volatile: Dispose と(後続タスクの)推論呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する(FaceDetector と同一方針)。
    private volatile bool _disposed;

    /// <summary>
    /// 検出モデルと埋め込みモデルをそれぞれロードして構築する。検出側は <see cref="FaceDetector"/> に委譲し、
    /// 埋め込み側はメタデータから入出力仕様と次元 D を判別する(design §6)。
    /// </summary>
    /// <param name="detectorModelPath">顔検出 ONNX モデルのファイルパス。</param>
    /// <param name="embeddingModelPath">顔埋め込み ONNX モデルのファイルパス。</param>
    /// <exception cref="ArgumentNullException">いずれかのパスが null(要件 2.7)。</exception>
    /// <exception cref="FileNotFoundException">いずれかのファイルが存在しない(要件 2.4)。</exception>
    /// <exception cref="NotSupportedException">いずれかのモデル形式を判別できない(要件 2.6)。</exception>
    public FaceRecognizer(string detectorModelPath, string embeddingModelPath)
    {
        // 事前条件: 両パスの null 検査を最優先で行い、重いモデルロードに入る前に弾く(design §6)。
        ArgumentNullException.ThrowIfNull(detectorModelPath);
        ArgumentNullException.ThrowIfNull(embeddingModelPath);

        // 検出側を先に構築する。パス存在検査・FileNotFoundException・形式判別・ORT 透過は FaceDetector に一元化し重複させない。
        FaceDetector detector = new(detectorModelPath);
        try
        {
            if (!File.Exists(embeddingModelPath))
            {
                throw new FileNotFoundException("モデルファイルが見つかりません。", embeddingModelPath);
            }

            // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.5)。
            InferenceSession embeddingSession = new(embeddingModelPath);
            try
            {
                _embeddingSpec = ModelIntrospector.IntrospectEmbedding(embeddingSession);
            }
            catch
            {
                // Why: 形式判別失敗時に埋め込みセッションをリークさせないため、送出前に破棄する。
                embeddingSession.Dispose();
                throw;
            }

            _embeddingSession = embeddingSession;
        }
        catch
        {
            // Why: 部分構築を残さない。埋め込み側の構築が失敗したら、生成済みの内包 FaceDetector を破棄してから送出する(リーク防止。design §6)。
            detector.Dispose();
            throw;
        }

        _detector = detector;
    }

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

    /// <summary>内包する検出器と埋め込み推論セッションを解放する。二重呼び出しは安全(要件 6.4)。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector.Dispose();
        _embeddingSession.Dispose();
    }

    // 破棄済みインスタンスへの非同期メソッド呼び出しを ObjectDisposedException で弾く(要件 6.5)。
    // Why not: 本タスクでは非同期メソッド未実装のため未使用だが、5.3 以降のガード機構として先行実装する。
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
