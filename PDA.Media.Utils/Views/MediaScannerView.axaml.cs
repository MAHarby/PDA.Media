using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

/// <summary>The media library window's Scanner tab.</summary>
public partial class MediaScannerView : UserControl
{
    private MediaScannerViewModel? _viewModel;

    public MediaScannerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null) _viewModel.ActivityEntries.CollectionChanged -= ActivityEntries_CollectionChanged;
        _viewModel = DataContext as MediaScannerViewModel;
        if (_viewModel != null) _viewModel.ActivityEntries.CollectionChanged += ActivityEntries_CollectionChanged;
    }

    // Keep the newest activity in view, posted so the new item has been laid out first.
    private void ActivityEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                int count = _viewModel?.ActivityEntries.Count ?? 0;
                if (count > 0) ActivityListBox.ScrollIntoView(count - 1);
            }, DispatcherPriority.Background);
        }
    }

    private async void SaveActivityButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        try
        {
            _viewModel.SaveActivityLog(await GetDownloadsFolderAsync());
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Couldn't save the activity log: {ex.Message}";
        }
    }

    // Uses the system's Downloads location (which may be redirected on Windows), falling back to ~/Downloads.
    private async Task<string> GetDownloadsFolderAsync()
    {
        IStorageFolder? downloads = TopLevel.GetTopLevel(this) is { } topLevel
            ? await topLevel.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads)
            : null;
        string folder = downloads?.TryGetLocalPath() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
