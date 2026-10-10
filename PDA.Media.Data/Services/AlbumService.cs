using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes albums. The shared operations (get, list, search, add, update, delete) come from
/// <see cref="DataService{TEntity}"/>; an album is the same as an existing one when the artist and name match.
/// </summary>
public class AlbumService : DataService<Album>
{
    public AlbumService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public AlbumService(IDbContextFactory<DataContext> contextFactory, ILogger<AlbumService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<Album, bool>> IsSameAs(Album record)
        => a => a.ArtistId == record.ArtistId && a.Name == record.Name;

    // Reads.
    // ==================================================================================================

    public Album? GetRecordByArtistIdAndName(int artistId, string name)
        => First(a => a.ArtistId == artistId && a.Name == name);

    public Task<Album?> GetRecordByArtistIdAndNameAsync(int artistId, string name, CancellationToken cancellationToken = default)
        => FirstAsync(a => a.ArtistId == artistId && a.Name == name, cancellationToken);

    /// <summary>Albums whose name matches <paramref name="searchTerm"/>, optionally with their artist loaded.</summary>
    public List<Album> GetAllRecords(string searchTerm, bool includeArtists)
        => List(q => DefaultOrder(WhereNameMatches(includeArtists ? q.Include(a => a.Artist) : q, searchTerm)));

    public Task<List<Album>> GetAllRecordsAsync(string searchTerm, bool includeArtists, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(WhereNameMatches(includeArtists ? q.Include(a => a.Artist) : q, searchTerm)), cancellationToken);

    public List<Album> GetAllRecordsByArtistId(int artistId)
        => List(q => DefaultOrder(q.Where(a => a.ArtistId == artistId)));

    public Task<List<Album>> GetAllRecordsByArtistIdAsync(int artistId, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(a => a.ArtistId == artistId)), cancellationToken);

    // Maintenance.
    // ==================================================================================================

    private const string ReseedAlbumsSql = "DBCC CHECKIDENT ('Albums', RESEED, 0)";

    /// <summary>
    /// Permanently deletes every album, including soft-deleted ones, and restarts the ids at 1. The database
    /// deletes their tracks too (FK_Tracks_Albums is ON DELETE CASCADE). Returns false, and changes nothing, if it
    /// fails (for example without permission to run DBCC CHECKIDENT).
    /// </summary>
    public bool TruncateTable()
    {
        try
        {
            using var context = ContextFactory.CreateDbContext();

            // The connection retries on failure, so a transaction has to run through the execution strategy (which
            // re-runs the whole block if the connection drops part way).
            int deleted = context.Database.CreateExecutionStrategy().Execute(() =>
            {
                using var transaction = context.Database.BeginTransaction();

                // IgnoreQueryFilters: ExecuteDelete applies the soft-delete filter too, which would leave deleted
                // rows behind and make the reseed reuse their ids.
                int count = context.Albums.IgnoreQueryFilters([DataContext.SoftDeleteFilter]).ExecuteDelete();
                context.Database.ExecuteSqlRaw(ReseedAlbumsSql);
                transaction.Commit();
                return count;
            });

            Logger.LogInformation("Truncated Albums: {Count} albums deleted", deleted);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Couldn't truncate Albums");
            return false;
        }
    }

    public async Task<bool> TruncateTableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            int deleted = await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                int count = await context.Albums.IgnoreQueryFilters([DataContext.SoftDeleteFilter])
                    .ExecuteDeleteAsync(token).ConfigureAwait(false);
                await context.Database.ExecuteSqlRawAsync(ReseedAlbumsSql, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return count;
            }, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Truncated Albums: {Count} albums deleted", deleted);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Couldn't truncate Albums");
            return false;
        }
    }
}
