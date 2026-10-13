using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The media scanner window: pick the media categories and options, run the scan (or a dry run) and follow it in the progress bar and activity log.
/// Row-level changes go to the activity log only; the scanner logs a line per category to the app log.
/// </summary>
public partial class MediaScannerViewModel : ViewModelBase
{
    private const int DefaultMaxDeletePercent = 25;

    private readonly MediaScanner _scanner;
    private readonly AppSettingsService _settingsService;
    private readonly ILogger<MediaScannerViewModel> _logger;
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _elapsedTicker;
    private Action? _onConfirm;
    private bool _closeWhenFinished;

    public MediaScannerViewModel(MediaScanner scanner, AppSettingsService settingsService, ILogger<MediaScannerViewModel>? logger = null)
    {
        _scanner = scanner;
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<MediaScannerViewModel>.Instance;

        UserSettings settings = _settingsService.LoadSettings();
        DryRun = settings.MediaScanDryRun;
        LimitDeletions = settings.MediaScanLimitDeletions;
        MaxDeletePercent = settings.MediaScanMaxDeletePercent is > 0 and <= 100 ? settings.MediaScanMaxDeletePercent : DefaultMaxDeletePercent;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>The summary shown at the end of a scan (finished, cancelled or failed), for the main window's status bar.</summary>
    public event Action<string>? ScanFinished;

    /// <summary>Raised when the window should close: it was closed during a scan, and the scan has now stopped.</summary>
    public event Action? CloseRequested;

    // Categories.
    // ==================================================================================================

    public ObservableCollection<MediaScanCategoryItem> Categories { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; }

    /// <summary>Loads the media categories and their last scans; ticks the ones ticked last time (or the active ones the first time).</summary>
    [RelayCommand]
    private async Task LoadCategoriesAsync()
    {
        IsLoading = true;
        try
        {
            List<MediaScanCategory> categories = await Task.Run(() => _scanner.GetCategoriesAsync());
            HashSet<int>? selected = _settingsService.LoadSettings().MediaScanCategoryIds?.ToHashSet();

            foreach (MediaScanCategoryItem item in Categories) item.PropertyChanged -= Category_PropertyChanged;
            Categories.Clear();
            foreach (MediaScanCategory category in categories)
            {
                var item = new MediaScanCategoryItem(category, selected?.Contains(category.Id) ?? category.Category.IsActive);
                item.PropertyChanged += Category_PropertyChanged;
                Categories.Add(item);
            }
            SelectionChanged();
            StatusText = $"{Categories.Count} media categories";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't load the media categories");
            AddActivity(LogLevel.Error, $"Couldn't load the media categories: {ex.Message}");
            StatusText = "Couldn't load the media categories (is the database there?)";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (MediaScanCategoryItem item in Categories.Where(c => c.CanScan)) item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (MediaScanCategoryItem item in Categories) item.IsSelected = false;
    }

    private IEnumerable<MediaScanCategoryItem> SelectedCategories => Categories.Where(c => c.IsSelected && c.CanScan);

    private void Category_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaScanCategoryItem.IsSelected)) SelectionChanged();
    }

    private void SelectionChanged()
    {
        if (!CanClearData) ClearData = false;
        OnPropertyChanged(nameof(CanClearData));
        OnPropertyChanged(nameof(ClearDataToolTip));
        StartScanCommand.NotifyCanExecuteChanged();
    }

    // Options.
    // ==================================================================================================

    /// <summary>Empty the media cache first (dbo.ClearMediaCache). Only allowed with every active category ticked, and never kept for the next scan.</summary>
    [ObservableProperty] public partial bool ClearData { get; set; }

    [ObservableProperty] public partial bool DryRun { get; set; }

    [ObservableProperty] public partial bool LimitDeletions { get; set; }

    /// <summary>The deletion limit (a NumericUpDown value, so decimal).</summary>
    [ObservableProperty] public partial decimal? MaxDeletePercent { get; set; }

    /// <summary>The media cache is shared by every category, so it can only be cleared when every active one is scanned (and not in a dry run).</summary>
    public bool CanClearData => !DryRun && SelectedCategories.Any() && Categories.Where(c => c.CanScan && c.IsActive).All(c => c.IsSelected);

    public string ClearDataToolTip =>
        DryRun ? "A dry run doesn't change anything, so it can't clear the media cache"
        : !CanClearData ? "The media cache holds every category, so tick every active category to clear it"
        : "Empty the media cache before the scan saves anything: every row is added again, and favourites, notes and external ids are lost";

    partial void OnDryRunChanged(bool value) => SelectionChanged();

    // Scanning.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string ProgressText { get; set; } = "Ready";
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial double ProgressMaximum { get; set; } = 1;

    /// <summary>True while the folders are read (the number of items isn't known yet).</summary>
    [ObservableProperty] public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty] public partial string ElapsedText { get; set; } = "";

    /// <summary>What each category's scan did, in this run.</summary>
    public ObservableCollection<MediaCategoryScanResult> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    private bool CanStartScan() => !IsScanning && Confirmation == null && SelectedCategories.Any();

    /// <summary>Starts the scan, after asking first when it will clear the media cache.</summary>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private Task StartScanAsync()
    {
        if (ClearData && !DryRun)
        {
            Ask(new ScanConfirmation("Clear all media data?",
                    "The media cache is emptied for every category before the scan saves anything, and the scan then adds everything again. "
                    + "Favourites, notes and external ids are lost.", "Clear and scan"),
                () => ScanCommand.ExecuteAsync(null));
            return Task.CompletedTask;
        }
        return ScanCommand.ExecuteAsync(null);
    }

    /// <summary>Runs the scan off the UI thread; ScanCancelCommand cancels it.</summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        List<int> ids = SelectedCategories.Select(c => c.Id).ToList();
        if (ids.Count == 0 || IsScanning) return;

        SaveOptions();
        bool dryRun = DryRun;
        var options = new MediaScanOptions
        {
            ClearData = ClearData && !dryRun,
            DryRun = dryRun,
            MaxDeletePercent = LimitDeletions ? (int)(MaxDeletePercent ?? DefaultMaxDeletePercent) : null,
        };
        ClearData = false;

        IsScanning = true;
        Results.Clear();
        ProgressValue = 0;
        IsProgressIndeterminate = true;
        ProgressText = "Starting...";
        StatusText = dryRun ? "Dry run: nothing will be saved" : "Scanning...";
        StartElapsedTicker();
        var progress = new UiProgress<MediaScanProgress>(p => OnProgress(p, dryRun));
        string summary;
        try
        {
            MediaScanResult result = await Task.Run(() => _scanner.ScanMediaCategoriesAsync(ids, options, progress, cancellationToken), cancellationToken);
            summary = Summarise(result);
            ProgressValue = ProgressMaximum;
            ProgressText = "Finished";
            if (!dryRun)
            {
                foreach (MediaCategoryScanResult categoryResult in result.Categories)
                {
                    MediaScanCategoryItem? item = Categories.FirstOrDefault(c => c.Id == categoryResult.CategoryId);
                    if (item != null) item.LastScan = categoryResult;
                }
            }
        }
        catch (OperationCanceledException)
        {
            summary = "Scan cancelled: what was saved stays, and the category being saved deleted nothing";
            AddActivity(LogLevel.Warning, summary);
            _logger.LogWarning("Media scan cancelled");
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            summary = $"Scan failed: {ex.Message}";
            AddActivity(LogLevel.Error, summary);
            _logger.LogError(ex, "Media scan failed");
            ProgressText = "Failed";
        }
        finally
        {
            IsScanning = false;
            IsProgressIndeterminate = false;
            StopElapsedTicker();
        }

        StatusText = summary;
        ScanFinished?.Invoke(summary);
        if (_closeWhenFinished) CloseRequested?.Invoke();
    }

    private static string Summarise(MediaScanResult result)
    {
        int held = result.Categories.Count(c => c.Status == MediaCategoryScanStatus.DeletionsHeld);
        string text = result.IsDryRun
            ? $"Dry run finished (nothing saved): {result.Added} to add, {result.Restored} to restore, {result.Updated} to update, {result.Deleted} to delete"
            : $"Scan finished: {result.ChangesText}";
        if (held > 0) text += $"; deletions held back in {held} {(held == 1 ? "category" : "categories")}";
        if (result.CategoriesSkipped > 0) text += $"; {result.CategoriesSkipped} skipped";
        return text;
    }

    private void OnProgress(MediaScanProgress progress, bool dryRun)
    {
        if (progress.Change != null) AddActivity(LogLevel.Information, dryRun ? $"(dry run) {progress.Change}" : progress.Change.ToString(), progress.Change);
        if (progress.Message != null) AddActivity(progress.Level, progress.Message);
        if (progress.CategoryResult != null) Results.Add(progress.CategoryResult);

        // Reports are queued: ignore the state of any that arrive after the scan has finished.
        if (!IsScanning) return;
        switch (progress.Phase)
        {
            case MediaScanPhase.Reading:
                IsProgressIndeterminate = true;
                ProgressText = $"Reading {progress.CategoryName}: {progress.FoldersRead:N0} folders";
                break;
            case MediaScanPhase.Saving:
                IsProgressIndeterminate = false;
                ProgressMaximum = Math.Max(1, progress.ItemsTotal);
                ProgressValue = progress.ItemsDone;
                ProgressText = $"{(dryRun ? "Checking" : "Saving")} {progress.CategoryName}: {progress.ItemsDone:N0} / {progress.ItemsTotal:N0}";
                break;
            default:
                IsProgressIndeterminate = false;
                ProgressValue = ProgressMaximum;
                ProgressText = "Finished";
                break;
        }
    }

    /// <summary>Saves the ticked categories and the options (not ClearData) for next time, keeping the other settings.</summary>
    private void SaveOptions()
    {
        UserSettings settings = _settingsService.LoadSettings();
        settings.MediaScanCategoryIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        settings.MediaScanDryRun = DryRun;
        settings.MediaScanLimitDeletions = LimitDeletions;
        settings.MediaScanMaxDeletePercent = (int)(MaxDeletePercent ?? DefaultMaxDeletePercent);
        _settingsService.SaveSettings(settings);
    }

    private void StartElapsedTicker()
    {
        _elapsed.Restart();
        ElapsedText = FormatElapsed(TimeSpan.Zero);
        _elapsedTicker = new CancellationTokenSource();
        _ = TickElapsedAsync(_elapsedTicker.Token);
    }

    private void StopElapsedTicker()
    {
        _elapsed.Stop();
        _elapsedTicker?.Cancel();
        _elapsedTicker?.Dispose();
        _elapsedTicker = null;
        ElapsedText = FormatElapsed(_elapsed.Elapsed);
    }

    private async Task TickElapsedAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)) ElapsedText = FormatElapsed(_elapsed.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // The scan has finished.
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");

    // Activity log.
    // ==================================================================================================

    /// <summary>Messages and changed rows from the scans in this window (kept until cleared).</summary>
    public ObservableCollection<ScanActivityEntry> ActivityEntries { get; } = [];

    private void AddActivity(LogLevel level, string text, MediaScanChange? change = null) => ActivityEntries.Add(new ScanActivityEntry(DateTime.Now, level, text, change));

    [RelayCommand]
    private void ClearActivity() => ActivityEntries.Clear();

    /// <summary>Writes the activity log to a text file in <paramref name="folder"/>; returns its path.</summary>
    public string SaveActivityLog(string folder)
    {
        string path = Path.Combine(folder, $"PDA.Media scan {DateTime.Now:yyyy-MM-dd HHmmss}.txt");
        File.WriteAllLines(path, ActivityEntries.Select(e =>
            $"{e.Timestamp:yyyy-MM-dd HH:mm:ss} [{e.Level}] {e.Text}" + (e.Folder != null ? $" ({e.Folder})" : "")));
        _logger.LogInformation("Saved the media scan activity log to {Path}", path);
        StatusText = $"Saved the activity log to {path}";
        return path;
    }

    // Confirmations and closing.
    // ==================================================================================================

    /// <summary>A question shown over the window; null when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    public partial ScanConfirmation? Confirmation { get; set; }

    public bool HasConfirmation => Confirmation != null;

    private void Ask(ScanConfirmation confirmation, Action onConfirm)
    {
        _onConfirm = onConfirm;
        Confirmation = confirmation;
    }

    [RelayCommand]
    private void Confirm()
    {
        Action? onConfirm = _onConfirm;
        _onConfirm = null;
        Confirmation = null;
        onConfirm?.Invoke();
    }

    [RelayCommand]
    private void DismissConfirmation()
    {
        _onConfirm = null;
        Confirmation = null;
    }

    /// <summary>
    /// Called when the window is closing: true if it can close now. During a scan it asks first; if the user agrees, the scan is cancelled and
    /// <see cref="CloseRequested"/> is raised once it has stopped.
    /// </summary>
    public bool CanClose()
    {
        if (!IsScanning) return true;
        if (Confirmation == null)
        {
            Ask(new ScanConfirmation("Stop the scan?",
                    "A scan is running. Closing the window cancels it: what was saved so far stays, and the category being saved deletes nothing.",
                    "Cancel scan and close", "Keep scanning"),
                () =>
                {
                    _closeWhenFinished = true;
                    ScanCancelCommand.Execute(null);
                });
        }
        return false;
    }
}

/// <summary>
/// An IProgress that runs its handler on the thread that created it (the UI thread), in order; without a synchronization context (tests) it runs it at once.
/// </summary>
internal sealed class UiProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context == null) handler(value);
        else _context.Post(_ => handler(value), null);
    }
}
