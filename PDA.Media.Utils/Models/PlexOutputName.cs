namespace PDA.Media.Utils.Models;

public enum PlexMediaKind
{
    /// <summary>Recognised by an episode marker such as S01E02 or 1x02.</summary>
    TvEpisode,

    /// <summary>Anything without an episode marker; a year is used when one is found.</summary>
    Movie
}

/// <summary>
/// Where a source file should go under the destination folder, named for Plex.
/// </summary>
/// <param name="Kind">Whether the file was treated as a TV episode or a movie.</param>
/// <param name="RelativePath">Path relative to the destination folder, e.g. <c>Show (2005)/Season 01/Show (2005) - s01e01 - Pilot.mkv</c>.</param>
public sealed record PlexOutputName(PlexMediaKind Kind, string RelativePath);
