using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes albums. Each method creates its own short-lived <see cref="DataContext"/> from the factory, so
/// the service is safe to keep for the life of the app and to call from more than one place at a time.
/// </summary>
/// <remarks>
/// Soft-deleted albums are hidden by the DataContext query filter, so no method here returns them. Audit fields
/// (CreatedOn / ModifiedOn ...) are set by DataContext when changes are saved.
/// </remarks>
public class AlbumService : IDataService<Album>
{
    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<AlbumService> _logger;

    public AlbumService(string connectionString) : this(new DataContextFactory(connectionString)) { }

    public AlbumService(IDbContextFactory<DataContext> contextFactory, ILogger<AlbumService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<AlbumService>.Instance;
    }

    // Reads.
    // ==================================================================================================
    // Reads use AsNoTracking: the context is thrown away straight after, so tracking would only cost time.

    public Album? GetRecordById(int id)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Albums.AsNoTracking().FirstOrDefault(a => a.Id == id);
    }

    public async Task<Album?> GetRecordByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public Album? GetRecordByArtistIdAndName(int artistId, string name)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Albums.AsNoTracking().FirstOrDefault(a => a.ArtistId == artistId && a.Name == name);
    }

    public async Task<Album?> GetRecordByArtistIdAndNameAsync(int artistId, string name, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Albums.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistId == artistId && a.Name == name, cancellationToken).ConfigureAwait(false);
    }

    public List<Album> GetAllRecords()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Albums.AsNoTracking().OrderBy(a => a.Name).ToList();
    }

    public async Task<List<Album>> GetAllRecordsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Albums.AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public List<Album> GetAllRecords(string searchTerm) => GetAllRecords(searchTerm, includeArtists: false);

    public Task<List<Album>> GetAllRecordsAsync(string searchTerm, CancellationToken cancellationToken = default)
        => GetAllRecordsAsync(searchTerm, includeArtists: false, cancellationToken);

    /// <summary>
    /// Albums whose name matches <paramref name="searchTerm"/>. Without a '*' the name only has to contain the term;
    /// '*' is a wildcard for any text ("Abba*" starts with Abba, "*Gold" ends with Gold, "Abba*Gold" both).
    /// An empty term or "*" returns every album. Matching ignores case.
    /// </summary>
    public List<Album> GetAllRecords(string searchTerm, bool includeArtists)
    {
        using var context = _contextFactory.CreateDbContext();
        return SearchQuery(context, searchTerm, includeArtists).ToList();
    }

    public async Task<List<Album>> GetAllRecordsAsync(string searchTerm, bool includeArtists, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await SearchQuery(context, searchTerm, includeArtists).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public List<Album> GetAllRecordsByArtistId(int artistId)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Albums.AsNoTracking().Where(a => a.ArtistId == artistId).OrderBy(a => a.Name).ToList();
    }

    public async Task<List<Album>> GetAllRecordsByArtistIdAsync(int artistId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Albums.AsNoTracking().Where(a => a.ArtistId == artistId).OrderBy(a => a.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<Album> SearchQuery(DataContext context, string searchTerm, bool includeArtists)
    {
        IQueryable<Album> query = context.Albums.AsNoTracking();
        if (includeArtists) query = query.Include(a => a.Artist);

        string? pattern = ToLikePattern(searchTerm);
        if (pattern != null) query = query.Where(a => EF.Functions.Like(a.Name, pattern));

        return query.OrderBy(a => a.Name);
    }

    /// <summary>
    /// Turns a search term into a SQL LIKE pattern, or null to match everything. LIKE's own wildcards (%, _ and [)
    /// in the term are escaped so they match literally; '*' becomes '%'.
    /// </summary>
    internal static string? ToLikePattern(string? searchTerm)
    {
        string term = searchTerm?.Trim() ?? "";
        if (term.Replace("*", "").Length == 0) return null;

        string escaped = term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        return term.Contains('*') ? escaped.Replace('*', '%') : $"%{escaped}%";
    }

    // Writes.
    // ==================================================================================================

    /// <summary>
    /// Adds an album, or returns the existing one if the artist already has an album with that name.
    /// </summary>
    public Album AddRecord(Album record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var context = _contextFactory.CreateDbContext();

        Album? existing = context.Albums.AsNoTracking().FirstOrDefault(a => a.ArtistId == record.ArtistId && a.Name == record.Name);
        if (existing != null) return existing;

        context.Albums.Add(record);
        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Someone else added the same album between the check and the save (the unique index rejected ours).
            Album? added = GetRecordByArtistIdAndName(record.ArtistId, record.Name);
            if (added == null) throw;
            return added;
        }

        _logger.LogInformation("Added album {AlbumId} {AlbumName} (artist {ArtistId})", record.Id, record.Name, record.ArtistId);
        return record;
    }

    public async Task<Album> AddRecordAsync(Album record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Album? existing = await context.Albums.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtistId == record.ArtistId && a.Name == record.Name, cancellationToken).ConfigureAwait(false);
        if (existing != null) return existing;

        context.Albums.Add(record);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Someone else added the same album between the check and the save (the unique index rejected ours).
            Album? added = await GetRecordByArtistIdAndNameAsync(record.ArtistId, record.Name, cancellationToken).ConfigureAwait(false);
            if (added == null) throw;
            return added;
        }

        _logger.LogInformation("Added album {AlbumId} {AlbumName} (artist {ArtistId})", record.Id, record.Name, record.ArtistId);
        return record;
    }

    /// <summary>
    /// Copies every column of <paramref name="record"/> onto album <paramref name="id"/> and saves it. Returns the
    /// saved album, or null if there is no album with that id. Audit fields are set by DataContext, not copied.
    /// </summary>
    public Album? UpdateRecord(int id, Album record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var context = _contextFactory.CreateDbContext();

        Album? album = context.Albums.FirstOrDefault(a => a.Id == id);
        if (album == null) return null;

        CopyValues(context, album, record);
        context.SaveChanges();

        _logger.LogInformation("Updated album {AlbumId} {AlbumName}", album.Id, album.Name);
        return album;
    }

    public async Task<Album?> UpdateRecordAsync(int id, Album record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Album? album = await context.Albums.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
        if (album == null) return null;

        CopyValues(context, album, record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Updated album {AlbumId} {AlbumName}", album.Id, album.Name);
        return album;
    }

    /// <summary>
    /// Copies all of record's column values onto the tracked album, so a column added to Album later is updated
    /// too. The key and the Created fields are kept (record may carry another id, or none), the Modified fields are
    /// set by DataContext, and EF only saves the columns that changed.
    /// </summary>
    private static void CopyValues(DataContext context, Album album, Album record)
    {
        var values = context.Entry(record).CurrentValues.Clone();
        values[nameof(Album.Id)] = album.Id;
        values[nameof(Album.CreatedOn)] = album.CreatedOn;
        values[nameof(Album.CreatedBy)] = album.CreatedBy;
        context.Entry(album).CurrentValues.SetValues(values);
    }

    /// <summary>Soft-deletes album <paramref name="id"/> (sets IsDeleted). Returns false if it wasn't found.</summary>
    public bool DeleteRecord(int id)
    {
        using var context = _contextFactory.CreateDbContext();

        Album? album = context.Albums.FirstOrDefault(a => a.Id == id);
        if (album == null) return false;

        album.IsDeleted = true;
        context.SaveChanges();

        _logger.LogInformation("Deleted album {AlbumId} {AlbumName}", album.Id, album.Name);
        return true;
    }

    public async Task<bool> DeleteRecordAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Album? album = await context.Albums.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
        if (album == null) return false;

        album.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Deleted album {AlbumId} {AlbumName}", album.Id, album.Name);
        return true;
    }

    public bool DeleteRecord(Album record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return DeleteRecord(record.Id);
    }

    public Task<bool> DeleteRecordAsync(Album record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return DeleteRecordAsync(record.Id, cancellationToken);
    }

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
            using var context = _contextFactory.CreateDbContext();

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

            _logger.LogInformation("Truncated Albums: {Count} albums deleted", deleted);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't truncate Albums");
            return false;
        }
    }

    public async Task<bool> TruncateTableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            int deleted = await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                int count = await context.Albums.IgnoreQueryFilters([DataContext.SoftDeleteFilter])
                    .ExecuteDeleteAsync(token).ConfigureAwait(false);
                await context.Database.ExecuteSqlRawAsync(ReseedAlbumsSql, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return count;
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Truncated Albums: {Count} albums deleted", deleted);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Couldn't truncate Albums");
            return false;
        }
    }
}
