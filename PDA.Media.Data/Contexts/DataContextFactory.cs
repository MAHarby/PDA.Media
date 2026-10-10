using Microsoft.EntityFrameworkCore;

namespace PDA.Media.Data.Contexts;

/// <summary>
/// Creates a new, short-lived <see cref="DataContext"/> for each unit of work. Services keep the factory and create
/// a context per call (<c>using var context = contextFactory.CreateDbContext();</c>) rather than holding one context
/// for their lifetime.
/// </summary>
/// <remarks>
/// The app gets one through <see cref="DataServiceCollectionExtensions.AddMediaData(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, string)"/>;
/// tests and small tools can create one from a connection string.
/// </remarks>
public sealed class DataContextFactory : IDbContextFactory<DataContext>
{
    private readonly DbContextOptions<DataContext> _options;
    private readonly string _auditUser;

    /// <summary>Connects with the app's standard options (see <see cref="DataConnection.Configure"/>).</summary>
    public DataContextFactory(string connectionString, string auditUser = DataContext.DefaultAuditUser)
        : this((DbContextOptions<DataContext>)DataConnection.Configure(new DbContextOptionsBuilder<DataContext>(), connectionString).Options, auditUser) { }

    /// <param name="options">The options every context is created with.</param>
    /// <param name="auditUser">Written to CreatedBy / ModifiedBy when changes are saved.</param>
    public DataContextFactory(DbContextOptions<DataContext> options, string auditUser = DataContext.DefaultAuditUser)
    {
        _options = options;
        _auditUser = auditUser;
    }

    public DataContext CreateDbContext() => new(_options) { AuditUser = _auditUser };
}
