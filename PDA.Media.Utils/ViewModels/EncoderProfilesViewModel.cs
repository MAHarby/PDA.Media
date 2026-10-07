using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

public partial class EncoderProfilesViewModel : ViewModelBase
{
    private readonly EncoderProfileService _profileService;

    [ObservableProperty] private ObservableCollection<EncodeProfile> profiles = new();
    [ObservableProperty] private ObservableCollection<EncodeProfile> filteredProfiles = new();
    [ObservableProperty] private EncodeProfile? selectedProfile;
    [ObservableProperty] private EncodeProfile? editingProfile;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public event EventHandler? RequestClose;
    public event EventHandler? ProfilesChanged;

    public IReadOnlyList<string> VideoCodecs { get; } =
    [
        "libx265",
        "libx264",
        "copy",
        "libvpx-vp9",
        "libaom-av1",
        "hevc_nvenc",
        "h264_nvenc",
        "hevc_qsv",
        "h264_qsv",
        "hevc_amf",
        "h264_amf"
    ];

    public IReadOnlyList<string> Presets { get; } =
    [
        "ultrafast",
        "superfast",
        "veryfast",
        "faster",
        "fast",
        "medium",
        "slow",
        "slower",
        "veryslow"
    ];

    public IReadOnlyList<string> PixelFormats { get; } =
    [
        "yuv420p10le",
        "yuv420p",
        "yuv422p10le",
        "yuv444p10le",
        "yuv420p12le",
        "nv12"
    ];

    public IReadOnlyList<string> AudioCodecs { get; } =
    [
        "copy",
        "aac",
        "ac3",
        "eac3",
        "libmp3lame",
        "libopus",
        "flac"
    ];

    public IReadOnlyList<int> AudioBitrates { get; } =
    [
        0, 64, 96, 128, 160, 192, 224, 256, 320, 384, 448, 640
    ];

    public IReadOnlyList<string> AudioChannelsList { get; } =
    [
        "Source",
        "Mono (1.0)",
        "Stereo (2.0)",
        "5.1 Surround",
        "7.1 Surround"
    ];

    public IReadOnlyList<string> AudioSampleRates { get; } =
    [
        "Source",
        "44100 Hz",
        "48000 Hz",
        "96000 Hz"
    ];

    public IReadOnlyList<string> Resolutions { get; } =
    [
        "Source",
        "3840x2160 (4K UHD)",
        "2560x1440 (1440p)",
        "1920x1080 (1080p FHD)",
        "1280x720 (720p HD)",
        "720x576 (576p)",
        "720x480 (480p)"
    ];

    public IReadOnlyList<string> FrameRates { get; } =
    [
        "Source",
        "23.976",
        "24",
        "25",
        "29.97",
        "30",
        "50",
        "59.94",
        "60"
    ];

    public IReadOnlyList<string> TargetCategories { get; } =
    [
        "General",
        "Movie",
        "TV Show"
    ];

    public IReadOnlyList<string> ContainerFormats { get; } =
    [
        "mkv",
        "mp4",
        "webm"
    ];

    public IReadOnlyList<string> HardwareAccelerations { get; } =
    [
        "None",
        "cuda",
        "nvenc",
        "qsv",
        "vaapi",
        "dxva2",
        "d3d11va"
    ];

    public EncoderProfilesViewModel() : this(new EncoderProfileService())
    {
    }

    public EncoderProfilesViewModel(EncoderProfileService profileService, string? initialSelectedProfileName = null)
    {
        _profileService = profileService;
        LoadProfiles(initialSelectedProfileName);
    }

