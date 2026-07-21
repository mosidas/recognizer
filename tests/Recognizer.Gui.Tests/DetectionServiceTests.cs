using System.Drawing;
using Recognizer.Gui.Models;
using Recognizer.Gui.Services;

namespace Recognizer.Gui.Tests;

public sealed class DetectionServiceTests
{
    // 入力非依存の定数出力 fixture のため、任意の有効画像で検出パイプラインが走る(spec テスト方針)。
    // Why BMP: 1x1 PNG は OpenCV デコードに失敗する環境があるため、確実にデコードできる 8x8 の無圧縮 BMP を用いる。
    private const string TinyBmpBase64 =
        "Qk32AAAAAAAAADYAAAAoAAAACAAAAAgAAAABABgAAAAAAMAAAAATCwAAEwsAAAAAAAAAAAAAPFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4PFp4";

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string CreateTempImage()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bmp");
        File.WriteAllBytes(path, Convert.FromBase64String(TinyBmpBase64));
        return path;
    }

    // ---- 4.1 ClassNamesFile ----

    [Fact]
    public void ClassNamesFile_1行1クラス名で読み込む()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "person\nbicycle\ncar\n");

        IReadOnlyList<string> names = ClassNamesFile.Read(path);

        Assert.Equal(new[] { "person", "bicycle", "car" }, names);
    }

    [Fact]
    public void ClassNamesFile_空行と前後空白を除去する()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "  person  \n\n   \n car\n");

        IReadOnlyList<string> names = ClassNamesFile.Read(path);

        Assert.Equal(new[] { "person", "car" }, names);
    }

    [Fact]
    public void ClassNamesFile_不在パスは読み込み失敗を表出する()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

        _ = Assert.Throws<FileNotFoundException>(() => ClassNamesFile.Read(missing));
    }

    // ---- 4.2 / 2.1 顔検出の写像 ----

    [Fact]
    public async Task RunAsync_顔F5モデルは成功しランドマークがnull()
    {
        string image = CreateTempImage();
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_standard_f5.onnx"), image, 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.Equal(3, outcome.Detections.Count);
        Assert.All(outcome.Detections, d => Assert.Null(d.Landmarks));
        Assert.All(outcome.Detections, d => Assert.StartsWith("face #", d.Label));
        Assert.Equal(image, outcome.ImageDisplayPath);
        Assert.Null(outcome.Message);
    }

    [Fact]
    public async Task RunAsync_顔F20モデルはランドマーク5点を写す()
    {
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_transposed_f20.onnx"), CreateTempImage(), 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.NotEmpty(outcome.Detections);
        DetectionOverlay top = outcome.Detections[0];
        Assert.NotNull(top.Landmarks);
        Assert.Equal(5, top.Landmarks!.Count);
    }

    // ---- 4.4 検出 0 件でも成功 ----

    [Fact]
    public async Task RunAsync_検出0件でも成功を返す()
    {
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_standard_f5.onnx"), CreateTempImage(), 0.99f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.Empty(outcome.Detections);
        Assert.Null(outcome.Message);
    }

    // ---- 3.1 / 3.3 物体検出・クラス名の既定解決 ----

    [Fact]
    public async Task RunAsync_物体モードはクラス名をラベルに写す()
    {
        DetectionRequest request = new(
            DetectionMode.Object, FixturePath("object_nchw_standard_5c3.onnx"), CreateTempImage(), 0.5f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.NotEmpty(outcome.Detections);
        // クラス数 3(≠80)かつクラス名未指定 → コア既定解決で class_{id} にフォールバックする(要件 3.3)。
        Assert.All(outcome.Detections, d => Assert.StartsWith("class_", d.Label));
        Assert.All(outcome.Detections, d => Assert.Null(d.Landmarks));
    }

    // ---- 3.2 クラス名ファイル指定 ----

    [Fact]
    public async Task RunAsync_物体モードは指定クラス名を使う()
    {
        string classesPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(classesPath, "alpha\nbravo\ncharlie\n");
        DetectionRequest request = new(
            DetectionMode.Object, FixturePath("object_nchw_standard_5c3.onnx"), CreateTempImage(), 0.5f, 0.5f, classesPath);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.NotEmpty(outcome.Detections);
        // クラス数 3 に対し 3 名指定 → 全 ClassId(0..2)が範囲内で指定名に解決される。
        string[] expected = ["alpha", "bravo", "charlie"];
        Assert.All(outcome.Detections, d => Assert.Contains(d.Label, expected));
    }

    // ---- 4.5 キャンセル ----

    [Fact]
    public async Task RunAsync_事前キャンセルはCancelledを返す()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_standard_f5.onnx"), CreateTempImage(), 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, cts.Token);

        Assert.Equal(DetectionStatus.Cancelled, outcome.Status);
        Assert.Empty(outcome.Detections);
        Assert.NotNull(outcome.Message);
    }

    // ---- 6.1 モデルロード失敗 ----

    [Fact]
    public async Task RunAsync_モデル不在はModelLoadFailedを返す()
    {
        string missingModel = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".onnx");
        DetectionRequest request = new(
            DetectionMode.Face, missingModel, CreateTempImage(), 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.ModelLoadFailed, outcome.Status);
        Assert.NotNull(outcome.Message);
    }

    [Fact]
    public async Task RunAsync_壊れたモデルファイルはModelLoadFailedを返す()
    {
        // 存在するが ONNX として不正なファイル。不在(FileNotFoundException)とは別に、
        // OnnxRuntime のロード失敗経路が ModelLoadFailed に写ることを確認する。
        string brokenModel = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".onnx");
        File.WriteAllText(brokenModel, "this is not a valid onnx model");
        DetectionRequest request = new(
            DetectionMode.Face, brokenModel, CreateTempImage(), 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.ModelLoadFailed, outcome.Status);
        Assert.NotNull(outcome.Message);
    }

    // ---- 6.2 画像ロード失敗 ----

    [Fact]
    public async Task RunAsync_画像不正はImageLoadFailedを返す()
    {
        string missingImage = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_standard_f5.onnx"), missingImage, 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.ImageLoadFailed, outcome.Status);
        Assert.NotNull(outcome.Message);
    }

    // ---- 6.3 非対応モデル形式 ----

    [Fact]
    public async Task RunAsync_非対応モデルはUnsupportedModelを返す()
    {
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_unsupported_f7.onnx"), CreateTempImage(), 0.7f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.UnsupportedModel, outcome.Status);
        Assert.NotNull(outcome.Message);
    }

    // ---- 3.4 クラス名ファイル読み込み失敗 → 検出前に弾く ----

    [Fact]
    public async Task RunAsync_クラス名ファイル失敗はClassNamesFileFailedを返す()
    {
        string missingClasses = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        DetectionRequest request = new(
            DetectionMode.Object, FixturePath("object_nchw_standard_5c3.onnx"), CreateTempImage(), 0.5f, 0.5f, missingClasses);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.ClassNamesFileFailed, outcome.Status);
        Assert.Empty(outcome.Detections);
        Assert.NotNull(outcome.Message);
    }

    // ---- 事前検証(InvalidInput)の短絡 ----

    [Fact]
    public async Task RunAsync_閾値範囲外はInvalidInputを返す()
    {
        DetectionRequest request = new(
            DetectionMode.Face, FixturePath("face_nchw_standard_f5.onnx"), CreateTempImage(), 1.5f, 0.5f, null);

        DetectionOutcome outcome = await new DetectionService().RunAsync(request, CancellationToken.None);

        Assert.Equal(DetectionStatus.InvalidInput, outcome.Status);
    }
}
