using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Recognizer.Gui;
using Recognizer.Gui.Views;

// ヘッドレス実行のアプリ構成を宣言する。画面表示なしで UI スレッド上のテストを可能にする(要件 9.2)
[assembly: AvaloniaTestApplication(typeof(Recognizer.Gui.Tests.TestAppBuilder))]

namespace Recognizer.Gui.Tests;

internal static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class SmokeTest
{
    [AvaloniaFact]
    public void メインウィンドウをヘッドレスで生成できる()
    {
        var window = new MainWindow();

        Assert.NotNull(window);
    }
}
