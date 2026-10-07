using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FFMpegCore.Enums;

namespace PDA.Media.Utils.Models;

/// <summary>
/// Represents an encoding profile with FFMpegCore transcoding settings tailored for MKV movies and TV shows.
/// </summary>
public partial class EncodeProfile : ObservableObject
{
    [ObservableProperty] private string id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string targetCategory = "General"; // "General", "Movie", "TV Show"
    [ObservableProperty] private string containerFormat = "mkv";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinimumSourceFileSizeFormatted))]
    private ulong minimumSourceFileSize; // Minimum source file size in bytes to process
    
    // Video settings
    [ObservableProperty] private string videoCodec = "libx265"; // libx265, libx264, copy, libvpx-vp9, libaom-av1, hevc_nvenc, h264_nvenc
    [ObservableProperty] private string preset = "fast"; // ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow
    [ObservableProperty] private int crf = 22; // Constant Rate Factor (0 - 51)
    [ObservableProperty] private string pixelFormat = "yuv420p10le"; // yuv420p10le (10-bit), yuv420p (8-bit), yuv422p10le, yuv444p10le, nv12
    [ObservableProperty] private int? videoBitrate = null; // optional bitrate in kbps (if not using CRF)
    [ObservableProperty] private string frameRate = "Source"; // Source, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60
    [ObservableProperty] private string resolution = "Source"; // Source, 3840x2160, 1920x1080, 1280x720, 720x576, 720x480
    [ObservableProperty] private string hardwareAcceleration = "None"; // None, cuda, nvenc, qsv, vaapi, dxva2

    // Audio settings
    [ObservableProperty] private string audioCodec = "copy"; // copy, aac, ac3, eac3, libmp3lame, libopus, flac
    [ObservableProperty] private int audioBitrate = 0; // 0 for copy or bitrate in kbps e.g. 192, 256, 320, 640
    [ObservableProperty] private string audioChannels = "Source"; // Source, Mono (1.0), Stereo (2.0), 5.1 Surround, 7.1 Surround
    [ObservableProperty] private string audioSampleRate = "Source"; // Source, 44100, 48000, 96000

    // Stream & Container options
    [ObservableProperty] private bool keepAllStreams = true; // -map 0
    [ObservableProperty] private bool copySubtitles = true; // -c:s copy
    [ObservableProperty] private string customArguments = string.Empty;
    [ObservableProperty] private bool isPredefined = false;

    public EncodeProfile()
    {
    }

    public EncodeProfile(string name, string description, string targetCategory = "General")
    {
        Name = name;
        Description = description;
        TargetCategory = targetCategory;
    }

    /// <summary>
    /// Creates a deep clone of this profile.
    /// </summary>
    public EncodeProfile Clone()
    {
        return new EncodeProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name,
            Description = Description,
            TargetCategory = TargetCategory,
            ContainerFormat = ContainerFormat,
            MinimumSourceFileSize = MinimumSourceFileSize,
            VideoCodec = VideoCodec,
            Preset = Preset,
            Crf = Crf,
            PixelFormat = PixelFormat,
            VideoBitrate = VideoBitrate,
            FrameRate = FrameRate,
            Resolution = Resolution,
            HardwareAcceleration = HardwareAcceleration,
            AudioCodec = AudioCodec,
            AudioBitrate = AudioBitrate,
            AudioChannels = AudioChannels,
            AudioSampleRate = AudioSampleRate,
            KeepAllStreams = KeepAllStreams,
            CopySubtitles = CopySubtitles,
            CustomArguments = CustomArguments,
            IsPredefined = IsPredefined
        };
    }

    /// <summary>
    /// Copies all values from the specified source profile into this instance.
    /// </summary>
    public void CopyFrom(EncodeProfile source)
    {
        if (source == null) return;
        Name = source.Name;
        Description = source.Description;
        TargetCategory = source.TargetCategory;
        ContainerFormat = source.ContainerFormat;
        MinimumSourceFileSize = source.MinimumSourceFileSize;
        VideoCodec = source.VideoCodec;
        Preset = source.Preset;
        Crf = source.Crf;
        PixelFormat = source.PixelFormat;
        VideoBitrate = source.VideoBitrate;
        FrameRate = source.FrameRate;
        Resolution = source.Resolution;
        HardwareAcceleration = source.HardwareAcceleration;
        AudioCodec = source.AudioCodec;
        AudioBitrate = source.AudioBitrate;
        AudioChannels = source.AudioChannels;
        AudioSampleRate = source.AudioSampleRate;
        KeepAllStreams = source.KeepAllStreams;
        CopySubtitles = source.CopySubtitles;
        CustomArguments = source.CustomArguments;
        IsPredefined = source.IsPredefined;
    }

    /// <summary>
    /// Builds the equivalent FFMpeg command-line argument string for preview and encoding.
    /// </summary>
    [JsonIgnore]
    public string GeneratedFFMpegArguments
    {
        get
        {
            var args = new System.Collections.Generic.List<string>();

            if (HardwareAcceleration != "None" && !string.IsNullOrWhiteSpace(HardwareAcceleration))
            {
                args.Add($"-hwaccel {HardwareAcceleration.ToLowerInvariant()}");
            }

            if (KeepAllStreams)
            {
                args.Add("-map 0");
            }

            // Video codec and options
            if (string.Equals(VideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-c:v copy");
            }
            else
            {
                args.Add($"-c:v:0 {VideoCodec}");
                if (!string.IsNullOrWhiteSpace(Preset))
                {
                    args.Add($"-preset {Preset.ToLowerInvariant()}");
                }

                if (VideoBitrate.HasValue && VideoBitrate.Value > 0)
                {
                    args.Add($"-b:v {VideoBitrate.Value}k");
                }
                else
                {
                    args.Add($"-crf {Crf}");
                }

                if (!string.IsNullOrWhiteSpace(PixelFormat) && PixelFormat != "Auto")
                {
                    args.Add($"-pix_fmt {PixelFormat}");
                }

                if (Resolution != "Source" && !string.IsNullOrWhiteSpace(Resolution))
                {
                    string scaleRes = Resolution.Contains(' ') ? Resolution.Split(' ')[0] : Resolution;
                    args.Add($"-vf scale={scaleRes}");
                }

                if (FrameRate != "Source" && !string.IsNullOrWhiteSpace(FrameRate))
                {
                    args.Add($"-r {FrameRate}");
                }
            }

            // Audio codec and options
            if (string.Equals(AudioCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-c:a copy");
            }
            else
            {
                args.Add($"-c:a {AudioCodec}");
                if (AudioBitrate > 0)
                {
                    args.Add($"-b:a {AudioBitrate}k");
                }

                if (AudioChannels != "Source" && !string.IsNullOrWhiteSpace(AudioChannels))
                {
                    if (AudioChannels.Contains("1.0") || AudioChannels.Equals("Mono", StringComparison.OrdinalIgnoreCase))
                        args.Add("-ac 1");
                    else if (AudioChannels.Contains("2.0") || AudioChannels.Equals("Stereo", StringComparison.OrdinalIgnoreCase))
                        args.Add("-ac 2");
                    else if (AudioChannels.Contains("5.1"))
                        args.Add("-ac 6");
                    else if (AudioChannels.Contains("7.1"))
                        args.Add("-ac 8");
                }

                if (AudioSampleRate != "Source" && !string.IsNullOrWhiteSpace(AudioSampleRate))
                {
                    string rate = AudioSampleRate.Replace("Hz", "").Trim();
                    args.Add($"-ar {rate}");
                }
            }

            // Subtitle handling
            if (CopySubtitles)
            {
                args.Add("-c:s copy");
            }

            if (!string.IsNullOrWhiteSpace(CustomArguments))
            {
                args.Add(CustomArguments.Trim());
            }

            return string.Join(" ", args);
        }
    }

    /// <summary>
    /// Gets a human-readable display string representing the minimum source file size (e.g. 10 GB, 500 MB).
    /// </summary>
    [JsonIgnore]
    public string MinimumSourceFileSizeFormatted => FormatFileSize(MinimumSourceFileSize);

    public static string FormatFileSize(ulong bytes)
    {
        if (bytes == 0) return "0 B";
        if (bytes >= 1024UL * 1024 * 1024)
        {
            double gb = (double)bytes / (1024UL * 1024 * 1024);
            return $"{gb:0.##} GB";
        }
        if (bytes >= 1024UL * 1024)
        {
            double mb = (double)bytes / (1024UL * 1024);
            return $"{mb:0.##} MB";
        }
        if (bytes >= 1024UL)
        {
            double kb = (double)bytes / 1024UL;
            return $"{kb:0.##} KB";
        }
        return $"{bytes} B";
    }
}
