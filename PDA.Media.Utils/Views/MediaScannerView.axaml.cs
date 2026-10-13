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

public partial class MediaScannerView : Window
{
    private MediaScannerViewModel? _viewModel;
    private bool _canClose;

    public MediaScannerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel != null)
        {
            _viewModel.ActivityEntries.CollectionChanged -= ActivityEntries_CollectionChanged;
            _viewModel.CloseRequested -= ViewModel_CloseRequested;
        }
        _viewModel = DataContext as MediaScannerViewModel;
        if (_viewModel != null)
        {
            _viewModel.ActivityEntries.CollectionChanged += ActivityEntries_CollectionChanged;
            _viewModel.CloseRequested += ViewModel_CloseRequested;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _viewModel?.LoadCategoriesCommand.Execute(null);
    }

    // During a scan the view model asks first; it raises CloseRequested once the scan has stopped.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_canClose && _viewModel != null && !_viewModel.CanClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void ViewModel_CloseRequested()
    {
        _canClose = true;
        Close();
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
        var downloads = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
        string folder = downloads?.TryGetLocalPath() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
