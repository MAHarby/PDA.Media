using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Services;

namespace PDA.Media.Data;

/// <summary>
/// Registers the data layer with dependency injection: one <see cref="IDbContextFactory{DataContext}"/> and the data
/// services, all singletons (each service call creates its own short-lived DataContext).
/// </summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddMediaData(this IServiceCollection services, string connectionString,
        string auditUser = DataContext.DefaultAuditUser)
        => services.AddMediaData(_ => connectionString, auditUser);

    /// <param name="connectionString">
    /// Called once, the first time the data layer is used (so it can read settings registered in the container).
    /// </param>
    /// <param name="auditUser">Written to CreatedBy / ModifiedBy when changes are saved.</param>
    public static IServiceCollection AddMediaData(this IServiceCollection services,
        Func<IServiceProvider, string> connectionString, string auditUser = DataContext.DefaultAuditUser)
    {
        services.AddSingleton<IDbContextFactory<DataContext>>(provider =>
        {
            var builder = new DbContextOptionsBuilder<DataContext>();
            DataConnection.Configure(builder, connectionString(provider));

            // EF logs through the app's logging. Raise the "Microsoft.EntityFrameworkCore" category to Warning there:
            // it logs every SQL command, and each connection retry, at Information.
            if (provider.GetService<ILoggerFactory>() is { } loggerFactory) builder.UseLoggerFactory(loggerFactory);

            return new DataContextFactory(builder.Options, auditUser);
        });

        services.AddSingleton<DatabaseStatusService>();
        services.AddSingleton<AlbumService>();
        return services;
    }
}
