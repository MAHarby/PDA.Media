using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.Services;

/// <summary>
/// Builds Plex-style output paths from source media file names, following
/// https://support.plex.tv/articles/naming-and-organizing-your-tv-show-files/ and
/// https://support.plex.tv/articles/naming-and-organizing-your-movie-media-files/.
/// <list type="bullet">
/// <item>TV: <c>Show (Year)/Season 01/Show (Year) - s01e02 - Episode Title.mkv</c></item>
/// <item>Movie: <c>Movie (Year)/Movie (Year).mkv</c></item>
/// </list>
/// Release tags (Bluray, 1080p, x265, ...) and everything after them are removed.
/// </summary>
public static partial class PlexNaming
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // [bracketed] and {braced} tags, e.g. "[Bluray-1080p]" or "{tmdb-1234}".
    [GeneratedRegex(@"\[[^\]]*\]|\{[^}]*\}", Options)]
    private static partial Regex BracketedTags { get; }

    [GeneratedRegex(@"[._]+", Options)]
    private static partial Regex DotsAndUnderscores { get; }

    // The first release tag (as a whole word) through to the end of the name. Words that also appear in real
    // titles (e.g. "Web" in "Charlotte's Web") are deliberately left out; release names use WEB-DL / WEBRip.
    [GeneratedRegex(@"[\s(\-]+(?:2160p|1080[pi]|720p|576[pi]|480[pi]|4k|uhd|blu-?ray|b[dr]rip|dvd(?:rip|r|9|5)?|web-?dl|web-?rip|hdtv|sdtv|remux|x26[45]|h\s?26[45]|hevc|avc|xvid|divx|aac|ac3|e-?ac-?3|dts(?:-?hd)?|truehd|ddp?\s?[257]\s?[01]|atmos|10-?bit|8-?bit|hdr10?|sdr|proper|repack|extended|unrated)(?:[\s)\-].*)?$", Options)]
    private static partial Regex ReleaseTagToEnd { get; }

    [GeneratedRegex(@"\s+", Options)]
    private static partial Regex Whitespace { get; }

    // S01E02, S01E02E03, S01E02-E03, S1 E2 ... or 1x02.
    [GeneratedRegex(@"^(?<show>.*?)[\s\-]*\b(?:S(?<season>\d{1,2})\s?E(?<episode>\d{1,3})(?:\s?-?\s?E(?<last>\d{1,3}))*|(?<season>\d{1,2})x(?<episode>\d{2,3}))\b(?<rest>.*)$", Options)]
    private static partial Regex EpisodeMarker { get; }

    // The last year in the name (19xx/20xx, optionally in brackets), not at the very start.
    [GeneratedRegex(@"^(?<title>.+?)[\s\-]*\(?(?<year>(?:19|20)\d{2})\)?(?!.*\b(?:19|20)\d{2}\b)", Options)]
    private static partial Regex TitleAndYear { get; }

    // Season folders such as "Season 1", "Season 01", "S01" or "Specials".
    [GeneratedRegex(@"^(?:season\s*\d+|s\d{1,2}|specials)$", Options)]
    private static partial Regex SeasonFolder { get; }

    /// <summary>
    /// Builds the output path, relative to the destination folder, for <paramref name="sourceFile"/>.
    /// </summary>
    /// <param name="sourceRoot">The source folder the file was selected from (used to find the show folder).</param>
    /// <param name="sourceFile">Full path of the source media file.</param>
    /// <param name="containerFormat">Output container, e.g. "mkv".</param>
    public static PlexOutputName GetOutputName(string sourceRoot, string sourceFile, string containerFormat)
    {
        string extension = "." + containerFormat.Trim().TrimStart('.').ToLowerInvariant();
        string fileName = Path.GetFileNameWithoutExtension(sourceFile);
        string normalised = Normalise(fileName);

        var episode = EpisodeMarker.Match(normalised);
        if (episode.Success)
        {
            int season = int.Parse(episode.Groups["season"].Value);
            string episodes = "e" + int.Parse(episode.Groups["episode"].Value).ToString("00");
            if (episode.Groups["last"].Success)
            {
                episodes += "-e" + int.Parse(episode.Groups["last"].Captures[^1].Value).ToString("00");
            }

            string show = GetShowFolderName(sourceRoot, sourceFile)
                          ?? CleanTitle(episode.Groups["show"].Value);
            if (show.Length == 0) show = "Unknown Show";

            string title = CleanTitle(episode.Groups["rest"].Value);
            string name = $"{show} - s{season:00}{episodes}" + (title.Length > 0 ? $" - {title}" : "");

            return new PlexOutputName(PlexMediaKind.TvEpisode,
                Path.Combine(SafeName(show), $"Season {season:00}", SafeName(name) + extension));
        }

        string movie = CleanMovieName(normalised);
        if (movie.Length == 0) movie = Normalise(fileName);
        string safeMovie = SafeName(movie);
        return new PlexOutputName(PlexMediaKind.Movie, Path.Combine(safeMovie, safeMovie + extension));
    }

    /// <summary>
    /// Cleans a movie name to <c>Title (Year)</c>, dropping release tags and anything after the year.
    /// </summary>
    public static string CleanMovieName(string name)
    {
        string cleaned = CutReleaseTags(Normalise(name));
        var match = TitleAndYear.Match(cleaned);
        if (match.Success && match.Groups["title"].Value.Trim(' ', '-', '(').Length > 0)
        {
            return $"{match.Groups["title"].Value.Trim(' ', '-', '(')} ({match.Groups["year"].Value})";
        }

        return cleaned;
    }

    // The show folder is the first folder under the source root, e.g. "<root>/Show (2005)/Season 1/file.mkv".
    // When the source root is itself a show folder ("<root>/Season 1/file.mkv"), the root's own name is used.
    private static string? GetShowFolderName(string sourceRoot, string sourceFile)
    {
        string root = sourceRoot.TrimEnd('/', '\\');
        string? fileFolder = Path.GetDirectoryName(sourceFile);
        if (string.IsNullOrEmpty(root) || fileFolder is null) return null;

        string relative = Path.GetRelativePath(root, fileFolder);
        string[] segments = relative == "." || relative.StartsWith("..", StringComparison.Ordinal)
            ? []
            : relative.Split('/', '\\');

        string? folder = segments.Length == 0 || SeasonFolder.IsMatch(segments[0])
            ? (segments.Length == 0 ? null : Path.GetFileName(root))
            : segments[0];

        if (folder is null) return null;
        string cleaned = CleanMovieName(folder); // keeps "(Year)", drops release tags
        return cleaned.Length > 0 ? cleaned : null;
    }

    private static string CleanTitle(string text) => CutReleaseTags(" " + text).Trim(' ', '-');

    private static string CutReleaseTags(string text)
    {
        string cut = ReleaseTagToEnd.Replace(text, "");
        return Whitespace.Replace(cut, " ").Trim(' ', '-');
    }

    // Drops bracketed tags and turns dots/underscores into spaces ("The.Office.S01E01" -> "The Office S01E01").
    private static string Normalise(string name)
    {
        string text = BracketedTags.Replace(name, " ");
        text = DotsAndUnderscores.Replace(text, " ");
        return Whitespace.Replace(text, " ").Trim(' ', '-');
    }

    // Output goes to a Windows share, so remove characters Windows doesn't allow in names,
    // whatever platform this runs on. A colon becomes " -" ("Star Trek: Picard" -> "Star Trek - Picard").
    private static string SafeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name.Replace(":", " -"))
        {
            if (c < 32 || "<>\"/\\|?*".Contains(c)) continue;
            builder.Append(c);
        }

        return Whitespace.Replace(builder.ToString(), " ").Trim(' ', '.');
    }
}
