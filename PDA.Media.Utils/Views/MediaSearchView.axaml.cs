using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

/// <summary>The media library window's Search tab.</summary>
public partial class MediaSearchView : UserControl
{
    public MediaSearchView()
    {
        InitializeComponent();
    }

    private async void CopyPathButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaSearchViewModel { SelectedPath: { } path } vm) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(path);
            vm.StatusText = $"Copied {path}";
        }
    }
}
