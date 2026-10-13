using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// The database side of renaming a media item on disk: the row's new name, and the new folders and file names of every row (of any media table) under
/// the folders that were renamed. Everything is saved in one SaveChanges, so either all of it is stored or none of it.
/// </summary>
/// <param name="Item">The row being renamed (its type and id are used).</param>
/// <param name="NewName">Its new Name (for a TV show, without the year).</param>
/// <param name="NewReleaseYear">A TV show's new release year; null to keep it.</param>
/// <param name="FolderMoves">Folders renamed on disk: old full path → new full path.</param>
/// <param name="FileRenames">Files renamed on disk: old full path (in its old folder) → new file name (in the same folder).</param>
public sealed record MediaRenameChanges(
    IEntity Item,
    string NewName,
    int? NewReleaseYear,
    IReadOnlyDictionary<string, string> FolderMoves,
    IReadOnlyDictionary<string, string> FileRenames);

/// <summary>
/// Updates the media cache after files and folders have been renamed on disk (see <see cref="MediaRenameChanges"/>), so the next scan finds the same rows,
/// with their ids, favourites and notes. Soft-deleted rows under the folders are moved too. Each method creates its own short-lived <see cref="DataContext"/>.
/// </summary>
public class MediaPathService
{
    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<MediaPathService> _logger;

    public MediaPathService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MediaPathService(IDbContextFactory<DataContext> contextFactory, ILogger<MediaPathService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<MediaPathService>.Instance;
    }

