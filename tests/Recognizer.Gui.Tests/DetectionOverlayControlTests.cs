using System.Collections.Generic;
using System.Drawing;
using Avalonia.Headless.XUnit;
using Recognizer.Gui.Models;
using Recognizer.Gui.Views;

namespace Recognizer.Gui.Tests;

/// <summary>
/// オーバーレイの座標写像(DisplayTransform 適用)を画面描画なしで検証する。
/// ピクセル描画そのものの目視は macOS 実機に委ねる。
/// </summary>
public sealed class DetectionOverlayControlTests
{
    [Fact]
    public void MapToDisplay_レターボックス時にBBoxとランドマークをスケールとオフセットで写す()
    {
        // 画像 100x100 を 200x100 に uniform フィット → scale=1, offsetX=50, offsetY=0
        var landmarks = new List<PointF> { new(10f, 20f), new(90f, 20f) };
        var face = new DetectionOverlay(new RectangleF(10f, 20f, 30f, 40f), 0.9f, "face #1", landmarks);

        var mapped = DetectionOverlayControl.MapToDisplay([face], imgW: 100f, imgH: 100f, viewW: 200f, viewH: 100f);

        var single = Assert.Single(mapped);
        Assert.Equal(new RectangleF(60f, 20f, 30f, 40f), single.box);
        Assert.NotNull(single.landmarks);
        Assert.Equal(2, single.landmarks!.Count);
        Assert.Equal(new PointF(60f, 20f), single.landmarks[0]);
        Assert.Equal(new PointF(140f, 20f), single.landmarks[1]);
    }

    [Fact]
    public void MapToDisplay_ランドマークnullは写像後もnullを保持する()
    {
        var obj = new DetectionOverlay(new RectangleF(0f, 0f, 50f, 50f), 0.5f, "dog", Landmarks: null);

        var mapped = DetectionOverlayControl.MapToDisplay([obj], imgW: 100f, imgH: 100f, viewW: 100f, viewH: 100f);

        var single = Assert.Single(mapped);
        Assert.Equal(new RectangleF(0f, 0f, 50f, 50f), single.box);
        Assert.Null(single.landmarks);
    }

    [Fact]
    public void MapToDisplay_空入力は空を返す()
    {
        var mapped = DetectionOverlayControl.MapToDisplay([], imgW: 100f, imgH: 100f, viewW: 100f, viewH: 100f);

        Assert.Empty(mapped);
    }

    [AvaloniaFact]
    public void Render_画像未設定でも例外を投げない()
    {
        var control = new DetectionOverlayControl();
        control.Measure(new Avalonia.Size(200, 200));
        control.Arrange(new Avalonia.Rect(0, 0, 200, 200));

        // 画像サイズ未設定(既定 0x0)・Detections 空でも描画がクラッシュしない契約。
        var exception = Record.Exception(() => control.RenderForTest());

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public void Render_画像ありDetectionありでも例外を投げない()
    {
        var control = new DetectionOverlayControl
        {
            ImagePixelSize = new Avalonia.PixelSize(100, 100),
            Detections = [new DetectionOverlay(new RectangleF(10f, 10f, 20f, 20f), 0.8f, "face #1", [new PointF(15f, 15f)])],
        };
        control.Measure(new Avalonia.Size(200, 200));
        control.Arrange(new Avalonia.Rect(0, 0, 200, 200));

        var exception = Record.Exception(() => control.RenderForTest());

        Assert.Null(exception);
    }
}
