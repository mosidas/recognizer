using System.Drawing;
using Recognizer.Gui.Models;

namespace Recognizer.Gui.Tests;

public sealed class DetectionRequestTests
{
    private static DetectionRequest 有効な要求(
        float confidence = 0.7f,
        float nms = 0.5f,
        string modelPath = "model.onnx",
        string imagePath = "image.jpg") =>
        new(DetectionMode.Face, modelPath, imagePath, confidence, nms, ClassNamesPath: null);

    [Fact]
    public void 正常な要求は検証を通過し_null_を返す()
    {
        var result = 有効な要求().Validate();

        Assert.Null(result);
    }

    [Fact]
    public void 境界値_0_と_1_の閾値は範囲内として通過する()
    {
        Assert.Null(有効な要求(confidence: 0f, nms: 1f).Validate());
        Assert.Null(有効な要求(confidence: 1f, nms: 0f).Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void モデルパスが空なら_InvalidInput_を返す(string modelPath)
    {
        var result = 有効な要求(modelPath: modelPath).Validate();

        Assert.NotNull(result);
        Assert.Equal(DetectionStatus.InvalidInput, result!.Status);
        Assert.NotNull(result.Message);
        Assert.Empty(result.Detections);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 画像パスが空なら_InvalidInput_を返す(string imagePath)
    {
        var result = 有効な要求(imagePath: imagePath).Validate();

        Assert.NotNull(result);
        Assert.Equal(DetectionStatus.InvalidInput, result!.Status);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(2f)]
    public void 信頼度閾値が範囲外なら_InvalidInput_を返す(float confidence)
    {
        var result = 有効な要求(confidence: confidence).Validate();

        Assert.NotNull(result);
        Assert.Equal(DetectionStatus.InvalidInput, result!.Status);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(2f)]
    public void NMS閾値が範囲外なら_InvalidInput_を返す(float nms)
    {
        var result = 有効な要求(nms: nms).Validate();

        Assert.NotNull(result);
        Assert.Equal(DetectionStatus.InvalidInput, result!.Status);
        Assert.NotNull(result.Message);
    }
}

public sealed class DetectionOutcomeTests
{
    [Fact]
    public void 成功アウトカムは_Message_が_null()
    {
        var overlay = new DetectionOverlay(new RectangleF(0, 0, 1, 1), 0.9f, "face #0", null);

        var outcome = DetectionOutcome.Success([overlay], "image.jpg");

        Assert.Equal(DetectionStatus.Success, outcome.Status);
        Assert.Null(outcome.Message);
        Assert.Single(outcome.Detections);
    }

    [Fact]
    public void 失敗アウトカムは_Message_非_null_かつ検出が空()
    {
        var outcome = DetectionOutcome.Failure(DetectionStatus.ModelLoadFailed, "モデルを読み込めません。");

        Assert.Equal(DetectionStatus.ModelLoadFailed, outcome.Status);
        Assert.NotNull(outcome.Message);
        Assert.Empty(outcome.Detections);
    }

    [Fact]
    public void 成功なのに_Message_を持つ生成は不変条件違反で拒否される()
    {
        Assert.Throws<ArgumentException>(() =>
            new DetectionOutcome(DetectionStatus.Success, [], "image.jpg", "余計なメッセージ"));
    }

    [Fact]
    public void 失敗なのに_Message_が_null_の生成は不変条件違反で拒否される()
    {
        Assert.Throws<ArgumentException>(() =>
            new DetectionOutcome(DetectionStatus.InvalidInput, [], null, null));
    }
}
