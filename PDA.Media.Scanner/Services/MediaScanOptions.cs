namespace PDA.Media.Scanner.Services;

/// <summary>How <see cref="MediaScanner.ScanMediaCategoriesAsync"/> runs.</summary>
public sealed record MediaScanOptions
{
    /// <summary>
    /// Empty the media cache before scanning (dbo.ClearMediaCache: this also loses favourites, notes and external ids).
    /// The cache is shared by every category, so this is only allowed when every active, scannable category is being scanned. Ignored for a dry run.
    /// </summary>
    public bool ClearData { get; init; }

    /// <summary>Read the folders and compare them with the database, reporting what would change, without saving anything.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// If a category would soft-delete more than this percentage of its rows, its deletions are held back (the rest of the scan is saved) and reported,
    /// e.g. when a NAS share is half there or a folder was renamed. Null for no limit.
    /// </summary>
    public int? MaxDeletePercent { get; init; }
}
