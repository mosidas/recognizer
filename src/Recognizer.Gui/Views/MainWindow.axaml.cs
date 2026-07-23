using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Recognizer.Gui.Models;
using Recognizer.Gui.ViewModels;

namespace Recognizer.Gui.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private Bitmap? _previewBitmap;
    private readonly ComboBox _modeComboBox;
    private readonly Image _previewImage;
    private readonly DetectionOverlayControl _overlay;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Why: AvaloniaXamlLoader.Load を直接呼ぶ構成では x:Name の C# フィールドが
        // 自動代入されないため、名前スコープから明示的に取得する。
        _modeComboBox = this.GetControl<ComboBox>("ModeComboBox");
        _previewImage = this.GetControl<Image>("PreviewImage");
        _overlay = this.GetControl<DetectionOverlayControl>("Overlay");

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 旧 VM の購読を解除してから新 VM へ張り替える(DataContext 差し替え時の多重購読を防ぐ)。
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Detections.CollectionChanged -= OnDetectionsChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Detections.CollectionChanged += OnDetectionsChanged;

        // ComboBox の初期選択を VM のモードへ合わせる。
        _modeComboBox.SelectedIndex = _viewModel.Mode == DetectionMode.Object ? 1 : 0;
        LoadPreview(_viewModel.ImagePath);
        SyncOverlayDetections();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ImagePath))
        {
            LoadPreview(_viewModel?.ImagePath);
        }
    }

    private void OnDetectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SyncOverlayDetections();

    private void SyncOverlayDetections()
    {
        if (_viewModel is null)
        {
            return;
        }

        // コレクションの参照は不変で内容だけ変わるため、スナップショットを渡して再描画を促す。
        _overlay.Detections = _viewModel.Detections.ToList();
        _overlay.InvalidateVisual();
    }

    private void LoadPreview(string? path)
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            _previewImage.Source = null;
            _overlay.ImagePixelSize = default;
            _overlay.InvalidateVisual();
            return;
        }

        try
        {
            _previewBitmap = new Bitmap(path);
            _previewImage.Source = _previewBitmap;
            _overlay.ImagePixelSize = _previewBitmap.PixelSize;
        }
        catch (Exception)
        {
            // Why not: 画像ロード失敗でアプリを落とさない(要件 6.4)。プレビューを消すのみで、
            // 検出時のエラーメッセージ表出は検出サービス経由の結果型に委ねる。
            _previewImage.Source = null;
            _overlay.ImagePixelSize = default;
        }

        _overlay.InvalidateVisual();
    }

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Mode = _modeComboBox.SelectedIndex == 1 ? DetectionMode.Object : DetectionMode.Face;
    }

    // Why not (async void): Avalonia のイベントハンドラは Task を待てないため async void を用いる。
    // StorageProvider は非同期。未処理例外でアプリを落とさぬよう本体を try/catch で保護する(要件 6.4)。
    private async void OnBrowseModel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var file = await PickFileAsync("モデルファイルを選択", "ONNX モデル", "*.onnx");
            if (file is not null && _viewModel is not null)
            {
                _viewModel.ModelPath = file;
            }
        }
        catch (Exception)
        {
            // Why not: ファイル選択の失敗はアプリ終了に値しない。選択を破棄して継続する。
        }
    }

    private async void OnBrowseImage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var file = await PickFileAsync("入力画像を選択", "画像", "*.png", "*.jpg", "*.jpeg", "*.bmp");
            if (file is not null && _viewModel is not null)
            {
                _viewModel.SelectImage(file);
            }
        }
        catch (Exception)
        {
        }
    }

    private async void OnBrowseClassNames(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var file = await PickFileAsync("クラス名ファイルを選択", "テキスト", "*.txt", "*.names", "*");
            if (file is not null && _viewModel is not null)
            {
                _viewModel.ClassNamesPath = file;
            }
        }
        catch (Exception)
        {
        }
    }

    private async void OnRun(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.RunAsync();
        }
        catch (Exception)
        {
            // Why not: RunAsync は例外を投げない契約だが、防御的に握ってアプリ終了を防ぐ(要件 6.4)。
        }
    }

    private async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(filterName) { Patterns = patterns },
            },
        };

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
