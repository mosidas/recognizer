using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Recognizer.Gui.Models;
using Recognizer.Gui.ViewModels;
using Recognizer.Gui.Views;

namespace Recognizer.Gui.Tests;

/// <summary>
/// レイアウト土台の最小スモーク(要件 8.1)。ピクセル描画は macOS 実機で目視確認する。
/// </summary>
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void DataContextにMainViewModelを割り当てて生成できる()
    {
        var window = new MainWindow
        {
            // 既定コンストラクタが実サービスを注入するが、生成時点では検出しないため安全。
            DataContext = new MainViewModel(),
        };

        Assert.IsType<MainViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void オーバーレイへの検出反映で例外を投げない()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };

        // 結果一覧の元コレクションを直接更新し、コードビハインドのオーバーレイ同期経路を通す。
        var exception = Record.Exception(() =>
            vm.Detections.Add(new DetectionOverlay(new System.Drawing.RectangleF(0, 0, 10, 10), 0.5f, "face #1", null)));

        Assert.Null(exception);
        Assert.NotNull(window);
    }
}
