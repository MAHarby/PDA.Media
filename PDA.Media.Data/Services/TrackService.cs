using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes tracks (see <see cref="DataService{TEntity}"/>). A track is the same as an existing one when the
/// album, track number and name match.
/// </summary>
public class TrackService : DataService<Track>
{
    public TrackService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public TrackService(IDbContextFactory<DataContext> contextFactory, ILogger<TrackService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<Track, bool>> IsSameAs(Track record)
        => t => t.AlbumId == record.AlbumId && t.TrackNo == record.TrackNo && t.Name == record.Name;

    /// <summary>An album's tracks in track order.</summary>
    public List<Track> GetAllRecordsByAlbumId(int albumId)
        => List(q => q.Where(t => t.AlbumId == albumId).OrderBy(t => t.TrackNo).ThenBy(t => t.Name));

    public Task<List<Track>> GetAllRecordsByAlbumIdAsync(int albumId, CancellationToken cancellationToken = default)
        => ListAsync(q => q.Where(t => t.AlbumId == albumId).OrderBy(t => t.TrackNo).ThenBy(t => t.Name), cancellationToken);

    /// <summary>An artist's tracks, album by album in track order.</summary>
    public List<Track> GetAllRecordsByArtistId(int artistId)
        => List(q => q.Where(t => t.ArtistId == artistId).OrderBy(t => t.AlbumId).ThenBy(t => t.TrackNo));

    public Task<List<Track>> GetAllRecordsByArtistIdAsync(int artistId, CancellationToken cancellationToken = default)
        => ListAsync(q => q.Where(t => t.ArtistId == artistId).OrderBy(t => t.AlbumId).ThenBy(t => t.TrackNo), cancellationToken);
}
