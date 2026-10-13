using System.Text.Json.Serialization;

namespace PDA.Media.Scanner.Services;

/// <summary>Database rows a scan added, restored, updated, left unchanged and deleted (artists, albums, tracks, movies, shows and episodes together).</summary>
public class MediaScanCounts
{
    public int Added { get; set; }

    /// <summary>Soft-deleted rows found on disk again, restored with their id, favourites and notes.</summary>
    public int Restored { get; set; }

    /// <summary>Rows whose folder, file name, cover art or track count changed.</summary>
    public int Updated { get; set; }

    public int Unchanged { get; set; }

    /// <summary>Rows no longer on disk, soft-deleted.</summary>
    public int Deleted { get; set; }

    /// <summary>Adds another result's row counts to this one.</summary>
    internal void AddRows(MediaScanCounts other)
    {
        Added += other.Added;
        Restored += other.Restored;
        Updated += other.Updated;
        Unchanged += other.Unchanged;
        Deleted += other.Deleted;
    }

    /// <summary>"12 added, 0 restored, 3 updated, 1 deleted".</summary>
    [JsonIgnore]
    public string ChangesText => $"{Added} added, {Restored} restored, {Updated} updated, {Deleted} deleted";
}

/// <summary>What a scan did: the row counts over every category, and a result per category.</summary>
public sealed class MediaScanResult : MediaScanCounts
{
    /// <summary>True for a dry run: the counts are what the scan would have done, and nothing was saved.</summary>
    public bool IsDryRun { get; set; }

    /// <summary>Categories scanned completely (including those whose deletions were held back).</summary>
    public int CategoriesScanned { get; set; }

    /// <summary>Categories skipped: unknown media class, no root folder, root folder unreachable, or an error part way (nothing was deleted for them).</summary>
    public int CategoriesSkipped { get; set; }

    /// <summary>Each category asked for, in the order scanned.</summary>
    public List<MediaCategoryScanResult> Categories { get; } = [];

    public override string ToString() =>
        $"{(IsDryRun ? "Dry run: " : "")}{CategoriesScanned} categories scanned ({CategoriesSkipped} skipped): {Added} added, {Restored} restored, {Updated} updated, "
        + $"{Unchanged} unchanged, {Deleted} deleted";
}

/// <summary>How a category's scan ended.</summary>
public enum MediaCategoryScanStatus
{
    /// <summary>Scanned and saved.</summary>
    Scanned,

    /// <summary>Scanned and saved, except the deletions: they were over the limit set in <see cref="MediaScanOptions.MaxDeletePercent"/>.</summary>
    DeletionsHeld,

    /// <summary>Not scanned: unknown media class, no root folder, or a root folder that can't be reached. Nothing was changed.</summary>
    Skipped,

    /// <summary>Stopped by an error (an unreadable folder or a database error). What was saved before it stays; nothing was deleted.</summary>
    Failed,
}

/// <summary>What the scan of one media category did. Also stored as the category's last scan (see <see cref="MediaScanner.GetCategoriesAsync"/>).</summary>
public sealed class MediaCategoryScanResult : MediaScanCounts
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public MediaCategoryScanStatus Status { get; set; }

    /// <summary>Why the category was skipped, failed or had its deletions held; null when it was scanned.</summary>
    public string? Message { get; set; }

    /// <summary>Rows that would have been deleted, when <see cref="Status"/> is <see cref="MediaCategoryScanStatus.DeletionsHeld"/>.</summary>
    public int DeletionsHeld { get; set; }

    public DateTime FinishedOn { get; set; }

    /// <summary>"Scanned: 12 added, 0 restored, 3 updated, 1 deleted", or the status and its reason.</summary>
    [JsonIgnore]
    public string SummaryText => Status switch
    {
        MediaCategoryScanStatus.Scanned => $"Scanned: {ChangesText}",
        MediaCategoryScanStatus.DeletionsHeld => $"Deletions held: {ChangesText}; {DeletionsHeld} not deleted",
        MediaCategoryScanStatus.Skipped => $"Skipped: {Message}",
        _ => $"Failed: {Message}",
    };
}
