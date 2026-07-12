using Microsoft.ML.OnnxRuntime;

namespace Recognizer.Tests;

/// <summary>
/// FaceDetector のコンストラクタ(ガード・モデルロード・形式判別)と Dispose の契約テスト。
/// DetectAsync 系(検出・オーバーロード・キャンセル・並行・Dispose 後呼び出し)は後続タスクの責務。
/// </summary>
public sealed class FaceDetectorTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    // 正常系: 対応 fixture で構築でき、形式判別が例外なく完了する(要件 2.1)
    [Fact]
    public void Constructor_対応モデルで構築できる()
    {
        using FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        Assert.NotNull(detector);
    }

    // 異常系: null の modelPath は ArgumentNullException(要件 2.7)
    [Fact]
    public void Constructor_nullパスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new FaceDetector(null!));
    }

    // 異常系: 存在しないパスは FileNotFoundException(要件 2.4)
    [Fact]
    public void Constructor_存在しないパスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(() => new FaceDetector(missing));
    }

    // 異常系: ONNX として不正なバイト列は OnnxRuntime の例外を包まず透過する(要件 2.5)
    // 一時ファイルを用いるのは fixture(有効な ONNX のみ)にロード失敗ケースが存在しないため。
    [Fact]
    public void Constructor_不正なONNXはOnnxRuntimeExceptionを透過する()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03, 0x04]);
        try
        {
            // Assert.Throws は厳密型一致のため、ラップされていない(透過している)ことを保証する。
            _ = Assert.Throws<OnnxRuntimeException>(() => new FaceDetector(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: 判別できない形式(F=7)は NotSupportedException(要件 2.6)
    [Fact]
    public void Constructor_非対応形式はNotSupportedException()
    {
        string path = FixturePath("face_unsupported_f7.onnx");

        _ = Assert.Throws<NotSupportedException>(() => new FaceDetector(path));
    }

    // 破棄: Dispose を呼べる(要件 4.4)
    [Fact]
    public void Dispose_呼び出せる()
    {
        FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // 破棄: 二重 Dispose が例外を投げず安全である(要件 4.4)
    [Fact]
    public void Dispose_二重呼び出しが安全()
    {
        FaceDetector detector = new(FixturePath("face_nchw_transposed_f5.onnx"));
        detector.Dispose();

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }
}
