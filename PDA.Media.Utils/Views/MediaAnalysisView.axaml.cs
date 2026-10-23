using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class MediaAnalysisView : UserControl
{
    public MediaAnalysisView()
    {
        InitializeComponent();
    }

    private async void ExportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaAnalysisViewModel viewModel) return;
        try
        {
            await viewModel.ExportAsync(await GetDownloadsFolderAsync());
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Couldn't export: {ex.Message}";
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
