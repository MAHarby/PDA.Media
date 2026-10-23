namespace PDA.Media.Data.Services;

/// <summary>Which media files a <see cref="MediaAnalysisScope"/> covers: tracks, movies or TV episodes.</summary>
public enum MediaAnalysisKind
{
    Music,
    Movies,
    TV,
}

/// <summary>
/// A node of the Analysis tree, as the media files under it: every track, movie or episode under <see cref="Root"/> (a category's root folder), narrowed by
/// the most specific of the optional fields. Music: <see cref="ArtistId"/>, then <see cref="AlbumId"/>; movies: <see cref="SourceFolder"/>, then <see cref="CollectionFolder"/>; TV:
/// <see cref="ShowId"/>, then <see cref="SeasonNo"/>; any kind: <see cref="ItemId"/> (one file's row).
/// </summary>
public sealed record MediaAnalysisScope(MediaAnalysisKind Kind, string Root)
{
    public int? ArtistId { get; init; }
    public int? AlbumId { get; init; }

    /// <summary>A movie source folder (e.g. "Compressed Blu-Rays") under the root.</summary>
    public string? SourceFolder { get; init; }

    /// <summary>A collection folder in <see cref="SourceFolder"/> (several films in one folder, e.g. "Star Trek"): the movies straight in it.</summary>
    public string? CollectionFolder { get; init; }

    public int? ShowId { get; init; }

    /// <summary>A season of <see cref="ShowId"/> (0: specials).</summary>
    public int? SeasonNo { get; init; }

    /// <summary>One track, movie or episode.</summary>
    public int? ItemId { get; init; }

    /// <summary>Soft-deleted rows are counted too.</summary>
    public bool IncludeDeleted { get; init; }

    public MediaAnalysisLevel Level => ItemId != null ? MediaAnalysisLevel.Item
        : Kind switch
        {
            MediaAnalysisKind.Music => AlbumId != null ? MediaAnalysisLevel.Album : ArtistId != null ? MediaAnalysisLevel.Artist : MediaAnalysisLevel.Root,
            MediaAnalysisKind.Movies => CollectionFolder != null ? MediaAnalysisLevel.Collection
                : SourceFolder != null ? MediaAnalysisLevel.Source : MediaAnalysisLevel.Root,
            _ => SeasonNo != null ? MediaAnalysisLevel.Season : ShowId != null ? MediaAnalysisLevel.Show : MediaAnalysisLevel.Root,
        };
}

/// <summary>What a <see cref="MediaAnalysisScope"/> is: a category root, an artist, ..., or one file.</summary>
public enum MediaAnalysisLevel
{
    Root,
    Artist,
    Album,
    Source,
    Collection,
    Show,
    Season,
    Item,
}

/// <summary>Totals for the media files of a scope, as stored by the last scan.</summary>
/// <param name="Items">Media files (tracks, movies, episodes).</param>
/// <param name="Folders">Distinct folders holding them.</param>
/// <param name="Size">Their total size in bytes (of the files whose size is known).</param>
/// <param name="WithoutSize">Files whose size isn't known yet (not scanned since the size was added).</param>
/// <param name="Oldest">The earliest last-modified time.</param>
/// <param name="Newest">The latest last-modified time.</param>
public sealed record MediaStats(int Items, int Folders, long Size, int WithoutSize, DateTime? Oldest, DateTime? Newest)
{
    public static readonly MediaStats Empty = new(0, 0, 0, 0, null, null);

    /// <summary>The average size of the files whose size is known.</summary>
    public long AverageSize => Items - WithoutSize > 0 ? Size / (Items - WithoutSize) : 0;
}

/// <summary>A child of a node in the Analysis tree, with its own totals.</summary>
/// <param name="Scope">The child as a scope (open it with <see cref="MediaAnalysisService.GetChildren"/>, unless it is a leaf).</param>
/// <param name="Name">Its display name: "Queen", "01 - Bohemian Rhapsody", "Season 1", "s01e02 - Title".</param>
/// <param name="Folder">Its folder on disk (for a file: the folder holding it), if known.</param>
/// <param name="FileName">For a file: its file name.</param>
/// <param name="IsDeleted">The row (artist, album, show or file) is soft-deleted.</param>
public sealed record MediaAnalysisNode(MediaAnalysisScope Scope, string Name, string? Folder, string? FileName, MediaStats Stats, bool IsDeleted)
{
    /// <summary>The row's own name, without a track or episode number; what to search for.</summary>
    public string Title { get; init; } = Name;

    /// <summary>A single file: nothing under it.</summary>
    public bool IsLeaf => Scope.Level == MediaAnalysisLevel.Item;
}

/// <summary>A category root's totals, for the summary strip.</summary>
/// <param name="Groups">Artists (music), source folders (movies) or shows (TV) with files.</param>
/// <param name="SubGroups">Albums (music) or seasons (TV) with files; 0 for movies.</param>
public sealed record MediaCategorySummary(MediaAnalysisScope Scope, MediaStats Stats, int Groups, int SubGroups, int Deleted);

/// <summary>What <see cref="MediaAnalysisService.GetLargest"/> ranks: single files, or whole albums / shows.</summary>
public enum MediaLargestBy
{
    Files,

    /// <summary>Albums (music) or shows (TV); movies are ranked by file.</summary>
    Groups,
}

/// <summary>A media file, album or show in a list (largest, a housekeeping check, an export), with where it is.</summary>
/// <param name="Scope">The row as a scope (an item, album or show), e.g. to open it on the Search tab.</param>
/// <param name="Name">Its name ("Bohemian Rhapsody", "A Night at the Opera", "Firefly (2002)").</param>
/// <param name="Parent">The artist · album, or show · season, it belongs to; null for movies, shows and artists.</param>
/// <param name="Items">The files it counts (1 for a file).</param>
/// <param name="Note">What a housekeeping check found ("missing 3, 5", "12 tracks stored, 11 found"); null otherwise.</param>
public sealed record MediaAnalysisItem(MediaAnalysisScope Scope, string Name, string? Parent, string? Folder, string? FileName, long? Size, DateTime? Date,
    int Items, bool IsDeleted, string? Note = null)
{
    /// <summary>The row's own name, without a track or episode number ("Bohemian Rhapsody"); what to search for.</summary>
    public string Title { get; init; } = Name;

    /// <summary>The file's full path, or the folder for an album or show.</summary>
    public string? Path => Folder == null ? null : FileName == null ? Folder : System.IO.Path.Combine(Folder, FileName);
}

/// <summary>The files of one extension in a scope.</summary>
/// <param name="Extension">Lower case with its dot (".mkv"), or "(none)".</param>
public sealed record MediaFileType(string Extension, int Items, long Size);

/// <summary>A housekeeping check run over a scope: how many rows it found, and the first of them.</summary>
public sealed record MediaCheck(string Title, string Description, int Count, IReadOnlyList<MediaAnalysisItem> Rows)
{
    /// <summary>The most rows a check returns (<see cref="Count"/> is still the full number).</summary>
    public const int MaxRows = 500;
}
