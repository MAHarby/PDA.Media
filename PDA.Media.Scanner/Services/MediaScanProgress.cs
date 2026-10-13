using Microsoft.Extensions.Logging;

namespace PDA.Media.Scanner.Services;

/// <summary>The stages of a scan, in order.</summary>
public enum MediaScanPhase
{
    /// <summary>Reading the folders of every category; the number of items to save isn't known yet.</summary>
    Reading,

    /// <summary>Saving what was read; <see cref="MediaScanProgress.ItemsDone"/> of <see cref="MediaScanProgress.ItemsTotal"/>.</summary>
    Saving,

    Finished,
}

/// <summary>What happened to one database row.</summary>
public enum MediaScanChangeKind
{
    Added,
    Restored,
    Updated,
    Deleted,
}

/// <summary>A row the scan added, restored, updated or deleted (or would have, in a dry run).</summary>
/// <param name="Entity">"artist", "album", "track", "movie", "TV show" or "episode".</param>
public sealed record MediaScanChange(MediaScanChangeKind Kind, string Entity, string Name, string? Folder)
{
    public override string ToString() => $"{Kind} {Entity} {Name}";
}

/// <summary>
/// A progress report from <see cref="MediaScanner.ScanMediaCategoriesAsync"/>: where the scan is, and at most one change or message.
/// Reports come in order, one for each folder read, each item saved and each message.
/// </summary>
public sealed record MediaScanProgress
{
    public MediaScanPhase Phase { get; init; }

    /// <summary>The category being read or saved.</summary>
    public string? CategoryName { get; init; }

    /// <summary>Folders read so far (over every category).</summary>
    public int FoldersRead { get; init; }

    /// <summary>Items (artists, albums, tracks, movies, shows, episodes found on disk) saved so far, while <see cref="Phase"/> is Saving.</summary>
    public int ItemsDone { get; init; }

    /// <summary>Items found on disk in every category, known once reading has finished.</summary>
    public int ItemsTotal { get; init; }

    /// <summary>A row that was added, restored, updated or deleted; null for a report about progress or a message.</summary>
    public MediaScanChange? Change { get; init; }

    /// <summary>A message about the scan, e.g. a category that was skipped; the same messages go to the logger.</summary>
    public string? Message { get; init; }

    public LogLevel Level { get; init; } = LogLevel.Information;

    /// <summary>Set when a category has finished (scanned, skipped or failed).</summary>
    public MediaCategoryScanResult? CategoryResult { get; init; }
}
