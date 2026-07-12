using Microsoft.ML.OnnxRuntime;

namespace Recognizer.Tests;

/// <summary>
/// ObjectDetector のコンストラクタ(ガード・物体用形式判別・classNames 保持)と Dispose の契約テスト。
/// DetectAsync 系(検出・オーバーロード・クラス単位 NMS・キャンセル・並行・Dispose 後呼び出し)は後続タスク(5.x)の責務。
/// </summary>
public sealed class ObjectDetectorTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    // 正常系: 対応 fixture(classNames 省略)で構築でき、物体用形式判別が例外なく完了する(要件 2.1)
    [Fact]
    public void Constructor_対応モデルで構築できる()
    {
        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        Assert.NotNull(detector);
    }

    // 正常系: classNames を渡しても構築できる(要件 2.1・classNames 保持)
    [Fact]
    public void Constructor_classNames付きで構築できる()
    {
        string[] classNames = ["cat", "dog", "bird"];

        using ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"), classNames);

        Assert.NotNull(detector);
    }

    // 異常系: null の modelPath は ArgumentNullException(要件 2.9)
    [Fact]
    public void Constructor_nullパスはArgumentNullException()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new ObjectDetector(null!));
    }

    // 異常系: 存在しないパスは FileNotFoundException(要件 2.6)
    [Fact]
    public void Constructor_存在しないパスはFileNotFoundException()
    {
        string missing = FixturePath("does_not_exist.onnx");

        _ = Assert.Throws<FileNotFoundException>(() => new ObjectDetector(missing));
    }

    // 異常系: ONNX として不正なバイト列は OnnxRuntime の例外を包まず透過する(要件 2.7)
    // 一時ファイルを用いるのは fixture(有効な ONNX のみ)にロード失敗ケースが存在しないため。
    [Fact]
    public void Constructor_不正なONNXはOnnxRuntimeExceptionを透過する()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(tempPath, [0x00, 0x01, 0x02, 0x03, 0x04]);
        try
        {
            // Assert.Throws は厳密型一致のため、ラップされていない(透過している)ことを保証する。
            _ = Assert.Throws<OnnxRuntimeException>(() => new ObjectDetector(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // 異常系: 判別できない物体形式(F=4 → C=0)は NotSupportedException(要件 2.8)
    [Fact]
    public void Constructor_非対応形式はNotSupportedException()
    {
        string path = FixturePath("object_unsupported_f4.onnx");

        _ = Assert.Throws<NotSupportedException>(() => new ObjectDetector(path));
    }

    // 正常系: 出力 N 軸が動的な fixture でも構築できる(構築時は分類を保留する。要件 2.1)
    [Fact]
    public void Constructor_動的出力モデルで構築できる()
    {
        using ObjectDetector detector = new(FixturePath("object_dynamic_output_4c3.onnx"));

        Assert.NotNull(detector);
    }

    // 破棄: Dispose を呼べる(要件 5.4)
    [Fact]
    public void Dispose_呼び出せる()
    {
        ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }

    // 破棄: 二重 Dispose が例外を投げず安全である(要件 5.4)
    [Fact]
    public void Dispose_二重呼び出しが安全()
    {
        ObjectDetector detector = new(FixturePath("object_nchw_transposed_4c3.onnx"));
        detector.Dispose();

        Exception? ex = Record.Exception(detector.Dispose);

        Assert.Null(ex);
    }
}
