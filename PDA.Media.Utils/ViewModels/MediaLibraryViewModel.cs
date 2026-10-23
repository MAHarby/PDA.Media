using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Services;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The Media Library Services window: a Search tab (<see cref="MediaSearchViewModel"/>, shown first), an Analysis tab (<see cref="MediaAnalysisViewModel"/>)
/// and a Scanner tab (<see cref="MediaScannerViewModel"/>). Closing asks first when a scan is running or there are unsaved changes, on the tab concerned.
/// </summary>
public partial class MediaLibraryViewModel : ViewModelBase
{
    public const int SearchTab = 0;
    public const int AnalysisTab = 1;
    public const int ScannerTab = 2;

    private readonly DatabaseStatusService? _databaseStatus;

    public MediaLibraryViewModel(MediaSearchViewModel search, MediaScannerViewModel scanner, DatabaseStatusService? databaseStatus = null,
        MediaAnalysisViewModel? analysis = null)
    {
        Search = search;
        Scanner = scanner;
        Analysis = analysis;
        _databaseStatus = databaseStatus;
        Search.CloseRequested += () => CloseRequested?.Invoke();
        Scanner.CloseRequested += () => CloseRequested?.Invoke();
        if (analysis != null)
        {
            Scanner.ScanFinished += _ => analysis.MarkStale();
            analysis.ShowInSearchRequested += request => _ = ShowInSearchAsync(request);
        }
    }

    public MediaSearchViewModel Search { get; }
    public MediaScannerViewModel Scanner { get; }

    /// <summary>The Analysis tab; null (and the tab hidden) without the analysis services, e.g. in older tests.</summary>
    public MediaAnalysisViewModel? Analysis { get; }

    public bool HasAnalysis => Analysis != null;

    /// <summary>Raised when the window should close after a question was answered (see <see cref="CanClose"/>).</summary>
    public event Action? CloseRequested;

    [ObservableProperty] public partial int SelectedTabIndex { get; set; } = SearchTab;

    /// <summary>"PDA-Main / Media.Master", for the heading.</summary>
    public string DatabaseText => _databaseStatus == null ? "" : $"Database: {_databaseStatus.Server} / {_databaseStatus.Database}";

    /// <summary>
    /// Called when the window is closing: true if it can close now. Otherwise the tab with the question (a running scan, unsaved changes) is shown,
    /// and <see cref="CloseRequested"/> is raised once it has been answered.
    /// </summary>
    /// <summary>Shows the Search tab and looks there for a row from the Analysis tab.</summary>
    public Task ShowInSearchAsync(AnalysisSearchRequest request)
    {
        SelectedTabIndex = SearchTab;
        return Search.ShowInSearchAsync(request);
    }

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
