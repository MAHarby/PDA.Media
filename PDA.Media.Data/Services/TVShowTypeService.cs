using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes TVShowType lookup rows (see <see cref="DataService{TEntity}"/>). Deleting one removes the row, and
/// the database moves the TV shows that used it to TV show type 0 (ON DELETE SET DEFAULT);
/// TV show type 0 itself can't be deleted.
/// </summary>
public class TVShowTypeService : DataService<TVShowType>
{
    public TVShowTypeService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public TVShowTypeService(IDbContextFactory<DataContext> contextFactory, ILogger<TVShowTypeService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<TVShowType, bool>> IsSameAs(TVShowType record) => t => t.Name == record.Name;

    protected override bool IsProtected(int id) => id == 0;

    public TVShowType? GetRecordByName(string name)
        => First(t => t.Name == name);

    public Task<TVShowType?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(t => t.Name == name, cancellationToken);
}
