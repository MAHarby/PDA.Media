using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes MovieType lookup rows (see <see cref="DataService{TEntity}"/>). Deleting one removes the row, and
/// the database moves the movies that used it to movie type 0 (ON DELETE SET DEFAULT);
/// movie type 0 itself can't be deleted.
/// </summary>
public class MovieTypeService : DataService<MovieType>
{
    public MovieTypeService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MovieTypeService(IDbContextFactory<DataContext> contextFactory, ILogger<MovieTypeService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<MovieType, bool>> IsSameAs(MovieType record) => t => t.Name == record.Name;

    protected override bool IsProtected(int id) => id == 0;

    public MovieType? GetRecordByName(string name)
        => First(t => t.Name == name);

    public Task<MovieType?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(t => t.Name == name, cancellationToken);
}
