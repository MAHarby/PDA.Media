namespace PDA.Media.Scanner.Services;

/// <summary>Which kind of media a category's root folder holds, read from its MediaClass.</summary>
public enum MediaKind
{
    Unknown,
    Music,
    Movie,
    TV,
}

public static class MediaKinds
{
    /// <summary>
    /// The kind for a MediaCategory.MediaClass, ignoring case, spaces and dashes: "Music"; "Movie", "Movies", "Film";
    /// "TV", "TV Show", "TVShows", "Television", "Series". Anything else is Unknown, and the scanner skips that category.
    /// </summary>
    public static MediaKind FromMediaClass(string? mediaClass)
    {
        string key = new string((mediaClass ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch
        {
            "music" or "audio" => MediaKind.Music,
            "movie" or "movies" or "film" or "films" => MediaKind.Movie,
            "tv" or "tvshow" or "tvshows" or "television" or "series" => MediaKind.TV,
            _ => MediaKind.Unknown,
        };
    }
}
