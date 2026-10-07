using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Helpers;

namespace PDA.Media.Utils.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    const long MegaByte = 1024 * 1024;
    const long GigaByte = MegaByte * 1024;
    
    private readonly AppSettingsService _settingsService;
    private readonly EncoderProfileService _encoderProfileService;
    private readonly AuditLogSink _auditLogSink;
    private readonly ILogger<MainViewModel> _logger;
    private bool _isInitializing;
    // Set while a general profile applies its paths, so settings are saved once afterwards.
    private bool _isApplyingGeneralProfile;
    // Set while profiles reload; the ComboBox briefly clears its selection when its list is replaced.
    private bool _isRefreshingProfiles;

    public EncoderProfileService EncoderProfileService => _encoderProfileService;
    public EncoderProfileService ProfileSettingsService => _encoderProfileService;

    [ObservableProperty] public partial string BrandTitle { get; set; } = "Media Utilities - Batch Encoder";
    
    // Application Settings.
    [ObservableProperty] public partial string? SelectedGeneralProfile { get; set; } = null;
    [ObservableProperty] public partial List<string> GeneralProfiles { get; set; } = ["General","TV Show","Movie"];
    [ObservableProperty] public partial string? SelectedEncoderProfile { get; set; } = null;
    [ObservableProperty] public partial List<string> EncoderProfiles { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<EncodeProfile> AvailableProfiles { get; set; } = new();
    [ObservableProperty] public partial EncodeProfile? CurrentEncodeProfile { get; set; } = null;
    
    [ObservableProperty] public partial string SourcePath { get; set; } = @"\\pda-hp-z620\data\ARR-Stack\media\tv";
    [ObservableProperty] public partial string DestinationPath { get; set; } = "";
    [ObservableProperty] public partial ObservableCollection<DestinationItem> DestinationItems { get; set; } = new();
    [ObservableProperty] public partial DestinationItem? SelectedDestinationItem { get; set; } = null;

    public ObservableCollection<MediaNode> SourceMediaNodes { get; } = new();
    public MediaNode? SelectedMediaNode = null;

    /// <summary>Live log entries shown in the Audit Log panel.</summary>
    public ObservableCollection<AuditLogEntry> AuditLogEntries => _auditLogSink.Entries;

    public MainViewModel() : this(new AppSettingsService(), new EncoderProfileService())
    {
    }

    public MainViewModel(AppSettingsService settingsService) : this(settingsService, new EncoderProfileService())
    {
    }

    public MainViewModel(AppSettingsService settingsService, EncoderProfileService encoderProfileService)
        : this(settingsService, encoderProfileService, new AuditLogSink(), NullLogger<MainViewModel>.Instance)
    {
    }

    public MainViewModel(AppSettingsService settingsService, EncoderProfileService encoderProfileService,
        AuditLogSink auditLogSink, ILogger<MainViewModel> logger)
    {
        _settingsService = settingsService;
        _encoderProfileService = encoderProfileService;
        _auditLogSink = auditLogSink;
        _logger = logger;
        _isInitializing = true;
        _logger.LogInformation("Initialising main window view model");

        // Load profiles from the encoding profile service.
        LoadProfilesFromService();

        // Load last session settings.
        var settings = _settingsService.LoadSettings();
        
        if (!string.IsNullOrEmpty(settings.GeneralProfile)) SelectedGeneralProfile = settings.GeneralProfile; 
        if (!string.IsNullOrEmpty(settings.EncoderProfile))
        {
            SelectedEncoderProfile = settings.EncoderProfile;
        }
        else if (EncoderProfiles.Count > 0)
        {
            SelectedEncoderProfile = EncoderProfiles[0];
        }

        if (!string.IsNullOrEmpty(settings.SourcePath)) SourcePath = settings.SourcePath; 
        if (!string.IsNullOrEmpty(settings.DestinationPath)) DestinationPath = settings.DestinationPath; 

        _logger.LogInformation(
            "Restored last session: General profile {GeneralProfile}, Encoder profile {EncoderProfile}, Source {SourcePath}, Destination {DestinationPath}",
            SelectedGeneralProfile ?? "(none)", SelectedEncoderProfile ?? "(none)", SourcePath, DestinationPath);
        
        _isInitializing = false;
        LoadMediaItems();
        LoadEncoderProfileSettings(SelectedEncoderProfile);
    }

    /// <summary>
    /// Loads encoding profiles and refreshes profile lists.
    /// </summary>
    public void LoadProfilesFromService()
    {
        var loadedProfiles = _encoderProfileService.LoadProfiles();
        AvailableProfiles.Clear();
        foreach (var p in loadedProfiles)
        {
            AvailableProfiles.Add(p);
        }

        EncoderProfiles = loadedProfiles.Select(p => p.Name).ToList();
    }

    /// <summary>
    /// Refreshes profiles and ensures active profile remains selected.
    /// </summary>
    public void RefreshProfiles()
    {
        string? previousSelection = SelectedEncoderProfile;
        _logger.LogInformation("Refreshing encoding profiles (current selection {EncoderProfile})", previousSelection ?? "(none)");

        _isRefreshingProfiles = true;
        try
        {
            LoadProfilesFromService();

            if (!string.IsNullOrEmpty(previousSelection) && EncoderProfiles.Contains(previousSelection))
            {
                SelectedEncoderProfile = previousSelection;
            }
            else if (EncoderProfiles.Count > 0)
            {
                SelectedEncoderProfile = EncoderProfiles[0];
                _logger.LogWarning("Encoding profile {PreviousProfile} no longer exists; selected {EncoderProfile} instead",
                    previousSelection ?? "(none)", SelectedEncoderProfile);
            }
            else
            {
                SelectedEncoderProfile = null;
                CurrentEncodeProfile = null;
                _logger.LogWarning("No encoding profiles are available");
            }
        }
        finally
        {
            _isRefreshingProfiles = false;
        }

        if (!string.Equals(SelectedEncoderProfile, previousSelection, StringComparison.Ordinal))
        {
            SaveCurrentSettings();
        }

        LoadEncoderProfileSettings(SelectedEncoderProfile);
    }

    [RelayCommand]
    private void ClearAuditLog()
    {
        _auditLogSink.Clear();
        _logger.LogInformation("Audit log panel cleared (the log file is unchanged)");
    }

    [RelayCommand]
    private void LoadDestinationItems()
    {
        DestinationItems.Clear();

        foreach (MediaNode mediaItem in SourceMediaNodes)
        {
            CollectDestinationItems(mediaItem);
        }
        
        SelectedDestinationItem = DestinationItems.FirstOrDefault();

        if (DestinationItems.Count == 0)
        {
            _logger.LogWarning("No source media files are selected; nothing sent to the destination list");
        }
        else
        {
            _logger.LogInformation("Sent {FileCount} selected media files to the destination list", DestinationItems.Count);
        }
    }
    private void CollectDestinationItems(MediaNode node)
    {
        if (node.SubNodes != null)
        {
            foreach (var childNode in node.SubNodes)
            {
                CollectDestinationItems(childNode);
            }
        }
        else if (node.Selected)
        {
            DestinationItems.Add(new DestinationItem(node.Name, node.FullPath));
        }
    }

    // During construction the handlers only record values; the constructor scans the source folder
    // and loads the encoder profile once at the end.
    partial void OnSelectedGeneralProfileChanged(string? value)
    {
        if (!_isInitializing)
        {
            _logger.LogInformation("General profile changed to {GeneralProfile}", value ?? "(none)");
        }
        LoadGeneralProfileSettings(value);
        if (!_isInitializing) SaveCurrentSettings();
    }
    partial void OnSelectedEncoderProfileChanged(string? value)
    {
        if (_isInitializing || _isRefreshingProfiles) return;

        _logger.LogInformation("Encoder profile changed to {EncoderProfile}", value ?? "(none)");
        SaveCurrentSettings();
        LoadEncoderProfileSettings(value);
    }
    partial void OnSourcePathChanged(string value)
    {
        if (_isInitializing) return;

        _logger.LogInformation("Source path changed to {SourcePath}", value);
        if (!_isApplyingGeneralProfile) SaveCurrentSettings();
        LoadMediaItems();
    }
    partial void OnDestinationPathChanged(string value)
    {
        if (_isInitializing) return;

        _logger.LogInformation("Destination path changed to {DestinationPath}", value);
        if (!_isApplyingGeneralProfile) SaveCurrentSettings();
    }

    private void SaveCurrentSettings()
    {
        _settingsService.SaveSettings(new UserSettings
        {
            GeneralProfile = SelectedGeneralProfile ?? "General",
            EncoderProfile = SelectedEncoderProfile ?? "Bluray TV",
            SourcePath = SourcePath,
            DestinationPath = DestinationPath
        });
    }
    private void LoadGeneralProfileSettings(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Fall back to known defaults. Each path is assigned once so the source folder is scanned once.
        (string sourcePath, string destinationPath) = value switch
        {
            "TV Show" => (@"\\pda-hp-z620\data\ARR-Stack\media\tv", @"\\Aubrey-NAS\Media\TV Series\ARRstack"),
            "Movie" => (@"\\pda-hp-z620\data\ARR-Stack\media\movies", @"\\Aubrey-NAS\Media\Movies\ARRstack"),
            _ => ("", "")
        };

        _isApplyingGeneralProfile = true;
        try
        {
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
        finally
        {
            _isApplyingGeneralProfile = false;
        }
    }
    private void LoadEncoderProfileSettings(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            CurrentEncodeProfile = null;
            return;
        }
        
        CurrentEncodeProfile = AvailableProfiles.FirstOrDefault(p => string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase))
                               ?? _encoderProfileService.GetProfileByName(value);

        if (CurrentEncodeProfile == null)
        {
            _logger.LogWarning("Encoding profile {EncoderProfile} could not be found", value);
        }
        else
        {
            _logger.LogInformation(
                "Active encoding profile {EncoderProfile}: video {VideoCodec} (preset {Preset}, CRF {Crf}), audio {AudioCodec}",
                CurrentEncodeProfile.Name, CurrentEncodeProfile.VideoCodec, CurrentEncodeProfile.Preset,
                CurrentEncodeProfile.Crf, CurrentEncodeProfile.AudioCodec);
        }
    }
    private void LoadMediaItems()
    {
        // Clear the existing media items.
        SourceMediaNodes.Clear();

        // Make sure we have a source path.
        if (string.IsNullOrEmpty(SourcePath))
        {
            _logger.LogInformation("No source path set; source media list cleared");
            return;
        }

        if (!Directory.Exists(SourcePath))
        {
            _logger.LogWarning("Source folder {SourcePath} does not exist or is not reachable", SourcePath);
            return;
        }

        _logger.LogInformation("Scanning source folder {SourcePath}", SourcePath);
        var stopwatch = Stopwatch.StartNew();
        int folderCount = 0, fileCount = 0;

        try
        {
            // Try to use a nicer name for the root folder.
            string rootName = Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(rootName)) rootName = SourcePath;

            // Create the single top-level root node containing all child folders and files
            var rootNode = CreateDirectoryNode(SourcePath, ref folderCount, ref fileCount, rootName);
            SourceMediaNodes.Add(rootNode);

            _logger.LogInformation("Found {FolderCount} folders and {FileCount} files in {SourcePath} ({ElapsedMs} ms)",
                folderCount, fileCount, SourcePath, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to scan source folder {SourcePath}", SourcePath);
        }
    }
    private MediaNode CreateDirectoryNode(string folderPath, ref int folderCount, ref int fileCount, string? displayName = null)
    {
        var subNodes = new ObservableCollection<MediaNode>();
        folderCount++;
        
        // Recursively add sub-directories eg Season 1, Season 2 ...
        foreach (string subDirectory in Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            subNodes.Add(CreateDirectoryNode(subDirectory, ref folderCount, ref fileCount));
        }
        
        // Now add any media files that are in the current folder.
        foreach (string mediaFile in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly))
        {
            subNodes.Add(new MediaNode(Path.GetFileNameWithoutExtension(mediaFile), mediaFile));
            fileCount++;
        }

        string? folderName = displayName ?? Path.GetFileName(folderPath);
        return new MediaNode(folderName, folderPath, subNodes);
    }
}

public partial class MediaNode : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<MediaNode>? SubNodes { get; }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                // Cascade selection to child nodes
                if (SubNodes != null)
                {
                    foreach (var childNode in SubNodes)
                    {
                        childNode.Selected = value;
                    }
                }
            }
        }
    }
    
    public MediaNode(string name, string fullPath) { Name = name; FullPath = fullPath; }
    public MediaNode(string name, string fullPath, ObservableCollection<MediaNode>? subNodes) { Name = name; FullPath = fullPath; SubNodes = subNodes; }
}
public partial class DestinationItem : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool Selected { get; set; } = true;
    
    public DestinationItem(string name, string fullPath) { Name = name; FullPath = fullPath; }
}


