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
}
