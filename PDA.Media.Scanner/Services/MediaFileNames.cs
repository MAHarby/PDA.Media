using System.Text.RegularExpressions;

namespace PDA.Media.Scanner.Services;

/// <summary>
/// Reads media details from file and folder names (pure: no disk access), following the layouts the scanner expects:
/// music <c>Artist/Album/NN - Title.ext</c>, movies <c>Movie (Year)/Movie (Year).ext</c> and TV <c>Show (Year)/Season NN/Show (Year) - sNNeNN - Title.ext</c>.
/// </summary>
public static partial class MediaFileNames
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".alac", ".ape", ".aiff", ".aif", ".dsf",
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm",
    };

    /// <summary>Cover art file names, in order of preference (each also as .png).</summary>
    private static readonly string[] CoverArtNames = ["cover", "folder", "front", "poster", "albumart", "show", "movie"];

    public static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Folders the scan skips: hidden ones (".something") and NAS housekeeping folders such as Synology's "@eaDir" and "#recycle".
    /// </summary>
    public static bool IsIgnoredFolder(string path)
    {
        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Length == 0 || name[0] is '.' or '@' or '#' || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The cover art file among <paramref name="fileNames"/> (names only), e.g. "cover.jpg", or null.</summary>
    public static string? FindCoverArt(IEnumerable<string> fileNames)
    {
        var names = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        foreach (string name in CoverArtNames)
        {
            foreach (string extension in new[] { ".jpg", ".jpeg", ".png" })
            {
                if (names.TryGetValue(name + extension, out string? actual)) return actual;
            }
        }
        return null;
    }

    /// <summary>
    /// A track's number and title from its file name: "01 - Title.flac", "01. Title.mp3" and "01 Title.m4a" give track 1.
    /// A disc number in front ("2-03 - Title") gives disc x 100 + track (203), so a multi-disc album stays in order.
    /// Without a number the track is 0 and the title is the whole name.
    /// </summary>
    public static (int TrackNo, string Title) ParseTrackFileName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Trim();
        Match match = TrackRegex().Match(name);
        if (!match.Success) return (0, name);

        int track = int.Parse(match.Groups["track"].Value);
        if (match.Groups["disc"].Success) track += int.Parse(match.Groups["disc"].Value) * 100;
        return (track, match.Groups["title"].Value.Trim());
    }

    /// <summary>A Plex folder name split into name and year: "The Office (US) (2005)" gives ("The Office (US)", 2005); no year gives 0.</summary>
    public static (string Name, int Year) ParseNameAndYear(string folderName)
    {
        string name = folderName.Trim();
        Match match = NameYearRegex().Match(name);
        return match.Success ? (match.Groups["name"].Value.Trim(), int.Parse(match.Groups["year"].Value)) : (name, 0);
    }

    /// <summary>
    /// An episode's season, number and title from its file name: "Show (2005) - s01e02 - Title.mkv" gives (1, 2, "Title").
    /// A multi-episode file ("s01e01-e02") takes the first number. Without an "sNNeNN" marker the season comes from the folder ("Season 3", "Specials" = 0),
    /// the episode is 0 and the title is the whole name. A missing title is "Episode N".
    /// </summary>
    public static (int SeasonNo, int EpisodeNo, string Title) ParseEpisodeFileName(string fileName, string seasonFolderName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Trim();
        Match match = EpisodeRegex().Match(name);
        if (!match.Success) return (ParseSeasonFolder(seasonFolderName), 0, name);

        int season = int.Parse(match.Groups["season"].Value), episode = int.Parse(match.Groups["episode"].Value);
        string title = match.Groups["title"].Value.Trim();
        return (season, episode, title.Length > 0 ? title : $"Episode {episode}");
    }

    /// <summary>
    /// The start of an episode file name up to and including its "sNNeNN" marker ("Show (2005) - s01e02" of "Show (2005) - s01e02 - Title"),
    /// or null without a marker.
    /// </summary>
    public static string? EpisodeMarkerPrefix(string fileNameWithoutExtension)
    {
        Match match = EpisodeMarkerRegex().Match(fileNameWithoutExtension);
        return match.Success ? fileNameWithoutExtension[..(match.Index + match.Length)] : null;
    }

    /// <summary>The season number of a folder: "Season 01" and "S2" give 1 and 2; "Specials" gives 0; anything else 0.</summary>
    public static int ParseSeasonFolder(string folderName)
    {
        Match match = SeasonFolderRegex().Match(folderName.Trim());
        return match.Success ? int.Parse(match.Groups["season"].Value) : 0;
    }

    // Optional "disc-" or "disc.", then the track number (not followed by another digit), then a separator and the title.
    [GeneratedRegex(@"^(?:(?<disc>\d{1,2})[-.](?=\d))?(?<track>\d{1,3})(?!\d)\s*(?:[-._]\s*|\s)(?<title>.+)$")]
    private static partial Regex TrackRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*\((?<year>\d{4})\)$")]
    private static partial Regex NameYearRegex();

    // "s01e02", optionally "-e03" / "e03" for more episodes, then an optional " - Title".
    [GeneratedRegex(@"\bs(?<season>\d{1,3})e(?<episode>\d{1,4})(?:-?e\d{1,4})*\b\s*(?:-\s*)?(?<title>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"\bs\d{1,3}e\d{1,4}(?:-?e\d{1,4})*\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeMarkerRegex();

    [GeneratedRegex(@"^(?:season|s)\s*(?<season>\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();
}
