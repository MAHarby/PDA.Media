using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>
/// Scans the root folder of each active media category into the database, as an incremental sync:
/// what is found is added (or restored, if it had been soft-deleted), what changed on disk is updated, and what has gone is soft-deleted.
/// Favourites, notes and external ids stored with a record are kept, because a record is only ever updated in the fields the scan owns.
/// </summary>
/// <remarks>
/// Each category's folders are read completely before anything is written, and nothing is deleted for a category whose root folder can't be reached
/// or that fails part way, so a NAS that is off or busy can't empty the database.
/// Layouts: music <c>Artist/Album/NN - Title.ext</c>, movies <c>Movie (Year)/…</c>, TV <c>Show (Year)/Season NN/… sNNeNN - Title.ext</c> (see <see cref="MediaFileNames"/>).
/// Music (MusicScan), movie (MovieScan) and TV (TVScan) scanning are in the other MediaScanner.*.cs files.
/// </remarks>
public partial class MediaScanner
{
    /// <summary>Hidden and system files and folders are skipped; an unreadable folder throws, which stops that category's scan without deleting anything.</summary>
    private static readonly EnumerationOptions ReadFolder = new() { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = false };

    private readonly MediaCategoryService _categoryService;
    private readonly ArtistService _artistService;
    private readonly AlbumService _albumService;
    private readonly AlbumTypeService _albumTypeService;
    private readonly TrackService _trackService;
    private readonly MovieService _movieService;
    private readonly TVShowService _tvShowService;
    private readonly TVShowEpisodeService _episodeService;
    private readonly MediaCacheService _mediaCacheService;
    private readonly ILogger<MediaScanner> _logger;

    public MediaScanner(MediaCategoryService categoryService, ArtistService artistService, AlbumService albumService, AlbumTypeService albumTypeService,
        TrackService trackService, MovieService movieService, TVShowService tvShowService, TVShowEpisodeService episodeService,
        MediaCacheService mediaCacheService, ILogger<MediaScanner>? logger = null)
    {
        _categoryService = categoryService;
        _artistService = artistService;
        _albumService = albumService;
        _albumTypeService = albumTypeService;
        _trackService = trackService;
        _movieService = movieService;
        _tvShowService = tvShowService;
        _episodeService = episodeService;
        _mediaCacheService = mediaCacheService;
        _logger = logger ?? NullLogger<MediaScanner>.Instance;
    }

    /// <summary>
    /// Step through each active media category and scan its root folder into the database.
    /// </summary>
    /// <param name="clearData">If true, then clear all existing media data before scanning (this also loses favourites, notes and external ids).</param>
    /// <returns>What the scan added, restored, updated and deleted.</returns>
    public MediaScanResult ScanAllMediaCategories(bool clearData = false)
        => ScanAllMediaCategoriesAsync(clearData).GetAwaiter().GetResult();

