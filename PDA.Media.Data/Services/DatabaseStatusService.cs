using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Data.Services;

/// <summary>
/// Tells the app whether Media.Master can be reached. The app works without the database (encoding doesn't need
/// it), so it checks in the background and turns data features off while the server is unavailable.
/// </summary>
public class DatabaseStatusService
{
    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<DatabaseStatusService> _logger;

    /// <summary>The server the app connects to, e.g. "PDA-Main".</summary>
    public string Server { get; }

    /// <summary>The database the app uses, e.g. "Media.Master".</summary>
    public string Database { get; }

    public DatabaseStatusService(IDbContextFactory<DataContext> contextFactory, ILogger<DatabaseStatusService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<DatabaseStatusService>.Instance;

        // Creating a context doesn't connect; it only reads the configured connection string.
        using var context = _contextFactory.CreateDbContext();
        var connection = new SqlConnectionStringBuilder(context.Database.GetConnectionString());
        Server = connection.DataSource;
        Database = connection.InitialCatalog;
    }

    /// <summary>True if the database can be opened. Never throws (other than for cancellation).</summary>
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            if (await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Connected to database {Database} on {Server}", Database, Server);
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Database connection check failed");
        }

        _logger.LogWarning("Can't reach database {Database} on {Server}; database features are unavailable", Database, Server);
        return false;
    }
}
