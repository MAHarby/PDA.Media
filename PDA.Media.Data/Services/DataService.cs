using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// The record operations every data service shares: get, list, search by name, add, update and delete, each with
/// a sync and an async method. A service derives from this and adds what is specific to its entity: which existing
/// record counts as the same one (<see cref="IsSameAs"/>), the list order, rows that can't be deleted, and its own
/// queries (built with <see cref="List"/> / <see cref="First"/> and their async versions).
/// </summary>
/// <remarks>
/// Each method creates its own short-lived <see cref="DataContext"/>, so a service is safe to keep for the life of the
/// app and to call from more than one place at a time. Records returned are detached; reads don't track them.
/// Soft-deleted rows are hidden by the DataContext query filter, so no method returns them. Audit fields are set by
/// DataContext when changes are saved.
/// </remarks>
public abstract class DataService<TEntity> : IDataService<TEntity> where TEntity : class, IEntity
{
    private static readonly string EntityName = typeof(TEntity).Name;

    protected IDbContextFactory<DataContext> ContextFactory { get; }
    protected ILogger Logger { get; }

    protected DataService(IDbContextFactory<DataContext> contextFactory, ILogger? logger)
    {
        ContextFactory = contextFactory;
        Logger = logger ?? NullLogger.Instance;
    }

    // What derived services decide.
    // ==================================================================================================

    /// <summary>
    /// Matches an existing record that is the same as <paramref name="record"/> (its natural key, e.g. an album's
    /// artist and name). AddRecord returns that record instead of adding a duplicate.
    /// </summary>
    protected abstract Expression<Func<TEntity, bool>> IsSameAs(TEntity record);

    /// <summary>The order of GetAllRecords and search results. By name unless a service says otherwise.</summary>
    protected virtual IOrderedQueryable<TEntity> DefaultOrder(IQueryable<TEntity> query) => query.OrderBy(e => e.Name);

    /// <summary>
    /// Rows that must never be deleted, such as the Id 0 "unknown" row that foreign keys fall back to
    /// (ON DELETE SET DEFAULT). DeleteRecord throws for them.
    /// </summary>
    protected virtual bool IsProtected(int id) => false;

    // Query helpers. Each runs in its own context without tracking.
    // ==================================================================================================

    /// <summary>Runs <paramref name="query"/> over the entity's table and returns the results.</summary>
    protected List<TEntity> List(Func<IQueryable<TEntity>, IQueryable<TEntity>> query)
    {
        using var context = ContextFactory.CreateDbContext();
        return query(context.Set<TEntity>().AsNoTracking()).ToList();
    }

    protected async Task<List<TEntity>> ListAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await query(context.Set<TEntity>().AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The first record matching <paramref name="predicate"/>, or null.</summary>
    protected TEntity? First(Expression<Func<TEntity, bool>> predicate)
    {
        using var context = ContextFactory.CreateDbContext();
        return context.Set<TEntity>().AsNoTracking().FirstOrDefault(predicate);
    }

    protected async Task<TEntity?> FirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records whose name matches <paramref name="searchTerm"/>: without a '*' the name only has to contain the term;
    /// '*' is a wildcard for any text ("Abba*" starts with Abba, "*Gold" ends with Gold, "Abba*Gold" both). An empty
    /// term or "*" matches everything. Matching ignores case (database collation).
    /// </summary>
    protected static IQueryable<TEntity> WhereNameMatches(IQueryable<TEntity> query, string? searchTerm)
    {
        string? pattern = ToLikePattern(searchTerm);
        return pattern == null ? query : query.Where(e => EF.Functions.Like(e.Name, pattern));
    }

    /// <summary>
    /// Turns a search term into a SQL LIKE pattern, or null to match everything. LIKE's own wildcards (%, _ and [)
    /// in the term are escaped so they match literally; '*' becomes '%'.
    /// </summary>
    internal static string? ToLikePattern(string? searchTerm)
    {
        string term = searchTerm?.Trim() ?? "";
        if (term.Replace("*", "").Length == 0) return null;

        string escaped = term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        return term.Contains('*') ? escaped.Replace('*', '%') : $"%{escaped}%";
    }

    // Reads.
    // ==================================================================================================

    public TEntity? GetRecordById(int id) => First(e => e.Id == id);

    public Task<TEntity?> GetRecordByIdAsync(int id, CancellationToken cancellationToken = default)
        => FirstAsync(e => e.Id == id, cancellationToken);

    public List<TEntity> GetAllRecords() => List(DefaultOrder);

    public Task<List<TEntity>> GetAllRecordsAsync(CancellationToken cancellationToken = default)
        => ListAsync(DefaultOrder, cancellationToken);

    /// <summary>Records whose name matches <paramref name="searchTerm"/> (see <see cref="WhereNameMatches"/>).</summary>
    public List<TEntity> GetAllRecords(string searchTerm) => List(q => DefaultOrder(WhereNameMatches(q, searchTerm)));

    public Task<List<TEntity>> GetAllRecordsAsync(string searchTerm, CancellationToken cancellationToken = default)
        => ListAsync(q => DefaultOrder(WhereNameMatches(q, searchTerm)), cancellationToken);

    // Writes.
    // ==================================================================================================

    /// <summary>
    /// Adds <paramref name="record"/>, or returns the existing record that is the same (see <see cref="IsSameAs"/>).
    /// </summary>
    public TEntity AddRecord(TEntity record)
    {
        ArgumentNullException.ThrowIfNull(record);
        TEntity? existing = First(IsSameAs(record));
        if (existing != null) return existing;

        using var context = ContextFactory.CreateDbContext();
        context.Set<TEntity>().Add(record);
        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Someone else added the same record between the check and the save (a unique index rejected ours).
            TEntity? added = First(IsSameAs(record));
            if (added == null) throw;
            return added;
        }

        Logger.LogInformation("Added {Entity} {Id} {Name}", EntityName, record.Id, record.Name);
        return record;
    }

