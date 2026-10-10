using Microsoft.EntityFrameworkCore;

namespace PDA.Media.Data.Contexts;

/// <summary>
/// Creates a new, short-lived <see cref="DataContext"/> for each unit of work. Services keep the factory and create
/// a context per call (<c>using var context = contextFactory.CreateDbContext();</c>) rather than holding one context
/// for their lifetime.
/// </summary>
/// <remarks>
/// For use without dependency injection. With DI, register the factory with <c>AddDbContextFactory&lt;DataContext&gt;</c>
/// instead; services only depend on <see cref="IDbContextFactory{TContext}"/>, so either works.
/// </remarks>
public sealed class DataContextFactory : IDbContextFactory<DataContext>
{
    private readonly DbContextOptions<DataContext>? options;

    /// <summary>Uses DataContext's built-in connection string.</summary>
    public DataContextFactory() { }

    public DataContextFactory(string connectionString)
        : this(new DbContextOptionsBuilder<DataContext>().UseSqlServer(connectionString).Options) { }

    public DataContextFactory(DbContextOptions<DataContext> options) => this.options = options;

    public DataContext CreateDbContext() => options is null ? new DataContext() : new DataContext(options);
}
