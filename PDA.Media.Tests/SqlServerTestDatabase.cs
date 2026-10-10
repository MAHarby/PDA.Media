using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Tests;

/// <summary>
/// A throwaway SQL Server database created from Schema/Media.Master.sql for one test, and dropped when disposed.
/// The server comes from the PDA_MEDIA_TEST_SQL environment variable (a connection string; any database in it is
/// ignored), e.g. <c>Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true</c>.
/// Without it the test reports Inconclusive. Never point it at the real Media.Master server.
/// </summary>
internal sealed partial class SqlServerTestDatabase : IDisposable
{
    public const string ServerVariable = "PDA_MEDIA_TEST_SQL";

    private readonly string _serverConnectionString;
    private readonly string _name = "PDA_Media_Test_" + Guid.NewGuid().ToString("N");

    public string ConnectionString { get; }
    public DataContextFactory ContextFactory { get; }

    public SqlServerTestDatabase()
    {
        string? server = Environment.GetEnvironmentVariable(ServerVariable);
        if (string.IsNullOrWhiteSpace(server))
            Assert.Inconclusive($"Set {ServerVariable} to a SQL Server connection string to run the database tests.");

        _serverConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _name }.ConnectionString;

        Execute(_serverConnectionString, $"CREATE DATABASE [{_name}]");

        // Run the schema script batch by batch (GO separates batches), without its USE [Media.Master].
        string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schema", "Media.Master.sql"));
        foreach (string batch in GoRegex().Split(script))
        {
            string sql = UseRegex().Replace(batch, "").Trim();
            if (sql.Length > 0) Execute(ConnectionString, sql);
        }

        // The rows the foreign key defaults point at: AlbumType 1 and the Id 0 artist.
        Execute(ConnectionString, """
            INSERT INTO AlbumTypes (Name) VALUES ('Album');
            INSERT INTO Artists (Name) VALUES ('Unknown Artist');
            """);

        ContextFactory = new DataContextFactory(ConnectionString);
    }

    /// <summary>Runs SQL against the test database (for setting up or checking data behind a service's back).</summary>
    public void Execute(string sql) => Execute(ConnectionString, sql);

    public T Scalar<T>(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return (T)command.ExecuteScalar()!;
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqlConnection.ClearAllPools();
        Execute(_serverConnectionString, $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoRegex();

    [GeneratedRegex(@"^\s*USE\s+\[[^\]]+\]\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex UseRegex();
}