    /// <summary>Stores a rename; returns the number of rows changed. Throws InvalidOperationException if the renamed row isn't there.</summary>
    public int ApplyRename(MediaRenameChanges changes) => ApplyRenameCoreAsync(changes, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="ApplyRename"/>
    public Task<int> ApplyRenameAsync(MediaRenameChanges changes, CancellationToken cancellationToken = default)
        => ApplyRenameCoreAsync(changes, true, cancellationToken);

    private async Task<int> ApplyRenameCoreAsync(MediaRenameChanges changes, bool isAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        await using DataContext context = isAsync
            ? await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)
            : _contextFactory.CreateDbContext();

        // Every row in a folder that moved, or that holds a renamed file (each table, deleted rows included), tracked for the update.
        List<string> roots = changes.FolderMoves.Keys
            .Concat(changes.FileRenames.Keys.Select(f => Path.GetDirectoryName(f)!))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rows = new List<IEntity>();
        if (roots.Count > 0)
        {
            rows.AddRange(await LoadUnderAsync<Artist>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
            rows.AddRange(await LoadUnderAsync<Album>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
            rows.AddRange(await LoadUnderAsync<Track>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
            rows.AddRange(await LoadUnderAsync<Movie>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
            rows.AddRange(await LoadUnderAsync<TVShow>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
            rows.AddRange(await LoadUnderAsync<TVShowEpisode>(context, roots, isAsync, cancellationToken).ConfigureAwait(false));
        }

        IEntity item = rows.FirstOrDefault(r => r.GetType() == changes.Item.GetType() && r.Id == changes.Item.Id)
            ?? await FindAsync(context, changes.Item, isAsync, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{changes.Item.GetType().Name} {changes.Item.Id} isn't in the database.");
        if (!rows.Contains(item)) rows.Add(item);

        foreach (IEntity row in rows)
        {
            string? folder = Paths.Folder(row);
            if (folder != null)
            {
                // File names first, matched by their full path in the old folder; then the folder itself.
                if (Paths.FileName(row) is { } file && changes.FileRenames.TryGetValue(Path.Combine(folder, file), out string? newFile)) Paths.SetFileName(row, newFile);
                if (Paths.CoverArt(row) is { } cover && changes.FileRenames.TryGetValue(Path.Combine(folder, cover), out string? newCover)) Paths.SetCoverArt(row, newCover);
                string? moved = changes.FolderMoves
                    .OrderByDescending(m => m.Key.Length)
                    .Select(m => FolderQueries.Move(folder, m.Key, m.Value))
                    .FirstOrDefault(f => f != null);
                if (moved != null) Paths.SetFolder(row, moved);
            }
        }

        item.Name = changes.NewName;
        if (item is TVShow show && changes.NewReleaseYear is int year) show.ReleaseYear = year;

        int saved = isAsync ? await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false) : context.SaveChanges();
        _logger.LogInformation("Renamed {Entity} {Id} to {Name}: {Rows} rows updated", item.GetType().Name, item.Id, changes.NewName, saved);
        return saved;
    }

    private static async Task<List<T>> LoadUnderAsync<T>(DataContext context, List<string> roots, bool isAsync, CancellationToken cancellationToken) where T : class
    {
        IQueryable<T> query = context.Set<T>().IgnoreQueryFilters([DataContext.SoftDeleteFilter]).Where(FolderQueries.UnderAny<T>("Folder", roots));
        return isAsync ? await query.ToListAsync(cancellationToken).ConfigureAwait(false) : query.ToList();
    }

    private static async Task<IEntity?> FindAsync(DataContext context, IEntity item, bool isAsync, CancellationToken cancellationToken) => item switch
    {
        Artist => await Find<Artist>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        Album => await Find<Album>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        Track => await Find<Track>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        Movie => await Find<Movie>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        TVShow => await Find<TVShow>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        TVShowEpisode => await Find<TVShowEpisode>(context, item.Id, isAsync, cancellationToken).ConfigureAwait(false),
        _ => throw new NotSupportedException($"{item.GetType().Name} rows can't be renamed."),
    };

    private static async Task<T?> Find<T>(DataContext context, int id, bool isAsync, CancellationToken cancellationToken) where T : class, IEntity
    {
        IQueryable<T> query = context.Set<T>().IgnoreQueryFilters([DataContext.SoftDeleteFilter]).Where(e => e.Id == id);
        return isAsync ? await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) : query.FirstOrDefault();
    }

    /// <summary>The path fields media rows share (no interface has them).</summary>
    private static class Paths
    {
        public static string? Folder(IEntity row) => row switch
        {
            Artist a => a.Folder,
            Album a => a.Folder,
            Track t => t.Folder,
            Movie m => m.Folder,
            TVShow s => s.Folder,
            TVShowEpisode e => e.Folder,
            _ => null,
        };

        public static void SetFolder(IEntity row, string folder)
        {
            switch (row)
            {
                case Artist a: a.Folder = folder; break;
                case Album a: a.Folder = folder; break;
                case Track t: t.Folder = folder; break;
                case Movie m: m.Folder = folder; break;
                case TVShow s: s.Folder = folder; break;
                case TVShowEpisode e: e.Folder = folder; break;
            }
        }

        public static string? FileName(IEntity row) => row switch
        {
            Album a => a.OriginalFilename,
            Track t => t.OriginalFilename,
            Movie m => m.OriginalFilename,
            TVShowEpisode e => e.OriginalFilename,
            _ => null,
        };

        public static void SetFileName(IEntity row, string fileName)
        {
            switch (row)
            {
                case Album a: a.OriginalFilename = fileName; break;
                case Track t: t.OriginalFilename = fileName; break;
                case Movie m: m.OriginalFilename = fileName; break;
                case TVShowEpisode e: e.OriginalFilename = fileName; break;
            }
        }

        public static string? CoverArt(IEntity row) => row switch
        {
            Album a => a.CoverArtFilename,
            Track t => t.CoverArtFilename,
            Movie m => m.CoverArtFilename,
            TVShow s => s.CoverArtFilename,
            TVShowEpisode e => e.CoverArtFilename,
            _ => null,
        };

        public static void SetCoverArt(IEntity row, string fileName)
        {
            switch (row)
            {
                case Album a: a.CoverArtFilename = fileName; break;
                case Track t: t.CoverArtFilename = fileName; break;
                case Movie m: m.CoverArtFilename = fileName; break;
                case TVShow s: s.CoverArtFilename = fileName; break;
                case TVShowEpisode e: e.CoverArtFilename = fileName; break;
            }
        }
    }
}