    public async Task<TEntity> AddRecordAsync(TEntity record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        TEntity? existing = await FirstAsync(IsSameAs(record), cancellationToken).ConfigureAwait(false);
        if (existing != null) return existing;

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Set<TEntity>().Add(record);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Someone else added the same record between the check and the save (a unique index rejected ours).
            TEntity? added = await FirstAsync(IsSameAs(record), cancellationToken).ConfigureAwait(false);
            if (added == null) throw;
            return added;
        }

        Logger.LogInformation("Added {Entity} {Id} {Name}", EntityName, record.Id, record.Name);
        return record;
    }

    /// <summary>
    /// Copies every column of <paramref name="record"/> onto record <paramref name="id"/> and saves it. Returns the
    /// saved record, or null if there is no record with that id. The key and Created fields are kept and the Modified
    /// fields are set by DataContext, so <paramref name="record"/> may be a new object with no id or audit values.
    /// </summary>
    public TEntity? UpdateRecord(int id, TEntity record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var context = ContextFactory.CreateDbContext();

        TEntity? entity = context.Set<TEntity>().FirstOrDefault(e => e.Id == id);
        if (entity == null) return null;

        CopyValues(context, entity, record);
        context.SaveChanges();

        Logger.LogInformation("Updated {Entity} {Id} {Name}", EntityName, entity.Id, entity.Name);
        return entity;
    }

    public async Task<TEntity?> UpdateRecordAsync(int id, TEntity record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        TEntity? entity = await context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity == null) return null;

        CopyValues(context, entity, record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.LogInformation("Updated {Entity} {Id} {Name}", EntityName, entity.Id, entity.Name);
        return entity;
    }

    /// <summary>
    /// Copies all of record's column values onto the tracked entity, so a column added later is updated too. EF only
    /// saves the columns that changed.
    /// </summary>
    private static void CopyValues(DataContext context, TEntity entity, TEntity record)
    {
        var values = context.Entry(record).CurrentValues.Clone();
        values[nameof(IEntity.Id)] = entity.Id;
        if (entity is IAuditable audited)
        {
            values[nameof(IAuditable.CreatedOn)] = audited.CreatedOn;
            values[nameof(IAuditable.CreatedBy)] = audited.CreatedBy;
        }
        context.Entry(entity).CurrentValues.SetValues(values);
    }

    /// <summary>
    /// Deletes record <paramref name="id"/>: a soft delete (IsDeleted) for entities that have it, otherwise the row is
    /// removed. Returns false if there is no such record; throws for a protected row (see <see cref="IsProtected"/>).
    /// </summary>
    public bool DeleteRecord(int id)
    {
        ThrowIfProtected(id);
        using var context = ContextFactory.CreateDbContext();

        TEntity? entity = context.Set<TEntity>().FirstOrDefault(e => e.Id == id);
        if (entity == null) return false;

        Delete(context, entity);
        context.SaveChanges();

        Logger.LogInformation("Deleted {Entity} {Id} {Name}", EntityName, entity.Id, entity.Name);
        return true;
    }

    public async Task<bool> DeleteRecordAsync(int id, CancellationToken cancellationToken = default)
    {
        ThrowIfProtected(id);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        TEntity? entity = await context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity == null) return false;

        Delete(context, entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.LogInformation("Deleted {Entity} {Id} {Name}", EntityName, entity.Id, entity.Name);
        return true;
    }

    public bool DeleteRecord(TEntity record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return DeleteRecord(record.Id);
    }

    public Task<bool> DeleteRecordAsync(TEntity record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return DeleteRecordAsync(record.Id, cancellationToken);
    }

    private static void Delete(DataContext context, TEntity entity)
    {
        if (entity is ISoftDeletable softDeletable) softDeletable.IsDeleted = true;
        else context.Set<TEntity>().Remove(entity);
    }

    private void ThrowIfProtected(int id)
    {
        if (IsProtected(id))
            throw new InvalidOperationException($"{EntityName} {id} can't be deleted: other records fall back to it.");
    }
}
