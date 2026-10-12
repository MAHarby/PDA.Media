namespace PDA.Media.Scanner.Services;

/// <summary>
/// What a scan did, counted in database rows (artists, albums, tracks, movies, shows and episodes together).
/// </summary>
public sealed class MediaScanResult
{
    /// <summary>Categories scanned completely.</summary>
    public int CategoriesScanned { get; set; }

    /// <summary>Categories skipped: unknown media class, no root folder, root folder unreachable, or an error part way (nothing was deleted for them).</summary>
    public int CategoriesSkipped { get; set; }

    public int Added { get; set; }

    /// <summary>Soft-deleted rows found on disk again, restored with their id, favourites and notes.</summary>
    public int Restored { get; set; }

    /// <summary>Rows whose folder, file name, cover art or track count changed.</summary>
    public int Updated { get; set; }

    public int Unchanged { get; set; }

    /// <summary>Rows no longer on disk, soft-deleted.</summary>
    public int Deleted { get; set; }

    /// <summary>Adds another result's row counts (not its category counts) to this one.</summary>
    internal void AddRows(MediaScanResult other)
    {
        Added += other.Added;
        Restored += other.Restored;
        Updated += other.Updated;
        Unchanged += other.Unchanged;
        Deleted += other.Deleted;
    }

    public override string ToString() =>
        $"{CategoriesScanned} categories scanned ({CategoriesSkipped} skipped): {Added} added, {Restored} restored, {Updated} updated, {Unchanged} unchanged, {Deleted} deleted";
}
