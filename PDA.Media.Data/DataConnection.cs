using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PDA.Media.Data;

/// <summary>
/// How the app connects to Media.Master: the connection string and the EF options. Used both by
/// <see cref="DataServiceCollectionExtensions.AddMediaData(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, string)"/>
/// and by <see cref="Contexts.DataContextFactory"/>, so the app and the tests connect the same way.
/// </summary>
public static class DataConnection
{
    public const string DefaultServer = "PDA-Main";
    public const string DefaultDatabase = "Media.Master";

    /// <summary>
    /// A Windows-authentication connection string (no password is stored), with a short connect timeout so the
    /// app finds out quickly when the server is off.
    /// </summary>
    public static string BuildConnectionString(string server, string database) => new SqlConnectionStringBuilder
    {
        DataSource = server,
        InitialCatalog = database,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
        ConnectTimeout = 5,
        ApplicationName = "PDA.Media",
    }.ConnectionString;

    /// <summary>
    /// SQL Server with retries for brief network drops. Because of the retries, a transaction started in code must
    /// run inside <c>context.Database.CreateExecutionStrategy().Execute(...)</c>.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null));
}
