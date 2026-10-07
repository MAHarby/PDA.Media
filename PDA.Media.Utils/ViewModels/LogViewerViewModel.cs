using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// Read-only view of the current run's log file.
/// </summary>
public partial class LogViewerViewModel : ViewModelBase
{
    private readonly LogFileService _logFileService;
    private readonly ILogger<LogViewerViewModel> _logger;

    public string LogFilePath => _logFileService.LogFilePath;

    [ObservableProperty] public partial string LogText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

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
            LogText = _logFileService.ReadLog();
            int lineCount = LogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            StatusText = $"{lineCount} lines, loaded at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read log file {LogFilePath}", LogFilePath);
            StatusText = $"Could not read the log file: {ex.Message}";
        }
    }
}
