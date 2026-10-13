using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The Media Library Services window: a Search tab (<see cref="MediaSearchViewModel"/>, shown first) and a Scanner tab (<see cref="MediaScannerViewModel"/>).
/// Closing asks first when a scan is running or there are unsaved changes, on the tab concerned.
/// </summary>
public partial class MediaLibraryViewModel : ViewModelBase
{
    public const int SearchTab = 0;
    public const int ScannerTab = 1;

    private readonly DatabaseStatusService? _databaseStatus;

    public MediaLibraryViewModel(MediaSearchViewModel search, MediaScannerViewModel scanner, DatabaseStatusService? databaseStatus = null)
    {
        Search = search;
        Scanner = scanner;
        _databaseStatus = databaseStatus;
        Search.CloseRequested += () => CloseRequested?.Invoke();
        Scanner.CloseRequested += () => CloseRequested?.Invoke();
    }

    public MediaSearchViewModel Search { get; }
    public MediaScannerViewModel Scanner { get; }

    /// <summary>Raised when the window should close after a question was answered (see <see cref="CanClose"/>).</summary>
    public event Action? CloseRequested;

    [ObservableProperty] public partial int SelectedTabIndex { get; set; } = SearchTab;

    /// <summary>"PDA-Main / Media.Master", for the heading.</summary>
    public string DatabaseText => _databaseStatus == null ? "" : $"Database: {_databaseStatus.Server} / {_databaseStatus.Database}";

    /// <summary>
    /// Called when the window is closing: true if it can close now. Otherwise the tab with the question (a running scan, unsaved changes) is shown,
    /// and <see cref="CloseRequested"/> is raised once it has been answered.
    /// </summary>
    public bool CanClose()
    {
        if (!Scanner.CanClose())
        {
            SelectedTabIndex = ScannerTab;
            return false;
        }
        if (!Search.CanClose())
        {
            SelectedTabIndex = SearchTab;
            return false;
        }
        return true;
    }
}
