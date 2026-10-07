using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.Services;

/// <summary>
/// Service responsible for persisting, loading, and managing FFMpegCore encoding profiles.
/// </summary>
public class EncoderProfileService
{
    public const ulong MegaByte = 1024UL * 1024UL;
    public const ulong GigaByte = MegaByte * 1024UL;

    public static readonly string DefaultProfilesDirectory = AppSettingsService.DefaultSettingsDirectory;
    public static readonly string DefaultProfilesFilePath = Path.Combine(DefaultProfilesDirectory, "profiles.json");

    private readonly string _filePath;

    public EncoderProfileService(string? customFilePath = null)
    {
        _filePath = customFilePath ?? DefaultProfilesFilePath;
    }

    /// <summary>
    /// Loads the stored encoding profiles from disk, or generates and saves defaults if the file does not exist.
    /// </summary>
    public List<EncodeProfile> LoadProfiles()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var profiles = JsonSerializer.Deserialize<List<EncodeProfile>>(json);
                    if (profiles != null && profiles.Count > 0)
                    {
                        return profiles;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading profiles from '{_filePath}': {ex.Message}");
        }

        // Return defaults and write them out for next time
        var defaults = GetDefaultProfiles();
        SaveProfiles(defaults);
        return defaults;
    }

    /// <summary>
    /// Saves the collection of encoding profiles to disk as formatted JSON.
    /// </summary>
    public void SaveProfiles(IEnumerable<EncodeProfile> profiles)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(profiles, options);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving profiles to '{_filePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves a single profile by name.
    /// </summary>
    public EncodeProfile? GetProfileByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var profiles = LoadProfiles();
        return profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds or updates a profile in storage.
    /// </summary>
    public void SaveProfile(EncodeProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var profiles = LoadProfiles();
        int existingIndex = profiles.FindIndex(p => p.Id == profile.Id || string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        SaveProfiles(profiles);
    }

    /// <summary>
    /// Deletes a profile by name or ID.
    /// </summary>
    public bool DeleteProfile(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return false;

        var profiles = LoadProfiles();
        int removedCount = profiles.RemoveAll(p => p.Id == nameOrId || string.Equals(p.Name, nameOrId, StringComparison.OrdinalIgnoreCase));

        if (removedCount > 0)
        {
            SaveProfiles(profiles);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets the profile store to factory default settings.
    /// </summary>
    public List<EncodeProfile> ResetToDefaults()
    {
        var defaults = GetDefaultProfiles();
        SaveProfiles(defaults);
        return defaults;
    }

    /// <summary>
    /// Returns the standard set of default encoding profiles for MKV media transcoding.
    /// </summary>
    public static List<EncodeProfile> GetDefaultProfiles()
    {
        return
        [
            new EncodeProfile
            {
                Name = "General",
                Description = "Balanced 10-bit HEVC x265 MKV encode with original audio preserved.",
                TargetCategory = "General",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 2 * GigaByte,
                VideoCodec = "libx265",
                Preset = "fast",
                Crf = 22,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "Source",
                AudioCodec = "copy",
                AudioBitrate = 0,
                AudioChannels = "Source",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "Bluray TV",
                Description = "High quality 10-bit HEVC x265 MKV transcode for 1080p Blu-ray TV series.",
                TargetCategory = "TV Show",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 1 * GigaByte,
                VideoCodec = "libx265",
                Preset = "fast",
                Crf = 23,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "Source",
                AudioCodec = "copy",
                AudioBitrate = 0,
                AudioChannels = "Source",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "Bluray Movie",
                Description = "Pristine 10-bit HEVC x265 MKV transcode for high-bitrate Blu-ray movies.",
                TargetCategory = "Movie",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 10 * GigaByte,
                VideoCodec = "libx265",
                Preset = "fast",
                Crf = 22,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "Source",
                AudioCodec = "copy",
                AudioBitrate = 0,
                AudioChannels = "Source",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "DVD TV",
                Description = "High quality transcode for standard-definition DVD TV shows with 576p resolution.",
                TargetCategory = "TV Show",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 500 * MegaByte,
                VideoCodec = "libx265",
                Preset = "medium",
                Crf = 20,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "720x576 (576p)",
                AudioCodec = "copy",
                AudioBitrate = 0,
                AudioChannels = "Source",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "DVD Movie",
                Description = "Clean transcode for standard-definition DVD movies with 576p resolution.",
                TargetCategory = "Movie",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 2 * GigaByte,
                VideoCodec = "libx265",
                Preset = "medium",
                Crf = 19,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "720x576 (576p)",
                AudioCodec = "copy",
                AudioBitrate = 0,
                AudioChannels = "Source",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "Web TV",
                Description = "Optimized fast encode for web-sourced TV episodes with AAC audio.",
                TargetCategory = "TV Show",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 500 * MegaByte,
                VideoCodec = "libx265",
                Preset = "fast",
                Crf = 22,
                PixelFormat = "yuv420p",
                FrameRate = "Source",
                Resolution = "Source",
                AudioCodec = "aac",
                AudioBitrate = 192,
                AudioChannels = "Stereo (2.0)",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            },
            new EncodeProfile
            {
                Name = "Web Movie",
                Description = "High efficiency MKV encode for web-sourced movies with AAC 5.1/Stereo audio.",
                TargetCategory = "Movie",
                ContainerFormat = "mkv",
                MinimumSourceFileSize = 2 * GigaByte,
                VideoCodec = "libx265",
                Preset = "fast",
                Crf = 21,
                PixelFormat = "yuv420p10le",
                FrameRate = "Source",
                Resolution = "Source",
                AudioCodec = "aac",
                AudioBitrate = 256,
                AudioChannels = "5.1 Surround",
                KeepAllStreams = true,
                CopySubtitles = true,
                IsPredefined = true
            }
        ];
    }
}
