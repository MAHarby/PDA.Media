using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Services;

/// <summary>
/// Operations on the media cache as a whole: the tables a re-scan of the media folders rebuilds (Artists, Albums, Tracks, Movies, TVShows, TVShowEpisodes).
/// Each method creates its own short-lived <see cref="DataContext"/>.
/// </summary>
public class MediaCacheService
{
    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<MediaCacheService> _logger;

    public MediaCacheService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MediaCacheService(IDbContextFactory<DataContext> contextFactory, ILogger<MediaCacheService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<MediaCacheService>.Instance;
    }

    /// <summary>
    /// Permanently deletes everything in the media cache, including soft-deleted rows, by running the dbo.ClearMediaCache stored procedure (one transaction).
    /// Artist 0 and the lookup and configuration tables (types, media categories, settings) are kept, and ids are not restarted.
    /// This also loses anything that isn't in the media folders (favourites, notes, external ids), so it is for starting again, not for a normal re-scan.
    /// </summary>
    public MediaCacheCleared ClearMediaCache()
    {
        using var context = _contextFactory.CreateDbContext();

        // ToList, not Single: EF can't add anything to an EXEC, so the row is read back as it is.
        MediaCacheCleared cleared = context.Database.SqlQuery<MediaCacheCleared>($"EXEC [dbo].[ClearMediaCache]").ToList().Single();

        LogCleared(cleared);
        return cleared;
    }

    public async Task<MediaCacheCleared> ClearMediaCacheAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<MediaCacheCleared> rows = await context.Database.SqlQuery<MediaCacheCleared>($"EXEC [dbo].[ClearMediaCache]")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        MediaCacheCleared cleared = rows.Single();

        LogCleared(cleared);
        return cleared;
    }

    private void LogCleared(MediaCacheCleared cleared) => _logger.LogInformation(
        "Cleared the media cache: {Artists} artists, {Albums} albums, {Tracks} tracks, {Movies} movies, {TVShows} TV shows, {TVShowEpisodes} episodes",
        cleared.Artists, cleared.Albums, cleared.Tracks, cleared.Movies, cleared.TVShows, cleared.TVShowEpisodes);
}
