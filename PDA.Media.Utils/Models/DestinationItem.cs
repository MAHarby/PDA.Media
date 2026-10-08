using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.Models;

public enum EncodeStatus
{
    Queued,
    Encoding,
    Done,
    Skipped,
    Failed,
    Cancelled
}

/// <summary>
/// A source file queued in the destination list, with its Plex output name and encoding progress.
/// </summary>
public partial class DestinationItem : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool Selected { get; set; } = true;

    /// <summary>The source folder the file was queued from; the Plex show folder is worked out relative to it.</summary>
    public string SourceRoot { get; }

    /// <summary>Size of the source file in bytes.</summary>
    public long SourceSize { get; }

    /// <summary>Source size for display after the path, e.g. "- 12.5 GB" or "- 850 MB".</summary>
    public string SourceSizeText => "- " + FileSize.Format(SourceSize);

    /// <summary>Output path relative to the destination folder, e.g. <c>Show (2005)/Season 01/Show (2005) - s01e01 - Pilot.mkv</c>.</summary>
    [ObservableProperty] public partial string OutputRelativePath { get; set; } = string.Empty;

    /// <summary>Full output path, or null while no destination folder is set.</summary>
    [ObservableProperty] public partial string? OutputPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEncoding), nameof(IsDone), nameof(IsSkipped), nameof(IsFailed))]
    public partial EncodeStatus Status { get; set; } = EncodeStatus.Queued;

    /// <summary>Short status shown beside the item, e.g. "45%", "Done", "Skipped: too small".</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = "Queued";

    /// <summary>Encoding progress of this file, 0 to 100.</summary>
    [ObservableProperty] public partial double Progress { get; set; }

    public bool IsEncoding => Status == EncodeStatus.Encoding;
    public bool IsDone => Status == EncodeStatus.Done;
    public bool IsSkipped => Status is EncodeStatus.Skipped or EncodeStatus.Cancelled;
    public bool IsFailed => Status == EncodeStatus.Failed;

    public DestinationItem(string name, string fullPath, string sourceRoot = "", long sourceSize = 0)
    {
        Name = name;
        FullPath = fullPath;
        SourceRoot = sourceRoot;
        SourceSize = sourceSize;
    }

    public static string FormatSize(long bytes) => FileSize.Format(bytes);
}
