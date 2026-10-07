using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDA.Media.Desktop.Models;
using PDA.Media.Desktop.Services;

namespace PDA.Media.Desktop.ViewModels;

public partial class MainWindowViewModel : BaseViewModel
{
    [ObservableProperty] private string brandLabel = string.Empty; 
    [ObservableProperty] private MediaFile? selectedMediaFile;
    [ObservableProperty] private ObservableCollection<MediaFile>? mediaFiles;
    [ObservableProperty] private string? searchString = string.Empty;
    [ObservableProperty] private bool removeExtras = false;
    
    public MainWindowViewModel()
    {
        Title = "PDA.Media";
        BrandLabel = $"{BrandName} - {VersionString}";
    }
    
    [RelayCommand]
    private async Task ScrapeMediaFolders()
    {
        MediaService mediaService = new MediaService(@"\\Aubrey-NAS\Media", "Movies", true);
        var mediaFileList = await mediaService.GetRawMediaFilesAsync();

        MediaFiles = new ObservableCollection<MediaFile>(mediaFileList);
        if (MediaFiles.Count > 0) SelectedMediaFile = MediaFiles.FirstOrDefault();
    }
    
}