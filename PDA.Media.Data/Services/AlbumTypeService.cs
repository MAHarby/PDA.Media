using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes AlbumType lookup rows (see <see cref="DataService{TEntity}"/>). Deleting one removes the row, and
/// the database moves the albums that used it to album type 1 (ON DELETE SET DEFAULT);
/// album type 1 itself can't be deleted.
/// </summary>
public class AlbumTypeService : DataService<AlbumType>
{
    public AlbumTypeService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public AlbumTypeService(IDbContextFactory<DataContext> contextFactory, ILogger<AlbumTypeService>? logger = null)
        : base(contextFactory, logger) { }

    protected override Expression<Func<AlbumType, bool>> IsSameAs(AlbumType record) => t => t.Name == record.Name;

    protected override bool IsProtected(int id) => id == 1;

    public AlbumType? GetRecordByName(string name)
        => First(t => t.Name == name);

    public Task<AlbumType?> GetRecordByNameAsync(string name, CancellationToken cancellationToken = default)
        => FirstAsync(t => t.Name == name, cancellationToken);
}