    public void LoadProfiles(string? selectProfileName = null)
    {
        var loaded = _profileService.LoadProfiles();
        Profiles = new ObservableCollection<EncodeProfile>(loaded);
        ApplyFilter();

        if (!string.IsNullOrEmpty(selectProfileName))
        {
            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, selectProfileName, StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault();
        }
        else
        {
            SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedProfileChanged(EncodeProfile? value)
    {
        if (value != null)
        {
            EditingProfile = value.Clone();
            StatusMessage = string.Empty;
        }
        else
        {
            EditingProfile = null;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredProfiles = new ObservableCollection<EncodeProfile>(Profiles);
        }
        else
        {
            var filtered = Profiles.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.TargetCategory.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.VideoCodec.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            );
            FilteredProfiles = new ObservableCollection<EncodeProfile>(filtered);
        }

        if (SelectedProfile != null && !FilteredProfiles.Contains(SelectedProfile))
        {
            SelectedProfile = FilteredProfiles.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        string newName = "New Profile";
        int counter = 1;
        while (Profiles.Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            newName = $"New Profile {++counter}";
        }

        var newProfile = new EncodeProfile
        {
            Name = newName,
            Description = "Custom FFMpegCore MKV transcoding profile.",
            TargetCategory = "General",
            ContainerFormat = "mkv",
            MinimumSourceFileSize = 2 * EncoderProfileService.GigaByte,
            VideoCodec = "libx265",
            Preset = "fast",
            Crf = 22,
            PixelFormat = "yuv420p10le",
            AudioCodec = "copy",
            KeepAllStreams = true,
            CopySubtitles = true,
            IsPredefined = false
        };

        Profiles.Add(newProfile);
        ApplyFilter();
        SelectedProfile = newProfile;
        SaveCurrentProfiles();
        StatusMessage = $"Added '{newProfile.Name}'.";
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile == null) return;

        string copyName = $"{SelectedProfile.Name} (Copy)";
        int counter = 1;
        while (Profiles.Any(p => string.Equals(p.Name, copyName, StringComparison.OrdinalIgnoreCase)))
        {
            copyName = $"{SelectedProfile.Name} (Copy {++counter})";
        }

        var duplicate = SelectedProfile.Clone();
        duplicate.Name = copyName;
        duplicate.IsPredefined = false;

        Profiles.Add(duplicate);
        ApplyFilter();
        SelectedProfile = duplicate;
        SaveCurrentProfiles();
        StatusMessage = $"Duplicated to '{duplicate.Name}'.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;

        var toDelete = SelectedProfile;
        int index = Profiles.IndexOf(toDelete);

        Profiles.Remove(toDelete);
        _profileService.DeleteProfile(toDelete.Id);
        ApplyFilter();

        if (Profiles.Count > 0)
        {
            int nextIndex = Math.Clamp(index, 0, Profiles.Count - 1);
            SelectedProfile = Profiles[nextIndex];
        }
        else
        {
            SelectedProfile = null;
        }

        SaveCurrentProfiles();
        StatusMessage = $"Deleted '{toDelete.Name}'.";
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile == null || EditingProfile == null) return;

        if (string.IsNullOrWhiteSpace(EditingProfile.Name))
        {
            StatusMessage = "Profile name cannot be empty.";
            return;
        }

        // Check if renamed to another existing profile name
        bool duplicateName = Profiles.Any(p => p != SelectedProfile && string.Equals(p.Name, EditingProfile.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicateName)
        {
            StatusMessage = $"A profile named '{EditingProfile.Name}' already exists.";
            return;
        }

        SelectedProfile.CopyFrom(EditingProfile);
        SaveCurrentProfiles();
        ApplyFilter();
        StatusMessage = $"Profile '{SelectedProfile.Name}' saved successfully.";
    }

    [RelayCommand]
    private void RevertChanges()
    {
        if (SelectedProfile != null)
        {
            EditingProfile = SelectedProfile.Clone();
            StatusMessage = "Reverted unsaved changes.";
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        var defaults = _profileService.ResetToDefaults();
        Profiles = new ObservableCollection<EncodeProfile>(defaults);
        ApplyFilter();
        SelectedProfile = Profiles.FirstOrDefault();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage = "Profiles reset to default presets.";
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCurrentProfiles()
    {
        _profileService.SaveProfiles(Profiles);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
}