    /// <inheritdoc cref="ScanAllMediaCategories"/>
    public async Task<MediaScanResult> ScanAllMediaCategoriesAsync(bool clearData = false, CancellationToken cancellationToken = default)
    {
        var result = new MediaScanResult();
        if (clearData) await ClearAllDataAsync(cancellationToken).ConfigureAwait(false);

        List<MediaCategory> categories = await _categoryService.GetActiveRecordsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Scanning {Count} active media categories", categories.Count);

        foreach (MediaCategory category in categories)
        {
            await ScanCategoryAsync(category, result, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Scan finished: {Result}", result);
        return result;
    }

    private async Task ScanCategoryAsync(MediaCategory category, MediaScanResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Media category {Category} ({MediaClass}): {RootFolder}", category.Name, category.MediaClass, category.RootFolder ?? "(no root folder)");

        MediaKind kind = MediaKinds.FromMediaClass(category.MediaClass);
        string? root = category.RootFolder?.Trim().TrimEnd('\\', '/');
        string? skipReason = kind == MediaKind.Unknown ? $"its media class \"{category.MediaClass}\" isn't Music, Movie or TV"
            : string.IsNullOrEmpty(root) ? "it has no root folder"
            : !Directory.Exists(root) ? $"its root folder {root} can't be reached"
            : null;
        if (skipReason != null)
        {
            _logger.LogWarning("Skipped media category {Category}: {Reason}; nothing was changed", category.Name, skipReason);
            result.CategoriesSkipped++;
            return;
        }

        var counts = new MediaScanResult();
        try
        {
            Task scan = kind switch
            {
                MediaKind.Music => ScanMusicAsync(category, root!, counts, cancellationToken),
                MediaKind.Movie => ScanMoviesAsync(root!, counts, cancellationToken),
                _ => ScanTVShowsAsync(root!, counts, cancellationToken),
            };
            await scan.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A folder that couldn't be read, or a database error: stop this category before its deletions, and carry on with the next.
            _logger.LogError(ex, "Scan of media category {Category} stopped: {Message}; nothing was deleted", category.Name, ex.Message);
            result.AddRows(counts);
            result.CategoriesSkipped++;
            return;
        }

        result.AddRows(counts);
        result.CategoriesScanned++;
        _logger.LogInformation("Scanned media category {Category}: {Added} added, {Restored} restored, {Updated} updated, {Unchanged} unchanged, {Deleted} deleted",
            category.Name, counts.Added, counts.Restored, counts.Updated, counts.Unchanged, counts.Deleted);
    }

    // Shared steps.
    // ==================================================================================================

    /// <summary>
    /// Adds a record found on disk, restores it if it had been soft-deleted, or, if it exists, copies the fields the scan owns onto it when they changed.
    /// Marks it as seen so <see cref="DeleteMissingAsync"/> keeps it, counts what happened, and returns the stored record (with its id).
    /// </summary>
    /// <param name="copyScannedFields">Copies the scan-owned fields from the found record (second) onto the stored one (first); returns true if any changed.</param>
    private static async Task<TEntity> SyncAsync<TEntity>(DataService<TEntity> service, TEntity found, Func<TEntity, TEntity, bool> copyScannedFields,
        HashSet<int> seen, MediaScanResult counts, CancellationToken cancellationToken) where TEntity : class, IEntity
    {
        (TEntity record, AddOutcome outcome) = await service.AddOrRestoreRecordAsync(found, cancellationToken).ConfigureAwait(false);
        seen.Add(record.Id);
        if (outcome == AddOutcome.Added)
        {
            counts.Added++;
            return record;
        }

        bool changed = copyScannedFields(record, found);
        if (changed) await service.UpdateRecordAsync(record.Id, record, cancellationToken).ConfigureAwait(false);

        if (outcome == AddOutcome.Restored) counts.Restored++;
        else if (changed) counts.Updated++;
        else counts.Unchanged++;
        return record;
    }

    /// <summary>Soft-deletes the records under the scanned root folder that the scan didn't see.</summary>
    private static async Task DeleteMissingAsync<TEntity>(DataService<TEntity> service, List<TEntity> underRoot, HashSet<int> seen, MediaScanResult counts,
        CancellationToken cancellationToken) where TEntity : class, IEntity
    {
        foreach (TEntity record in underRoot.Where(r => !seen.Contains(r.Id)))
        {
            await service.DeleteRecordAsync(record.Id, cancellationToken).ConfigureAwait(false);
            counts.Deleted++;
        }
    }

    /// <summary>Sets a field to the value found on disk; returns true if it changed.</summary>
    private static bool Set<TValue>(TValue current, TValue found, Action<TValue> set)
    {
        if (EqualityComparer<TValue>.Default.Equals(current, found)) return false;
        set(found);
        return true;
    }

    /// <summary>The folders in <paramref name="folder"/>, by name, without hidden, system and NAS housekeeping folders.</summary>
    private static List<string> SubFolders(string folder) =>
        Directory.EnumerateDirectories(folder, "*", ReadFolder).Where(f => !MediaFileNames.IsIgnoredFolder(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The files directly in <paramref name="folder"/>, by name, with their sizes.</summary>
    private static List<FileInfo> FilesIn(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*", ReadFolder).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Empties the media cache (dbo.ClearMediaCache) so the scan starts from nothing.
    /// Favourites, notes and external ids are lost with it; categories, types and settings are kept.
    /// </summary>
    private async Task ClearAllDataAsync(CancellationToken cancellationToken)
    {
        await _mediaCacheService.ClearMediaCacheAsync(cancellationToken).ConfigureAwait(false);
    }
}
