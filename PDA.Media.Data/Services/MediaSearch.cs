using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>What <see cref="MediaSearchService.Search"/> looks for, and where.</summary>
public sealed record MediaSearchCriteria
{
    /// <summary>
    /// Text to find in the Name, Description or Notes of artists, albums, tracks, movies, TV shows and episodes: without a '*' the field only has to contain it;
    /// '*' is a wildcard. Empty (and not <see cref="FavouritesOnly"/>): browse everything under the roots.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>The root folders of the music categories to search (rows belong to a category by folder). Empty: no music.</summary>
    public IReadOnlyList<string> MusicRoots { get; init; } = [];

    public IReadOnlyList<string> MovieRoots { get; init; } = [];
    public IReadOnlyList<string> TVRoots { get; init; } = [];

    /// <summary>
    /// Only albums of these album types (and their artists and tracks); null for any type. Like <see cref="MovieTypeIds"/> and <see cref="TVShowTypeIds"/>,
    /// it narrows its own kind of media only: to leave a kind out, give it no roots.
    /// </summary>
    public IReadOnlyCollection<int>? AlbumTypeIds { get; init; }

    /// <summary>Only movies of these movie types; null for any type.</summary>
    public IReadOnlyCollection<int>? MovieTypeIds { get; init; }

    /// <summary>Only TV shows of these show types (and their episodes); null for any type.</summary>
    public IReadOnlyCollection<int>? TVShowTypeIds { get; init; }

    /// <summary>Only rows marked as favourites match.</summary>
    public bool FavouritesOnly { get; init; }

    /// <summary>Soft-deleted rows are included (and their children lists show deleted rows too).</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>The most rows of each list returned; <see cref="MediaSearchResult.IsTruncated"/> says when there were more.</summary>
    public int MaxResults { get; init; } = 2000;
}

/// <summary>
/// The top-level items a search found (artists, movies, TV shows), and which rows matched below them, so a tree can open down to each match.
/// Children are loaded when a branch is opened (<see cref="MediaSearchService.GetArtistAlbums"/>, ...).
/// </summary>
public sealed class MediaSearchResult
{
    /// <summary>Artists that matched, or have an album or track that matched (by name).</summary>
    public List<Artist> Artists { get; } = [];
    public HashSet<int> MatchedArtistIds { get; } = [];

    /// <summary>Albums that matched, and the albums of tracks that matched (open these).</summary>
    public List<Album> PathAlbums { get; } = [];
    public HashSet<int> MatchedAlbumIds { get; } = [];
    public List<Track> MatchedTracks { get; } = [];

    public List<Movie> Movies { get; } = [];

    /// <summary>TV shows that matched, or have an episode that matched.</summary>
    public List<TVShow> Shows { get; } = [];
    public HashSet<int> MatchedShowIds { get; } = [];
    public List<TVShowEpisode> MatchedEpisodes { get; } = [];

    /// <summary>True when a list was cut at <see cref="MediaSearchCriteria.MaxResults"/>.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>True for a search that listed everything (no text and not favourites only): nothing is marked as matched.</summary>
    public bool IsBrowse { get; set; }
}
