using Avalonia;

namespace Recognizer.Gui;

internal static class Program
{
    // Avalonia デスクトップの標準エントリポイント。初期化前に UI フレームワークへ触れない
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // ヘッドレステストからも同一構成でアプリを組み立てられるよう public にする
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
