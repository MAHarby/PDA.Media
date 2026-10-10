using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes TV shows (see <see cref="DataService{TEntity}"/>). A show is the same as an existing one when the
/// name and release year match (e.g. Doctor Who 1963 and Doctor Who 2005 are different shows).
/// </summary>
public class TVShowService : DataService<TVShow>
{
    public TVShowService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public TVShowService(IDbContextFactory<DataContext> contextFactory, ILogger<TVShowService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<TVShow, bool>> IsSameAs(TVShow record)
        => s => s.Name == record.Name && s.ReleaseYear == record.ReleaseYear;

    public TVShow? GetRecordByName(string name, int releaseYear)
        => First(s => s.Name == name && s.ReleaseYear == releaseYear);

    public Task<TVShow?> GetRecordByNameAsync(string name, int releaseYear, CancellationToken cancellationToken = default)
        => FirstAsync(s => s.Name == name && s.ReleaseYear == releaseYear, cancellationToken);

    public List<TVShow> GetAllRecordsByTVShowTypeId(int tvShowTypeId)
        => List(q => DefaultOrder(q.Where(s => s.TVShowTypeId == tvShowTypeId)));

    public Task<List<TVShow>> GetAllRecordsByTVShowTypeIdAsync(int tvShowTypeId, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(s => s.TVShowTypeId == tvShowTypeId)), cancellationToken);
}
