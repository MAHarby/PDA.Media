using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes artists (see <see cref="DataService{TEntity}"/>). An artist is the same as an existing one when
/// the name matches. Artist 0 is the "unknown" artist that albums fall back to, so it can't be deleted.
/// </summary>
public class ArtistService : DataService<Artist>
{
    public ArtistService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public ArtistService(IDbContextFactory<DataContext> contextFactory, ILogger<ArtistService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<Artist, bool>> IsSameAs(Artist record) => a => a.Name == record.Name;

    protected override bool IsProtected(int id) => id == 0;

    public Artist? GetRecordByName(string name)
        => First(a => a.Name == name);

    public Task<Artist?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(a => a.Name == name, cancellationToken);
}
