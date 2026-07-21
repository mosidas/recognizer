using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Recognizer.Gui.ViewModels;
using Recognizer.Gui.Views;

namespace Recognizer.Gui;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // デスクトップライフタイム時のみメインウィンドウを割り当てる(ヘッドレスも同経路を通る)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 合成ルート: 既定コンストラクタが実 DetectionService を注入する
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
