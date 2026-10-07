using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LoadColourPalette_OnClick(object? sender, RoutedEventArgs e)
    {
        ColoursView colors = new ColoursView() { DataContext = new ColoursViewModel() };
        colors.Show();
    }

    private void ManageProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            var vm = new EncoderProfilesViewModel(mainVm.EncoderProfileService, mainVm.SelectedEncoderProfile);
            var profilesWindow = new EncoderProfilesView { DataContext = vm };
            profilesWindow.Closed += (s, args) =>
            {
                mainVm.RefreshProfiles();
            };
            profilesWindow.Show();
        }
    }

    private async void SelectSourceFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (!storageProvider.CanPickFolder) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select Source Folder",
            AllowMultiple = false
        };

        if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.SourcePath) && Directory.Exists(vm.SourcePath))
        {
            var startFolder = await storageProvider.TryGetFolderFromPathAsync(vm.SourcePath);
            if (startFolder != null)
            {
                options.SuggestedStartLocation = startFolder;
            }
        }

        var result = await storageProvider.OpenFolderPickerAsync(options);
        if (result.Count > 0)
        {
            var selectedFolder = result[0];
            string? selectedPath = selectedFolder.TryGetLocalPath() ?? (selectedFolder.Path.IsFile ? selectedFolder.Path.LocalPath : null);
            if (!string.IsNullOrEmpty(selectedPath) && DataContext is MainViewModel mainVm)
            {
                if (!string.Equals(mainVm.SourcePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    mainVm.SourcePath = selectedPath;
                }
            }
        }
    }

    private void WindowCloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
    private void WindowMinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    private void WindowMaximizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void SelectDestinationFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (!storageProvider.CanPickFolder) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select Destination Folder",
            AllowMultiple = false
        };

        if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.DestinationPath) && Directory.Exists(vm.DestinationPath))
        {
            var startFolder = await storageProvider.TryGetFolderFromPathAsync(vm.DestinationPath);
            if (startFolder != null)
            {
                options.SuggestedStartLocation = startFolder;
            }
        }

        var result = await storageProvider.OpenFolderPickerAsync(options);
        if (result.Count > 0)
        {
            var selectedFolder = result[0];
            string? selectedPath = selectedFolder.TryGetLocalPath() ?? (selectedFolder.Path.IsFile ? selectedFolder.Path.LocalPath : null);
            if (!string.IsNullOrEmpty(selectedPath) && DataContext is MainViewModel mainVm)
            {
                if (!string.Equals(mainVm.DestinationPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    mainVm.DestinationPath = selectedPath;
                }
            }
        }
    }
}