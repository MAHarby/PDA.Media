using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Utils.Views;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger = NullLogger<MainWindow>.Instance;
    private readonly Func<string?, EncoderProfilesViewModel>? _profilesViewModelFactory;
    private ObservableCollection<AuditLogEntry>? _auditLogEntries;

    // Used by the XAML previewer.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel, Func<string?, EncoderProfilesViewModel> profilesViewModelFactory,
        ILogger<MainWindow> logger) : this()
    {
        _logger = logger;
        _profilesViewModelFactory = profilesViewModelFactory;
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _logger.LogInformation("Main window opened");
        Dispatcher.UIThread.Post(ScrollAuditLogToEnd, DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _logger.LogInformation("Main window closing");
        base.OnClosing(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_auditLogEntries != null)
        {
            _auditLogEntries.CollectionChanged -= AuditLogEntries_CollectionChanged;
        }

        _auditLogEntries = (DataContext as MainViewModel)?.AuditLogEntries;

        if (_auditLogEntries != null)
        {
            _auditLogEntries.CollectionChanged += AuditLogEntries_CollectionChanged;
        }
    }

    // Keep the newest audit log entry in view.
    private void AuditLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Deferred until after layout so the new item has been measured; otherwise the scroll is lost.
            Dispatcher.UIThread.Post(ScrollAuditLogToEnd, DispatcherPriority.Background);
        }
    }

    private void ScrollAuditLogToEnd()
    {
        if (_auditLogEntries is { Count: > 0 })
        {
            AuditLogListBox.ScrollIntoView(_auditLogEntries.Count - 1);
        }
    }

    private void LoadColourPalette_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.LogInformation("Opening colour palette window");
        ColoursView colors = new ColoursView() { DataContext = new ColoursViewModel() };
        colors.Show();
    }

    private void ManageProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            var vm = _profilesViewModelFactory?.Invoke(mainVm.SelectedEncoderProfile)
                     ?? new EncoderProfilesViewModel(mainVm.EncoderProfileService, mainVm.SelectedEncoderProfile);
            var profilesWindow = new EncoderProfilesView { DataContext = vm };
            profilesWindow.Closed += (s, args) =>
            {
                _logger.LogInformation("Encoding profile manager closed");
                mainVm.RefreshProfiles();
            };
            profilesWindow.Show();
        }
    }

    private async void SelectSourceFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (!storageProvider.CanPickFolder)
        {
            _logger.LogWarning("Folder picking is not supported on this platform");
            return;
        }

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

        _logger.LogInformation("Opening source folder picker");
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
                else
                {
                    _logger.LogInformation("Source folder unchanged ({SourcePath})", selectedPath);
                }
            }
            else
            {
                _logger.LogWarning("Selected source folder has no local path and was ignored");
            }
        }
        else
        {
            _logger.LogInformation("Source folder selection cancelled");
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
        if (!storageProvider.CanPickFolder)
        {
            _logger.LogWarning("Folder picking is not supported on this platform");
            return;
        }

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

        _logger.LogInformation("Opening destination folder picker");
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
                else
                {
                    _logger.LogInformation("Destination folder unchanged ({DestinationPath})", selectedPath);
                }
            }
            else
            {
                _logger.LogWarning("Selected destination folder has no local path and was ignored");
            }
        }
        else
        {
            _logger.LogInformation("Destination folder selection cancelled");
        }
    }
}