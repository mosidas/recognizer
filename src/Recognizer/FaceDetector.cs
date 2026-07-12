using Microsoft.ML.OnnxRuntime;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// YOLO 形式 ONNX モデルによる顔検出。モデルのロードと形式判別をコンストラクタで行い、
/// 推論セッションのライフサイクルを所有する(design §6)。同一インスタンスへの並行検出を許可する。
/// </summary>
public sealed class FaceDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly DetectionModelSpec _modelSpec;

    // Why volatile: Dispose と(後続タスクの)検出呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する。
    private volatile bool _disposed;

    /// <summary>
    /// モデルをロードし、ONNX メタデータから入力レイアウト・サイズ・出力形式を判別する。
    /// </summary>
    /// <param name="modelPath">顔検出 ONNX モデルのファイルパス。</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelPath"/> が null(要件 2.7)。</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない(要件 2.4)。</exception>
    /// <exception cref="NotSupportedException">判別できないモデル形式(要件 2.6)。</exception>
    public FaceDetector(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("モデルファイルが見つかりません。", modelPath);
        }

        // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.5)。
        InferenceSession session = new(modelPath);
        try
        {
            _modelSpec = ModelIntrospector.Introspect(session);
        }
        catch
        {
            // Why: 形式判別失敗時にセッションをリークさせないため、送出前に破棄する。
            session.Dispose();
            throw;
        }

        _session = session;
    }

    /// <summary>推論セッションを解放する。二重呼び出しは安全(要件 4.4)。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
