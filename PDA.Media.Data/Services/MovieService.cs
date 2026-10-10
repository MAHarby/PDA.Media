using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes movies (see <see cref="DataService{TEntity}"/>). A movie is the same as an existing one when the
/// name matches (Plex names include the year, e.g. "Alien (1979)").
/// </summary>
public class MovieService : DataService<Movie>
{
    public MovieService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MovieService(IDbContextFactory<DataContext> contextFactory, ILogger<MovieService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<Movie, bool>> IsSameAs(Movie record) => m => m.Name == record.Name;

    public Movie? GetRecordByName(string name)
        => First(m => m.Name == name);

    public Task<Movie?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(m => m.Name == name, cancellationToken);

    public List<Movie> GetAllRecordsByMovieTypeId(int movieTypeId)
        => List(q => DefaultOrder(q.Where(m => m.MovieTypeId == movieTypeId)));

    public Task<List<Movie>> GetAllRecordsByMovieTypeIdAsync(int movieTypeId, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(m => m.MovieTypeId == movieTypeId)), cancellationToken);
}
