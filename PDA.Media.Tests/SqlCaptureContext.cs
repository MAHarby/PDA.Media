using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PDA.Media.Data.Contexts;

namespace PDA.Media.Tests;

/// <summary>
/// Creates a <see cref="DataContext"/> that never touches a database: opening the connection is skipped and the
/// first command EF tries to run is captured and then stopped. Tests can check the SQL a save would send.
/// </summary>
internal sealed class SqlCaptureContext
{
    public SqlCaptureInterceptor Interceptor { get; } = new();
    public DataContext Context { get; }

    public SqlCaptureContext()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseSqlServer("Server=not-used;Database=not-used")
            .AddInterceptors(Interceptor)
            .Options;
        Context = new DataContext(options);
    }

    /// <summary>Calls SaveChanges and returns the SQL it would have run.</summary>
    public string Save()
    {
        Assert.ThrowsExactly<DbUpdateException>(() => Context.SaveChanges());
        return Interceptor.CommandText ?? throw new AssertFailedException("SaveChanges ran no command");
    }

    /// <summary>
    /// The column list of the INSERT a save would run, e.g. "[AlbumTypeId], [Description], ...". Only these
    /// columns get a value from the entity; the database default fills any column left out. (Columns can also
    /// appear in the OUTPUT clause, which reads values back, so check this list rather than the whole SQL.)
    /// </summary>
    public string SaveInsertColumns()
    {
        string sql = Save();
        int start = sql.IndexOf("INSERT INTO", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, sql);
        int open = sql.IndexOf('(', start);
        int close = sql.IndexOf(')', open);
        return sql[(open + 1)..close];
    }
}

internal sealed class SqlCaptureInterceptor : DbCommandInterceptor, IDbConnectionInterceptor
{
    public string? CommandText { get; private set; }

    public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => InterceptionResult.Suppress();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        CommandText = command.CommandText;
        throw new InvalidOperationException("SQL captured; not run.");
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        CommandText = command.CommandText;
        throw new InvalidOperationException("SQL captured; not run.");
    }
}
