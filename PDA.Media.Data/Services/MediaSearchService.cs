using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Searches the media cache (music, movies, TV) by Name, Description and Notes within the root folders of chosen media categories, loads the children of
/// a search result as its branches are opened, and saves the fields of a row that can be edited by hand.
/// Rows belong to a category by folder (anything under its root), so an artist with albums in several music categories is found in each of them.
/// </summary>
/// <remarks>
/// Each method creates its own short-lived <see cref="DataContext"/>. Rows are returned detached (no tracking).
/// The sync methods run the same queries without awaiting (the async core is called with <c>isAsync: false</c>).
/// </remarks>
public class MediaSearchService
{
    private static readonly MethodInfo LikeMethod =
        typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<MediaSearchService> _logger;

    public MediaSearchService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MediaSearchService(IDbContextFactory<DataContext> contextFactory, ILogger<MediaSearchService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<MediaSearchService>.Instance;
    }

    // Search.
    // ==================================================================================================

    /// <summary>Finds the artists, movies and TV shows under the criteria's roots that match (themselves, or through an album, track or episode).</summary>
    public MediaSearchResult Search(MediaSearchCriteria criteria) => SearchCoreAsync(criteria, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="Search"/>
    public Task<MediaSearchResult> SearchAsync(MediaSearchCriteria criteria, CancellationToken cancellationToken = default)
        => SearchCoreAsync(criteria, true, cancellationToken);

    private async Task<MediaSearchResult> SearchCoreAsync(MediaSearchCriteria criteria, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        string? pattern = DataService<Artist>.ToLikePattern(criteria.Text);
        var result = new MediaSearchResult { IsBrowse = pattern == null && !criteria.FavouritesOnly };
        int max = criteria.MaxResults;

        if (criteria.MusicRoots.Count > 0)
        {
            IQueryable<Artist> artists = Rows<Artist>(context, criteria.IncludeDeleted);
            IQueryable<Album> albums = Rows<Album>(context, criteria.IncludeDeleted).Where(UnderAny<Album>(nameof(Album.Folder), criteria.MusicRoots));
            IQueryable<Track> tracks = Rows<Track>(context, criteria.IncludeDeleted).Where(UnderAny<Track>(nameof(Track.Folder), criteria.MusicRoots));

            // An artist is in the chosen categories when its folder is under one of their roots, or it has an album there.
            IQueryable<int> artistIdsInRoots = artists.Where(UnderAny<Artist>(nameof(Artist.Folder), criteria.MusicRoots)).Select(a => a.Id)
                .Union(albums.Select(a => a.ArtistId));
            IQueryable<Artist> artistsInRoots = artists.Where(a => artistIdsInRoots.Contains(a.Id));

            HashSet<int> topIds;
            if (result.IsBrowse)
            {
                topIds = (await ToListAsync(artistsInRoots.Select(a => a.Id), isAsync, cancellationToken).ConfigureAwait(false)).ToHashSet();
            }
            else
            {
                result.MatchedArtistIds.UnionWith(await ToListAsync(Matching(artistsInRoots, pattern, criteria).Select(a => a.Id), isAsync, cancellationToken).ConfigureAwait(false));
                List<Album> matchedAlbums = await ToListAsync(Matching(albums, pattern, criteria).OrderBy(a => a.Name).Take(max + 1), isAsync, cancellationToken).ConfigureAwait(false);
                List<Track> matchedTracks = await ToListAsync(Matching(tracks, pattern, criteria).OrderBy(t => t.TrackNo).Take(max + 1), isAsync, cancellationToken).ConfigureAwait(false);
                result.IsTruncated |= Trim(matchedAlbums, max) | Trim(matchedTracks, max);
                result.MatchedAlbumIds.UnionWith(matchedAlbums.Select(a => a.Id));
                result.MatchedTracks.AddRange(matchedTracks);

                // The albums on the way to each match (their own row, or the album of a matching track).
                List<int> pathAlbumIds = matchedAlbums.Select(a => a.Id).Union(matchedTracks.Select(t => t.AlbumId)).ToList();
                result.PathAlbums.AddRange(await ToListAsync(Rows<Album>(context, true).Where(a => pathAlbumIds.Contains(a.Id)), isAsync, cancellationToken).ConfigureAwait(false));
                topIds = result.MatchedArtistIds.Union(result.PathAlbums.Select(a => a.ArtistId)).ToHashSet();
            }

            List<int> ids = topIds.ToList();
            List<Artist> top = await ToListAsync(Rows<Artist>(context, true).Where(a => ids.Contains(a.Id)).OrderBy(a => a.Name).Take(max + 1), isAsync, cancellationToken)
                .ConfigureAwait(false);
            result.IsTruncated |= Trim(top, max);
            result.Artists.AddRange(top);
        }

        if (criteria.MovieRoots.Count > 0)
        {
            IQueryable<Movie> movies = Rows<Movie>(context, criteria.IncludeDeleted).Where(UnderAny<Movie>(nameof(Movie.Folder), criteria.MovieRoots));
            if (!result.IsBrowse) movies = Matching(movies, pattern, criteria);
            List<Movie> found = await ToListAsync(movies.OrderBy(m => m.Name).Take(max + 1), isAsync, cancellationToken).ConfigureAwait(false);
            result.IsTruncated |= Trim(found, max);
            result.Movies.AddRange(found);
        }

        if (criteria.TVRoots.Count > 0)
        {
            IQueryable<TVShow> shows = Rows<TVShow>(context, criteria.IncludeDeleted).Where(UnderAny<TVShow>(nameof(TVShow.Folder), criteria.TVRoots));
            IQueryable<TVShowEpisode> episodes = Rows<TVShowEpisode>(context, criteria.IncludeDeleted)
                .Where(UnderAny<TVShowEpisode>(nameof(TVShowEpisode.Folder), criteria.TVRoots));

            HashSet<int> topIds;
            if (result.IsBrowse)
            {
                topIds = (await ToListAsync(shows.Select(s => s.Id), isAsync, cancellationToken).ConfigureAwait(false)).ToHashSet();
            }
            else
            {
                result.MatchedShowIds.UnionWith(await ToListAsync(Matching(shows, pattern, criteria).Select(s => s.Id), isAsync, cancellationToken).ConfigureAwait(false));
                List<TVShowEpisode> matchedEpisodes = await ToListAsync(
                    Matching(episodes, pattern, criteria).OrderBy(e => e.SeasonNo).ThenBy(e => e.EpisodeNo).Take(max + 1), isAsync, cancellationToken).ConfigureAwait(false);
                result.IsTruncated |= Trim(matchedEpisodes, max);
                result.MatchedEpisodes.AddRange(matchedEpisodes);
                topIds = result.MatchedShowIds.Union(matchedEpisodes.Select(e => e.TVShowId)).ToHashSet();
            }

            List<int> ids = topIds.ToList();
            List<TVShow> top = await ToListAsync(Rows<TVShow>(context, true).Where(s => ids.Contains(s.Id)).OrderBy(s => s.Name).ThenBy(s => s.ReleaseYear).Take(max + 1),
                isAsync, cancellationToken).ConfigureAwait(false);
            result.IsTruncated |= Trim(top, max);
            result.Shows.AddRange(top);
        }

        _logger.LogDebug("Media search {Text}: {Artists} artists, {Movies} movies, {Shows} shows", criteria.Text, result.Artists.Count, result.Movies.Count, result.Shows.Count);
        return result;
    }

    // Children (loaded when a branch is opened).
    // ==================================================================================================

    /// <summary>An artist's albums by name, only those under <paramref name="musicRoots"/> (all of them if it is empty).</summary>
    public List<Album> GetArtistAlbums(int artistId, IReadOnlyList<string> musicRoots, bool includeDeleted = false)
        => GetArtistAlbumsCoreAsync(artistId, musicRoots, includeDeleted, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetArtistAlbums"/>
    public Task<List<Album>> GetArtistAlbumsAsync(int artistId, IReadOnlyList<string> musicRoots, bool includeDeleted = false, CancellationToken cancellationToken = default)
        => GetArtistAlbumsCoreAsync(artistId, musicRoots, includeDeleted, true, cancellationToken);

    private async Task<List<Album>> GetArtistAlbumsCoreAsync(int artistId, IReadOnlyList<string> musicRoots, bool includeDeleted, bool isAsync,
        CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<Album> albums = Rows<Album>(context, includeDeleted).Where(a => a.ArtistId == artistId);
        if (musicRoots.Count > 0) albums = albums.Where(UnderAny<Album>(nameof(Album.Folder), musicRoots));
        return await ToListAsync(albums.OrderBy(a => a.Name), isAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An album's tracks in track order.</summary>
    public List<Track> GetAlbumTracks(int albumId, bool includeDeleted = false)
        => GetAlbumTracksCoreAsync(albumId, includeDeleted, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetAlbumTracks"/>
    public Task<List<Track>> GetAlbumTracksAsync(int albumId, bool includeDeleted = false, CancellationToken cancellationToken = default)
        => GetAlbumTracksCoreAsync(albumId, includeDeleted, true, cancellationToken);

    private async Task<List<Track>> GetAlbumTracksCoreAsync(int albumId, bool includeDeleted, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<Track> tracks = Rows<Track>(context, includeDeleted).Where(t => t.AlbumId == albumId).OrderBy(t => t.TrackNo).ThenBy(t => t.Name);
        return await ToListAsync(tracks, isAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A TV show's episodes by season and episode number.</summary>
    public List<TVShowEpisode> GetShowEpisodes(int showId, bool includeDeleted = false)
        => GetShowEpisodesCoreAsync(showId, includeDeleted, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetShowEpisodes"/>
    public Task<List<TVShowEpisode>> GetShowEpisodesAsync(int showId, bool includeDeleted = false, CancellationToken cancellationToken = default)
        => GetShowEpisodesCoreAsync(showId, includeDeleted, true, cancellationToken);

    private async Task<List<TVShowEpisode>> GetShowEpisodesCoreAsync(int showId, bool includeDeleted, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<TVShowEpisode> episodes = Rows<TVShowEpisode>(context, includeDeleted).Where(e => e.TVShowId == showId)
            .OrderBy(e => e.SeasonNo).ThenBy(e => e.EpisodeNo).ThenBy(e => e.Name);
        return await ToListAsync(episodes, isAsync, cancellationToken).ConfigureAwait(false);
    }

    // Editing.
    // ==================================================================================================

    /// <summary>
    /// Saves the fields of <paramref name="edited"/> that can be edited by hand (see <see cref="CopyEditableFields"/>) onto its stored row (even a
    /// soft-deleted one), and returns the saved row. The other fields are left as stored: the scan owns them, or they are part of how the scan matches the row.
    /// Throws InvalidOperationException if the row isn't there.
    /// </summary>
    public TEntity SaveEditableFields<TEntity>(TEntity edited) where TEntity : class, IEntity
        => SaveEditableFieldsCoreAsync(edited, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveEditableFields"/>
    public Task<TEntity> SaveEditableFieldsAsync<TEntity>(TEntity edited, CancellationToken cancellationToken = default) where TEntity : class, IEntity
        => SaveEditableFieldsCoreAsync(edited, true, cancellationToken);

    private async Task<TEntity> SaveEditableFieldsCoreAsync<TEntity>(TEntity edited, bool isAsync, CancellationToken cancellationToken) where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(edited);
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<TEntity> query = context.Set<TEntity>().IgnoreQueryFilters([DataContext.SoftDeleteFilter]).Where(e => e.Id == edited.Id);
        TEntity stored = (isAsync ? await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) : query.FirstOrDefault())
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} {edited.Id} isn't in the database.");

        CopyEditableFields(stored, edited);
        if (isAsync) await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        else context.SaveChanges();
        _logger.LogInformation("Saved {Entity} {Id} {Name}", typeof(TEntity).Name, stored.Id, stored.Name);
        return stored;
    }

    /// <summary>
    /// Copies the fields that can be edited by hand from <paramref name="source"/> to <paramref name="target"/>:
    /// every row's Description, Notes and IsFavourite, plus MusicBrainzId (artist, album, track), the type (album, movie, TV show), Length (album, track) and
    /// ReleaseDate (episode). Names, folders, file names, cover art, numbers and years aren't copied: the scan owns them, or matches rows by them.
    /// </summary>
    public static void CopyEditableFields(IEntity target, IEntity source)
    {
        switch (target, source)
        {
            case (Artist t, Artist s):
                (t.Description, t.Notes, t.IsFavourite, t.MusicBrainzId) = (s.Description, s.Notes, s.IsFavourite, s.MusicBrainzId);
                break;
            case (Album t, Album s):
                (t.Description, t.Notes, t.IsFavourite, t.MusicBrainzId, t.AlbumTypeId, t.Length) = (s.Description, s.Notes, s.IsFavourite, s.MusicBrainzId, s.AlbumTypeId, s.Length);
                break;
            case (Track t, Track s):
                (t.Description, t.Notes, t.IsFavourite, t.MusicBrainzId, t.Length) = (s.Description, s.Notes, s.IsFavourite, s.MusicBrainzId, s.Length);
                break;
            case (Movie t, Movie s):
                (t.Description, t.Notes, t.IsFavourite, t.MovieTypeId) = (s.Description, s.Notes, s.IsFavourite, s.MovieTypeId);
                break;
            case (TVShow t, TVShow s):
                (t.Description, t.Notes, t.IsFavourite, t.TVShowTypeId) = (s.Description, s.Notes, s.IsFavourite, s.TVShowTypeId);
                break;
            case (TVShowEpisode t, TVShowEpisode s):
                (t.Description, t.Notes, t.IsFavourite, t.ReleaseDate) = (s.Description, s.Notes, s.IsFavourite, s.ReleaseDate);
                break;
            default:
                throw new NotSupportedException($"{target.GetType().Name} rows can't be edited here.");
        }
    }

    // Query helpers.
    // ==================================================================================================

    private async Task<DataContext> CreateContextAsync(bool isAsync, CancellationToken cancellationToken) =>
        isAsync ? await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false) : _contextFactory.CreateDbContext();

    private static async Task<List<T>> ToListAsync<T>(IQueryable<T> query, bool isAsync, CancellationToken cancellationToken) =>
        isAsync ? await query.ToListAsync(cancellationToken).ConfigureAwait(false) : query.ToList();

    /// <summary>A table without tracking; soft-deleted rows included or not.</summary>
    private static IQueryable<T> Rows<T>(DataContext context, bool includeDeleted) where T : class =>
        includeDeleted ? context.Set<T>().IgnoreQueryFilters([DataContext.SoftDeleteFilter]).AsNoTracking() : context.Set<T>().AsNoTracking();

    /// <summary>Removes the items over <paramref name="max"/>; true if there were any.</summary>
    private static bool Trim<T>(List<T> list, int max)
    {
        if (list.Count <= max) return false;
        list.RemoveRange(max, list.Count - max);
        return true;
    }

    /// <summary>The rows whose Name, Description or Notes match the LIKE pattern (if any), and that are favourites (if asked).</summary>
    private static IQueryable<T> Matching<T>(IQueryable<T> query, string? pattern, MediaSearchCriteria criteria)
    {
        if (pattern != null) query = query.Where(TextMatches<T>(pattern));
        if (criteria.FavouritesOnly) query = query.Where(IsTrue<T>("IsFavourite"));
        return query;
    }

    /// <summary><c>e => LIKE(e.Name, pattern) || LIKE(e.Description, pattern) || LIKE(e.Notes, pattern)</c>, with the pattern as a query parameter.</summary>
    private static Expression<Func<T, bool>> TextMatches<T>(string pattern)
    {
        ParameterExpression e = Expression.Parameter(typeof(T), "e");
        Expression value = FolderQueries.AsParameter(pattern);
        Expression body = new[] { "Name", "Description", "Notes" }
            .Select(field => (Expression)Expression.Call(LikeMethod, Expression.Constant(EF.Functions), Expression.Property(e, field), value))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<T, bool>>(body, e);
    }

    private static Expression<Func<T, bool>> IsTrue<T>(string property)
    {
        ParameterExpression e = Expression.Parameter(typeof(T), "e");
        return Expression.Lambda<Func<T, bool>>(Expression.Property(e, property), e);
    }

    private static Expression<Func<T, bool>> UnderAny<T>(string folderProperty, IReadOnlyList<string> roots) => FolderQueries.UnderAny<T>(folderProperty, roots);
}
