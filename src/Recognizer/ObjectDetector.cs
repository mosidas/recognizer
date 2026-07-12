using Microsoft.ML.OnnxRuntime;
using Recognizer.Internal;

namespace Recognizer;

/// <summary>
/// YOLO 形式 ONNX モデルによる物体検出。モデルのロードと物体用形式判別をコンストラクタで行い、
/// 推論セッションのライフサイクルを所有する(design §6)。同一インスタンスへの並行検出を許可する。
/// </summary>
public sealed class ObjectDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly DetectionModelSpec _modelSpec;

    // Why not: classNames は防御的コピーせず参照を保持する(design §6, §10)。コピーしない理由は
    // 大きなリストの複製コスト回避と YAGNI。呼び出し側が構築後にリストを変更しない前提を契約とする。
    private readonly IReadOnlyList<string>? _classNames;

    // Why volatile: Dispose と(後続タスクの)検出呼び出しが別スレッドになり得るため、破棄状態の可視性を保証する。
    private volatile bool _disposed;

    /// <summary>
    /// モデルをロードし、ONNX メタデータから入力レイアウト・サイズ・物体検出の出力形式を判別する。
    /// </summary>
    /// <param name="modelPath">物体検出 ONNX モデルのファイルパス。</param>
    /// <param name="classNames">
    /// クラス名リスト(省略可)。省略時はクラス数に応じて COCO 80 名または <c>"class_{id}"</c> を解決する(後続タスク)。
    /// 指定した場合、このインスタンスは参照をそのまま保持する(防御的コピーをしない)。
    /// 構築後にリストの内容を変更しないこと(変更するとクラス名解決が変わり得る。契約違反として扱う)。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="modelPath"/> が null(要件 2.9)。</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない(要件 2.6)。</exception>
    /// <exception cref="NotSupportedException">判別できないモデル形式(要件 2.8)。</exception>
    public ObjectDetector(string modelPath, IReadOnlyList<string>? classNames = null)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("モデルファイルが見つかりません。", modelPath);
        }

        // Why not: InferenceSession 構築時の例外(OnnxRuntimeException 等)は包まず透過する(要件 2.7)。
        InferenceSession session = new(modelPath);
        try
        {
            _modelSpec = ModelIntrospector.IntrospectObject(session);
        }
        catch
        {
            // Why: 形式判別失敗時にセッションをリークさせないため、送出前に破棄する。
            session.Dispose();
            throw;
        }

        _session = session;
        _classNames = classNames;
    }

    /// <summary>推論セッションを解放する。二重呼び出しは安全(要件 5.4)。</summary>
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
