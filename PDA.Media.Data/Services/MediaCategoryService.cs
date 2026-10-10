using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes media categories (see <see cref="DataService{TEntity}"/>). A category is the same as an existing
/// one when the name matches.
/// </summary>
public class MediaCategoryService : DataService<MediaCategory>
{
    public MediaCategoryService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MediaCategoryService(IDbContextFactory<DataContext> contextFactory, ILogger<MediaCategoryService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<MediaCategory, bool>> IsSameAs(MediaCategory record) => c => c.Name == record.Name;

    public MediaCategory? GetRecordByName(string name)
        => First(c => c.Name == name);

    public Task<MediaCategory?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(c => c.Name == name, cancellationToken);

    /// <summary>The categories switched on (IsActive).</summary>
    public List<MediaCategory> GetActiveRecords()
        => List(q => DefaultOrder(q.Where(c => c.IsActive)));

    public Task<List<MediaCategory>> GetActiveRecordsAsync(CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(c => c.IsActive)), cancellationToken);

    public List<MediaCategory> GetAllRecordsByMediaClass(string mediaClass)
        => List(q => DefaultOrder(q.Where(c => c.MediaClass == mediaClass)));

    public Task<List<MediaCategory>> GetAllRecordsByMediaClassAsync(string mediaClass, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(q.Where(c => c.MediaClass == mediaClass)), cancellationToken);
}
