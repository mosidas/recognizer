using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Recognizer.Gui.Models;
using Recognizer.Gui.Rendering;
using AvColor = Avalonia.Media.Color;
using AvPoint = Avalonia.Point;
using AvRect = Avalonia.Rect;

namespace Recognizer.Gui.Views;

/// <summary>
/// プレビュー画像の上に検出結果(BBox・信頼度ラベル・顔のランドマーク)を重ねて描く。
/// 背景の <see cref="Image"/>(Stretch=Uniform)と同一領域に重ね、同じ uniform フィット
/// (<see cref="DisplayTransform"/>)で画像ピクセル座標を表示座標へ写す。
/// </summary>
public sealed class DetectionOverlayControl : Control
{
    // 判別しやすい配色・線幅(要件 8.2)。ランドマークは BBox と別色にする。
    private static readonly IPen s_boxPen = new Pen(new SolidColorBrush(AvColor.FromRgb(0x2E, 0xCC, 0x71)), 2d);
    private static readonly IBrush s_labelBrush = new SolidColorBrush(AvColor.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly IBrush s_labelBackground = new SolidColorBrush(AvColor.FromArgb(0xB0, 0x00, 0x00, 0x00));
    private static readonly IBrush s_landmarkBrush = new SolidColorBrush(AvColor.FromRgb(0xE7, 0x4C, 0x3C));
    private const double LandmarkRadius = 2.5d;

    public static readonly StyledProperty<IReadOnlyList<DetectionOverlay>?> DetectionsProperty =
        AvaloniaProperty.Register<DetectionOverlayControl, IReadOnlyList<DetectionOverlay>?>(nameof(Detections));

    public static readonly StyledProperty<PixelSize> ImagePixelSizeProperty =
        AvaloniaProperty.Register<DetectionOverlayControl, PixelSize>(nameof(ImagePixelSize));

    /// <summary>表示する検出。null / 空なら何も描かない。</summary>
    public IReadOnlyList<DetectionOverlay>? Detections
    {
        get => GetValue(DetectionsProperty);
        set => SetValue(DetectionsProperty, value);
    }

    /// <summary>背景プレビュー画像のピクセルサイズ。0 サイズなら描画をスキップする。</summary>
    public PixelSize ImagePixelSize
    {
        get => GetValue(ImagePixelSizeProperty);
        set => SetValue(ImagePixelSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Why: StyledProperty の差し替え時に再描画を促す。コレクション内容の変化(Add/Clear)は
        // 参照が変わらず通知されないため、その追従は呼び出し側が InvalidateVisual で行う。
        if (change.Property == DetectionsProperty || change.Property == ImagePixelSizeProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!TryBuildDrawItems(out IReadOnlyList<(RectangleF box, IReadOnlyList<PointF>? landmarks)> items, out IReadOnlyList<DetectionOverlay> source))
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            (RectangleF box, IReadOnlyList<PointF>? landmarks) = items[i];
            DetectionOverlay overlay = source[i];

            var rect = new AvRect(box.X, box.Y, box.Width, box.Height);
            context.DrawRectangle(null, s_boxPen, rect);

            DrawLabel(context, overlay, rect);

            // Why: ランドマークは顔でのみ存在。null(物体・ランドマーク無し顔)は描かない(要件 2.4 / 3.1)。
            if (landmarks is null)
            {
                continue;
            }

            foreach (PointF point in landmarks)
            {
                context.DrawEllipse(s_landmarkBrush, null, new AvPoint(point.X, point.Y), LandmarkRadius, LandmarkRadius);
            }
        }
    }

    private static void DrawLabel(DrawingContext context, DetectionOverlay overlay, AvRect box)
    {
        var text = new FormattedText(
            FormatLabel(overlay),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12d,
            s_labelBrush);

        // ラベルは BBox 左上に置く。上端に収まらなければ枠内へ落として画面外を避ける。
        var y = box.Y - text.Height;
        if (y < 0d)
        {
            y = box.Y;
        }

        var origin = new AvPoint(box.X, y);
        context.FillRectangle(s_labelBackground, new AvRect(origin.X, origin.Y, text.Width, text.Height));
        context.DrawText(text, origin);
    }

    private static string FormatLabel(DetectionOverlay overlay) =>
        string.Create(CultureInfo.CurrentCulture, $"{overlay.Label} {overlay.Confidence:0.00}");

    /// <summary>
    /// 現在の表示領域(<see cref="Visual.Bounds"/>)と画像サイズから描画対象を組み立てる。
    /// 画像サイズ・表示領域が非正、または検出が無いときは false を返し描画を省く。
    /// </summary>
    private bool TryBuildDrawItems(
        out IReadOnlyList<(RectangleF box, IReadOnlyList<PointF>? landmarks)> items,
        out IReadOnlyList<DetectionOverlay> source)
    {
        items = [];
        source = [];

        IReadOnlyList<DetectionOverlay>? detections = Detections;
        if (detections is null || detections.Count == 0)
        {
            return false;
        }

        PixelSize imageSize = ImagePixelSize;
        Rect bounds = Bounds;
        if (imageSize.Width <= 0 || imageSize.Height <= 0 || bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return false;
        }

        items = MapToDisplay(detections, imageSize.Width, imageSize.Height, (float)bounds.Width, (float)bounds.Height);
        source = detections;
        return true;
    }

    /// <summary>
    /// 検出を表示座標へ写す純粋関数。<see cref="DisplayTransform"/> で BBox とランドマークを
    /// 表示領域に uniform フィットで写し、ランドマークの null は保持する(描画有無の分岐に使う)。
    /// </summary>
    internal static IReadOnlyList<(RectangleF box, IReadOnlyList<PointF>? landmarks)> MapToDisplay(
        IReadOnlyList<DetectionOverlay> detections,
        float imgW,
        float imgH,
        float viewW,
        float viewH)
    {
        if (detections.Count == 0)
        {
            return [];
        }

        DisplayTransform transform = DisplayTransform.Compute(imgW, imgH, viewW, viewH);
        var result = new List<(RectangleF box, IReadOnlyList<PointF>? landmarks)>(detections.Count);

        foreach (DetectionOverlay detection in detections)
        {
            RectangleF box = transform.Apply(detection.BBox);

            IReadOnlyList<PointF>? landmarks = null;
            if (detection.Landmarks is not null)
            {
                var mapped = new List<PointF>(detection.Landmarks.Count);
                foreach (PointF point in detection.Landmarks)
                {
                    mapped.Add(transform.Apply(point));
                }

                landmarks = mapped;
            }

            result.Add((box, landmarks));
        }

        return result;
    }

    /// <summary>
    /// ヘッドレステスト向けに、描画時と同じガード + 座標写像の計算経路を実行する。
    /// DrawingContext へのピクセル描画自体は macOS 実機で目視確認する(コンテナでは検証不可)。
    /// </summary>
    internal bool RenderForTest() =>
        TryBuildDrawItems(out _, out _);
}
