using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Recognizer.Gui.ViewModels;

/// <summary>
/// 変更通知の基底。追加パッケージ(CommunityToolkit 等)を避け、素の
/// <see cref="INotifyPropertyChanged"/> を手書きして GUI の依存境界を保つ。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>値が変わったときのみ更新し変更通知する。変わったら true。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
