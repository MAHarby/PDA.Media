using System.Text.Json.Serialization;
using PDA.Media.Data;

namespace PDA.Media.CommandLine.Configuration;

/// <summary>A folder prefix as stored in the database (a Windows / NAS path) and where it is on this computer.</summary>
/// <param name="From">The start of the stored path, e.g. <c>\\pda-hp-z620\data</c>.</param>
/// <param name="To">Where that is here, e.g. <c>/mnt/nas/data</c>.</param>
public sealed record PathMapping(string From, string To);

/// <summary>
/// pda-media's settings, read from its config file (see <see cref="CliSettingsStore"/>): which database to use and how to log in, and how to show the
/// database's Windows paths on this computer. Without a file: PDA-Main / Media.Master with Windows authentication, paths as stored.
/// </summary>
public sealed class CliSettings
{
    public string Server { get; set; } = DataConnection.DefaultServer;
    public string Database { get; set; } = DataConnection.DefaultDatabase;

    /// <summary>A SQL Server login (for Linux, where Windows authentication isn't available); null for Windows authentication.</summary>
    public string? UserId { get; set; }

    public string? Password { get; set; }

    /// <summary>A full connection string, used instead of Server / Database / UserId / Password when set.</summary>
    public string? ConnectionString { get; set; }

    public List<PathMapping> PathMappings { get; set; } = [];

    [JsonIgnore]
    public bool UsesSqlLogin => string.IsNullOrEmpty(ConnectionString) && !string.IsNullOrEmpty(UserId);

    /// <summary>The connection string to use.</summary>
    public string BuildConnectionString() =>
        !string.IsNullOrWhiteSpace(ConnectionString) ? ConnectionString
        : UsesSqlLogin ? DataConnection.BuildConnectionString(Server, Database, UserId!, Password ?? "", "pda-media")
        : DataConnection.BuildConnectionString(Server, Database);

    /// <summary>"PDA-Main / Media.Master (SQL login media_reader)", for messages (never the password).</summary>
    public string Describe() =>
        !string.IsNullOrWhiteSpace(ConnectionString) ? "the connection string in the config file"
        : $"{Server} / {Database} ({(UsesSqlLogin ? $"SQL login {UserId}" : "Windows authentication")})";
}
