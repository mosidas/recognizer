using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Recognizer.Gui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
