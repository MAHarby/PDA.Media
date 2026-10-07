using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>A search hit in <see cref="LogViewerViewModel.LogText"/>.</summary>
public readonly record struct TextMatch(int Start, int Length);

/// <summary>
/// Read-only view of the current run's log file, with case-insensitive search.
/// </summary>
public partial class LogViewerViewModel : ViewModelBase
{
    private readonly LogFileService _logFileService;
    private readonly ILogger<LogViewerViewModel> _logger;
    private readonly List<int> _matchStarts = new();
    private int _currentMatchIndex = -1;

    public string LogFilePath => _logFileService.LogFilePath;

    [ObservableProperty] public partial string LogText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string MatchSummary { get; set; } = string.Empty;

    /// <summary>The match the view should select and scroll to, or null when there is none.</summary>
    [ObservableProperty] public partial TextMatch? CurrentMatch { get; set; }

    public int MatchCount => _matchStarts.Count;

    public LogViewerViewModel(LogFileService logFileService, ILogger<LogViewerViewModel> logger)
    {
        _logFileService = logFileService;
        _logger = logger;
        _logger.LogInformation("Opening log viewer for {LogFilePath}", LogFilePath);
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            // Normalise line endings so match positions line up with the TextBox on every platform.
            LogText = _logFileService.ReadLog().Replace("\r\n", "\n");
            int lineCount = LogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            StatusText = $"{lineCount} lines, loaded at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read log file {LogFilePath}", LogFilePath);
            StatusText = $"Could not read the log file: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindNext() => MoveToMatch(_currentMatchIndex + 1);

    [RelayCommand(CanExecute = nameof(HasMatches))]
    private void FindPrevious() => MoveToMatch(_currentMatchIndex - 1);

    private bool HasMatches() => _matchStarts.Count > 0;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    partial void OnSearchTextChanged(string value) => FindMatches(keepPosition: false);

    // After a refresh, stay on the same match number where possible.
    partial void OnLogTextChanged(string value) => FindMatches(keepPosition: true);

    private void FindMatches(bool keepPosition)
    {
        int previousIndex = _currentMatchIndex;
        _matchStarts.Clear();

        if (!string.IsNullOrEmpty(SearchText))
        {
            int index = LogText.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                _matchStarts.Add(index);
                index = LogText.IndexOf(SearchText, index + SearchText.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        FindNextCommand.NotifyCanExecuteChanged();
        FindPreviousCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(MatchCount));

        if (_matchStarts.Count == 0)
        {
            _currentMatchIndex = -1;
            CurrentMatch = null;
            MatchSummary = string.IsNullOrEmpty(SearchText) ? string.Empty : "No matches";
            return;
        }

        MoveToMatch(keepPosition && previousIndex >= 0 ? Math.Min(previousIndex, _matchStarts.Count - 1) : 0);
    }

    // Wraps around at either end.
    private void MoveToMatch(int index)
    {
        if (_matchStarts.Count == 0) return;

        _currentMatchIndex = (index % _matchStarts.Count + _matchStarts.Count) % _matchStarts.Count;
        CurrentMatch = new TextMatch(_matchStarts[_currentMatchIndex], SearchText.Length);
        MatchSummary = $"{_currentMatchIndex + 1} of {_matchStarts.Count}";
    }
}
