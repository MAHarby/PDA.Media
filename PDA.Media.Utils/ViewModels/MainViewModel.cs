using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _isInitializing;

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

    public MainViewModel() : this(new AppSettingsService(), new EncoderProfileService())
    {
    }

    public MainViewModel(AppSettingsService settingsService) : this(settingsService, new EncoderProfileService())
    {
    }

    public MainViewModel(AppSettingsService settingsService, EncoderProfileService encoderProfileService)
    {
        _settingsService = settingsService;
        _encoderProfileService = encoderProfileService;
        _isInitializing = true;

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
        LoadProfilesFromService();

        if (!string.IsNullOrEmpty(previousSelection) && EncoderProfiles.Contains(previousSelection))
        {
            SelectedEncoderProfile = previousSelection;
        }
        else if (EncoderProfiles.Count > 0)
        {
            SelectedEncoderProfile = EncoderProfiles[0];
        }
        else
        {
            SelectedEncoderProfile = null;
            CurrentEncodeProfile = null;
        }

        LoadEncoderProfileSettings(SelectedEncoderProfile);
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

    partial void OnSelectedGeneralProfileChanged(string? value)
    {
        if (!_isInitializing) SaveCurrentSettings();
        LoadGeneralProfileSettings(value);
    }
    partial void OnSelectedEncoderProfileChanged(string? value)
    {
        if (!_isInitializing) SaveCurrentSettings();
        LoadEncoderProfileSettings(value);
    }
    partial void OnSourcePathChanged(string value)
    {
        if (!_isInitializing) SaveCurrentSettings();
        LoadMediaItems();
    }
    partial void OnDestinationPathChanged(string value)
    {
        if (!_isInitializing) SaveCurrentSettings();
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
        
        // Fall back to known defaults.
        SourcePath = "";
        DestinationPath = "";
        
        if (SelectedGeneralProfile == "TV Show")
        {
            SourcePath = @"\\pda-hp-z620\data\ARR-Stack\media\tv";
            DestinationPath = @"\\Aubrey-NAS\Media\TV Series\ARRstack";
            return;
        }

        if (SelectedGeneralProfile == "Movie")
        {
            SourcePath = @"\\pda-hp-z620\data\ARR-Stack\media\movies";
            DestinationPath = @"\\Aubrey-NAS\Media\Movies\ARRstack";
            return;
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
    }
    private void LoadMediaItems()
    {
        // Clear the existing media items.
        SourceMediaNodes.Clear();

        // Make sure we have a source path.
        if (!string.IsNullOrEmpty(SourcePath) && Directory.Exists(SourcePath))
        {
            // Try to use a nicer name for the root folder.
            string rootName = Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(rootName)) rootName = SourcePath;

            // Create the single top-level root node containing all child folders and files
            var rootNode = CreateDirectoryNode(SourcePath, rootName);
            SourceMediaNodes.Add(rootNode);
        }
    }
    private MediaNode CreateDirectoryNode(string folderPath, string? displayName = null)
    {
        var subNodes = new ObservableCollection<MediaNode>();
        
        // Recursively add sub-directories eg Season 1, Season 2 ...
        foreach (string subDirectory in Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly))
        {
            subNodes.Add(CreateDirectoryNode(subDirectory));
        }
        
        // Now add any media files that are in the current folder.
        foreach (string mediaFile in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly))
        {
            subNodes.Add(new MediaNode(Path.GetFileNameWithoutExtension(mediaFile), mediaFile));
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


