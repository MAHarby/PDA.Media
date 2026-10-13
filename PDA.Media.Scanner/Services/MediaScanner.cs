using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>
/// Scans the root folders of media categories into the database, as an incremental sync:
/// what is found is added (or restored, if it had been soft-deleted), what changed on disk is updated, and what has gone is soft-deleted.
/// Favourites, notes and external ids stored with a record are kept, because a record is only ever updated in the fields the scan owns.
/// </summary>
/// <remarks>
/// A scan runs in two phases: every category's folders are read first (so the number of items to save is known for the progress report), then each
/// category is saved. Nothing is deleted for a category whose root folder can't be reached or that fails part way, so a NAS that is off or busy can't
/// empty the database; <see cref="MediaScanOptions.MaxDeletePercent"/> also holds back deletions that look too large.
/// Layouts: music <c>Artist/Album/NN - Title.ext</c>, movies <c>Source/…/Movie/…</c>, TV <c>Show (Year)/Season NN/… sNNeNN - Title.ext</c> (see <see cref="MediaFileNames"/>).
/// Music, movie and TV scanning are in the other MediaScanner.*.cs files.
/// Changes to single rows go to the progress report only; the logger gets a line per category.
/// </remarks>
public partial class MediaScanner
{
    /// <summary>Settings table key prefix for a category's last scan (followed by the category id); the value is a <see cref="MediaCategoryScanResult"/> as JSON.</summary>
    public const string LastScanSettingPrefix = "MediaScanner.LastScan.";

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
    private readonly SettingService _settingService;
    private readonly ILogger<MediaScanner> _logger;
    private int _isScanning;

    public MediaScanner(MediaCategoryService categoryService, ArtistService artistService, AlbumService albumService, AlbumTypeService albumTypeService,
        TrackService trackService, MovieService movieService, TVShowService tvShowService, TVShowEpisodeService episodeService,
        MediaCacheService mediaCacheService, SettingService settingService, ILogger<MediaScanner>? logger = null)
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
        _settingService = settingService;
        _logger = logger ?? NullLogger<MediaScanner>.Instance;
    }

    /// <summary>True while a scan is running (only one can run at a time).</summary>
    /// <remarks>A rename (MediaRenameService) holds the same flag while it runs, so scans and renames never overlap.</remarks>
    public bool IsScanning => Volatile.Read(ref _isScanning) == 1;

    /// <summary>
    /// Takes the scanner's busy flag for something that mustn't run during a scan (a rename); null if a scan or another rename is running.
    /// Dispose the result to release it.
    /// </summary>
    internal IDisposable? TryReserve() => Interlocked.Exchange(ref _isScanning, 1) == 1 ? null : new Reservation(this);

    private sealed class Reservation(MediaScanner scanner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Volatile.Write(ref scanner._isScanning, 0);
        }
    }

    // Categories.
    // ==================================================================================================

    /// <summary>Every media category (active or not), with whether it can be scanned and how its last scan went.</summary>
    public List<MediaScanCategory> GetCategories() => GetCategoriesAsync().GetAwaiter().GetResult();

    /// <inheritdoc cref="GetCategories"/>
    public async Task<List<MediaScanCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        List<MediaCategory> categories = await _categoryService.GetAllRecordsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> settings = await _settingService.GetAllValuesAsync(cancellationToken).ConfigureAwait(false);
        return categories.Select(c => new MediaScanCategory(c, MediaKinds.FromMediaClass(c.MediaClass), CannotScanReason(c), ReadLastScan(settings, c.Id))).ToList();
    }

    /// <summary>Why a category can't be scanned (unknown media class, no root folder), or null if it can. Whether the root folder exists isn't checked here.</summary>
    public static string? CannotScanReason(MediaCategory category) =>
        MediaKinds.FromMediaClass(category.MediaClass) == MediaKind.Unknown ? $"its media class \"{category.MediaClass}\" isn't Music, Movie or TV"
        : string.IsNullOrEmpty(RootFolderOf(category)) ? "it has no root folder"
        : null;

    private static string? RootFolderOf(MediaCategory category) => category.RootFolder?.Trim().TrimEnd('\\', '/');

    private MediaCategoryScanResult? ReadLastScan(Dictionary<string, string> settings, int categoryId)
    {
        if (!settings.TryGetValue(LastScanSettingPrefix + categoryId, out string? json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MediaCategoryScanResult>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Ignored the last scan stored for media category {CategoryId}: {Message}", categoryId, ex.Message);
            return null;
        }
    }

    // Scanning.
    // ==================================================================================================

    /// <summary>
    /// Step through each active media category and scan its root folder into the database (no deletion limit).
    /// </summary>
    /// <param name="clearData">If true, then clear all existing media data before saving (this also loses favourites, notes and external ids).</param>
    /// <returns>What the scan added, restored, updated and deleted.</returns>
    public MediaScanResult ScanAllMediaCategories(bool clearData = false)
        => ScanAllMediaCategoriesAsync(clearData).GetAwaiter().GetResult();

    /// <inheritdoc cref="ScanAllMediaCategories"/>
    public async Task<MediaScanResult> ScanAllMediaCategoriesAsync(bool clearData = false, CancellationToken cancellationToken = default)
    {
        List<MediaCategory> categories = await _categoryService.GetActiveRecordsAsync(cancellationToken).ConfigureAwait(false);
        return await ScanMediaCategoriesAsync(categories.Select(c => c.Id), new MediaScanOptions { ClearData = clearData }, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scans the media categories with the given ids (active or not) into the database.
    /// All their folders are read first, then each category is saved; <paramref name="progress"/> gets a report for each folder read, each item saved,
    /// each row changed and each message.
    /// Throws InvalidOperationException if a scan is already running, or if <see cref="MediaScanOptions.ClearData"/> is set without every active, scannable category.
    /// </summary>
    /// <returns>What the scan did (or, for a dry run, would have done), with a result per category.</returns>
    public MediaScanResult ScanMediaCategories(IEnumerable<int> categoryIds, MediaScanOptions? options = null, IProgress<MediaScanProgress>? progress = null)
        => ScanMediaCategoriesAsync(categoryIds, options, progress).GetAwaiter().GetResult();

    /// <inheritdoc cref="ScanMediaCategories"/>
    public async Task<MediaScanResult> ScanMediaCategoriesAsync(IEnumerable<int> categoryIds, MediaScanOptions? options = null,
        IProgress<MediaScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new MediaScanOptions();
        HashSet<int> ids = categoryIds.ToHashSet();
        if (Interlocked.Exchange(ref _isScanning, 1) == 1) throw new InvalidOperationException("A media scan (or a rename) is already running.");
        try
        {
            List<MediaCategory> all = await _categoryService.GetAllRecordsAsync(cancellationToken).ConfigureAwait(false);
            bool clearData = options.ClearData && !options.DryRun;
            if (clearData)
            {
                // The media cache is shared by every category: clearing it while scanning only some of them would lose the rest.
                List<string> notSelected = all.Where(c => c.IsActive && CannotScanReason(c) == null && !ids.Contains(c.Id)).Select(c => c.Name).ToList();
                if (notSelected.Count > 0)
                {
                    throw new InvalidOperationException($"Clearing the media cache needs every active category scanned; not selected: {string.Join(", ", notSelected)}.");
                }
            }

            var run = new ScanRun(options, progress, cancellationToken)
            {
                MusicRoots = all.Where(c => MediaKinds.FromMediaClass(c.MediaClass) == MediaKind.Music && CannotScanReason(c) == null).Select(c => RootFolderOf(c)!).ToList(),
            };
            var result = new MediaScanResult { IsDryRun = options.DryRun };
            List<MediaCategory> categories = all.Where(c => ids.Contains(c.Id)).ToList();
            Note(run, LogLevel.Information, null, options.DryRun ? "Dry run of {Count} media categories: nothing will be saved" : "Scanning {Count} media categories",
                categories.Count);

            // Phase 1: read every category's folders.
            var plans = new List<CategoryPlan>();
            foreach (MediaCategory category in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (CategoryPlan? plan, MediaCategoryScanResult? notRead) = ReadCategory(category, run);
                if (plan != null) plans.Add(plan);
                else await FinishCategoryAsync(run, result, notRead!).ConfigureAwait(false);
            }

            if (clearData)
            {
                if (plans.Count < categories.Count)
                {
                    Note(run, LogLevel.Warning, null, "The media cache wasn't cleared, because not every category could be read");
                }
                else
                {
                    await _mediaCacheService.ClearMediaCacheAsync(cancellationToken).ConfigureAwait(false);
                    Note(run, LogLevel.Information, null, "Cleared the media cache");
                }
            }

            // Phase 2: save each category.
            run.Phase = MediaScanPhase.Saving;
            run.ItemsTotal = plans.Sum(p => p.ItemCount);
            run.Report();
            foreach (CategoryPlan plan in plans)
            {
                await SaveCategoryAsync(plan, run, result).ConfigureAwait(false);
            }

            run.Phase = MediaScanPhase.Finished;
            run.CategoryName = null;
            Note(run, LogLevel.Information, null, "Scan finished: {Result}", result);
            return result;
        }
        finally
        {
            Volatile.Write(ref _isScanning, 0);
        }
    }

    /// <summary>Reads a category's folders into a plan for saving it, or returns why it was skipped or failed (nothing is changed for it).</summary>
    private (CategoryPlan? Plan, MediaCategoryScanResult? NotRead) ReadCategory(MediaCategory category, ScanRun run)
    {
        run.CategoryName = category.Name;
        Note(run, LogLevel.Information, null, "Reading media category {Category} ({MediaClass}): {RootFolder}", category.Name, category.MediaClass,
            category.RootFolder ?? "(no root folder)");

        string? root = RootFolderOf(category);
        string? skipReason = CannotScanReason(category) ?? (!Directory.Exists(root) ? $"its root folder {root} can't be reached" : null);
        if (skipReason != null)
        {
            Note(run, LogLevel.Warning, null, "Skipped media category {Category}: {Reason}; nothing was changed", category.Name, skipReason);
            return (null, NewCategoryResult(category, MediaCategoryScanStatus.Skipped, skipReason));
        }

        try
        {
            CategoryPlan plan = MediaKinds.FromMediaClass(category.MediaClass) switch
            {
                MediaKind.Music => ReadMusic(category, root!, run),
                MediaKind.Movie => ReadMovies(category, root!, run),
                _ => ReadTVShows(category, root!, run),
            };
            return (plan, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A folder that couldn't be read: skip this category, and carry on with the next.
            string message = ex.GetBaseException().Message;
            Note(run, LogLevel.Error, ex, "Reading media category {Category} stopped: {Message}; nothing was changed", category.Name, message);
            return (null, NewCategoryResult(category, MediaCategoryScanStatus.Failed, message));
        }
    }

    private async Task SaveCategoryAsync(CategoryPlan plan, ScanRun run, MediaScanResult result)
    {
        MediaCategory category = plan.Category;
        MediaCategoryScanResult counts = NewCategoryResult(category, MediaCategoryScanStatus.Scanned, null);
        int itemsDoneAfter = run.ItemsDone + plan.ItemCount;
        run.StartCategory(category.Name, counts);
        try
        {
            await plan.SaveAsync(run).ConfigureAwait(false);
            await ApplyDeletionsAsync(run).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A database error: stop this category before its deletions, and carry on with the next.
            // EF's DbUpdateException only says "see the inner exception", so report the innermost message.
            string message = ex.GetBaseException().Message;
            Note(run, LogLevel.Error, ex, "Saving media category {Category} stopped: {Message}; nothing was deleted", category.Name, message);
            counts.Status = MediaCategoryScanStatus.Failed;
            counts.Message = message;
            run.ItemsDone = itemsDoneAfter;
            await FinishCategoryAsync(run, result, counts).ConfigureAwait(false);
            return;
        }

        Note(run, LogLevel.Information, null, "{Action} media category {Category}: {Added} added, {Restored} restored, {Updated} updated, {Unchanged} unchanged, {Deleted} deleted",
            run.Options.DryRun ? "Dry run of" : "Scanned", category.Name, counts.Added, counts.Restored, counts.Updated, counts.Unchanged, counts.Deleted);
        await FinishCategoryAsync(run, result, counts).ConfigureAwait(false);
    }

    /// <summary>Soft-deletes the rows the category's scan didn't see, unless there are more than the deletion limit allows.</summary>
    private async Task ApplyDeletionsAsync(ScanRun run)
    {
        MediaCategoryScanResult counts = run.Counts;
        int count = run.Deletions.Count;
        if (count == 0) return;

        if (run.Options.MaxDeletePercent is int maxPercent && run.RowsBefore > 0 && count * 100L > (long)maxPercent * run.RowsBefore)
        {
            counts.Status = MediaCategoryScanStatus.DeletionsHeld;
            counts.DeletionsHeld = count;
            counts.Message = $"{count} of {run.RowsBefore} rows ({count * 100.0 / run.RowsBefore:0}%) would have been deleted, over the {maxPercent}% limit";
            Note(run, LogLevel.Warning, null, "Media category {Category}: {Count} of {Rows} rows would have been deleted, over the {Limit}% limit; nothing was deleted",
                counts.CategoryName, count, run.RowsBefore, maxPercent);
            return;
        }

        foreach (PendingDeletion deletion in run.Deletions)
        {
            run.Token.ThrowIfCancellationRequested();
            if (!run.Options.DryRun) await deletion.DeleteAsync(run.Token).ConfigureAwait(false);
            counts.Deleted++;
            run.Report(change: deletion.Change);
        }
    }

    private async Task FinishCategoryAsync(ScanRun run, MediaScanResult result, MediaCategoryScanResult counts)
    {
        counts.FinishedOn = DateTime.Now;
        result.Categories.Add(counts);
        result.AddRows(counts);
        if (counts.Status is MediaCategoryScanStatus.Scanned or MediaCategoryScanStatus.DeletionsHeld) result.CategoriesScanned++;
        else result.CategoriesSkipped++;
        run.Report(categoryResult: counts);
        if (run.Options.DryRun) return;

        try
        {
            // Settings values are nvarchar(500): keep the message short.
            if (counts.Message is { Length: > 200 }) counts.Message = counts.Message[..200] + "…";
            await _settingService.SetValueAsync(LastScanSettingPrefix + counts.CategoryId, JsonSerializer.Serialize(counts), run.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Couldn't save the last scan of media category {Category}: {Message}", counts.CategoryName, ex.Message);
        }
    }

    private static MediaCategoryScanResult NewCategoryResult(MediaCategory category, MediaCategoryScanStatus status, string? message) =>
        new() { CategoryId = category.Id, CategoryName = category.Name, Status = status, Message = message };

    /// <summary>Writes a message to the logger and to the progress report.</summary>
    private void Note(ScanRun run, LogLevel level, Exception? exception, string messageTemplate, params object?[] args)
    {
#pragma warning disable CA2254 // The template is passed on from the callers' constant strings.
        _logger.Log(level, exception, messageTemplate, args);
#pragma warning restore CA2254
        run.Report(message: FormatTemplate(messageTemplate, args), level: level);
    }

    /// <summary>Fills a message template's {Placeholders} with the arguments in order, as the logger does.</summary>
    internal static string FormatTemplate(string messageTemplate, object?[] args)
    {
        int index = 0;
        return TemplatePlaceholder().Replace(messageTemplate, m => index < args.Length ? Convert.ToString(args[index++], CultureInfo.CurrentCulture) ?? "" : m.Value);
    }

    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex TemplatePlaceholder();

    // Shared steps.
    // ==================================================================================================

    /// <summary>A category whose folders have been read: how many items it found, and how to save them.</summary>
    private sealed record CategoryPlan(MediaCategory Category, int ItemCount, Func<ScanRun, Task> SaveAsync);

    /// <summary>A row to soft-delete once the category's scan has finished (and the deletion limit allows it).</summary>
    private sealed record PendingDeletion(Func<CancellationToken, Task> DeleteAsync, MediaScanChange Change);

    /// <summary>The state of one scan: its options, where it is (for progress reports) and the category being saved.</summary>
    private sealed class ScanRun(MediaScanOptions options, IProgress<MediaScanProgress>? progress, CancellationToken token)
    {
        private readonly HashSet<string> _foldersRead = new(StringComparer.OrdinalIgnoreCase);
        private int _lastDryRunId;

        public MediaScanOptions Options => options;

        /// <summary>The root folders of every music category (an artist can be in more than one).</summary>
        public List<string> MusicRoots { get; init; } = [];
        public CancellationToken Token => token;
        public MediaScanPhase Phase { get; set; } = MediaScanPhase.Reading;
        public string? CategoryName { get; set; }
        public int ItemsDone { get; set; }
        public int ItemsTotal { get; set; }

        /// <summary>The counts of the category being saved.</summary>
        public MediaCategoryScanResult Counts { get; private set; } = new();

        /// <summary>Rows that were stored (live) under the category's root before the scan; what the deletion limit is a percentage of.</summary>
        public int RowsBefore { get; set; }

        public List<PendingDeletion> Deletions { get; } = [];

        public void StartCategory(string name, MediaCategoryScanResult counts)
        {
            CategoryName = name;
            Counts = counts;
            RowsBefore = 0;
            Deletions.Clear();
        }

        /// <summary>A negative id for a record a dry run would add, so nothing stored matches it as a parent.</summary>
        public int NextDryRunId() => --_lastDryRunId;

        public List<string> SubFolders(string folder)
        {
            token.ThrowIfCancellationRequested();
            List<string> folders = MediaScanner.SubFolders(folder);
            FolderRead(folder);
            return folders;
        }

        public List<FileInfo> FilesIn(string folder)
        {
            token.ThrowIfCancellationRequested();
            List<FileInfo> files = MediaScanner.FilesIn(folder);
            FolderRead(folder);
            return files;
        }

        private void FolderRead(string folder)
        {
            if (_foldersRead.Add(folder)) Report();
        }

        public void ItemDone(MediaScanChange? change)
        {
            ItemsDone++;
            Report(change: change);
        }

        public void Report(MediaScanChange? change = null, string? message = null, LogLevel level = LogLevel.Information, MediaCategoryScanResult? categoryResult = null) =>
            progress?.Report(new MediaScanProgress
            {
                Phase = Phase,
                CategoryName = CategoryName,
                FoldersRead = _foldersRead.Count,
                ItemsDone = ItemsDone,
                ItemsTotal = ItemsTotal,
                Change = change,
                Message = message,
                Level = level,
                CategoryResult = categoryResult,
            });
    }

    /// <summary>
    /// Saves the records of one entity type found on disk, and keeps track of them so the ones that weren't found can be deleted.
    /// </summary>
    /// <param name="entity">The entity's name in change reports ("track").</param>
    /// <param name="folderOf">The record's folder, for change reports.</param>
    /// <param name="copyScannedFields">Copies the scan-owned fields from the found record (second) onto the stored one (first); returns true if any changed.</param>
    private sealed class EntitySync<TEntity>(DataService<TEntity> service, string entity, Func<TEntity, string?> folderOf, Func<TEntity, TEntity, bool> copyScannedFields)
        where TEntity : class, IEntity
    {
        private readonly HashSet<int> _seen = [];
        private readonly HashSet<int> _new = [];

        /// <summary>
        /// Adds a record found on disk, restores it if it had been soft-deleted, or, if it exists, copies the fields the scan owns onto it when they changed
        /// (in a dry run, only works out which of these it would do). Counts and reports what happened, and returns the stored record (with its id).
        /// </summary>
        public async Task<TEntity> SyncAsync(TEntity found, ScanRun run)
        {
            run.Token.ThrowIfCancellationRequested();
            TEntity record;
            MediaScanChangeKind? change;
            if (run.Options.DryRun)
            {
                TEntity? stored = await service.FindSameRecordAsync(found, run.Token).ConfigureAwait(false);
                if (stored == null)
                {
                    found.Id = run.NextDryRunId();
                    (record, change) = (found, MediaScanChangeKind.Added);
                }
                else
                {
                    bool changed = copyScannedFields(stored, found);
                    record = stored;
                    change = stored is ISoftDeletable { IsDeleted: true } ? MediaScanChangeKind.Restored : changed ? MediaScanChangeKind.Updated : null;
                }
            }
            else
            {
                (record, AddOutcome outcome) = await service.AddOrRestoreRecordAsync(found, run.Token).ConfigureAwait(false);
                if (outcome == AddOutcome.Added)
                {
                    change = MediaScanChangeKind.Added;
                }
                else
                {
                    bool changed = copyScannedFields(record, found);
                    if (changed) await service.UpdateRecordAsync(record.Id, record, run.Token).ConfigureAwait(false);
                    change = outcome == AddOutcome.Restored ? MediaScanChangeKind.Restored : changed ? MediaScanChangeKind.Updated : null;
                }
            }

            _seen.Add(record.Id);
            if (change is MediaScanChangeKind.Added or MediaScanChangeKind.Restored) _new.Add(record.Id);
            switch (change)
            {
                case MediaScanChangeKind.Added: run.Counts.Added++; break;
                case MediaScanChangeKind.Restored: run.Counts.Restored++; break;
                case MediaScanChangeKind.Updated: run.Counts.Updated++; break;
                default: run.Counts.Unchanged++; break;
            }
            run.ItemDone(change == null ? null : new MediaScanChange(change.Value, entity, record.Name, folderOf(found)));
            return record;
        }

        /// <summary>The records the scan didn't see among <paramref name="underRoot"/> (candidates for <see cref="QueueMissing"/>).</summary>
        public IEnumerable<TEntity> Unseen(IEnumerable<TEntity> underRoot) => underRoot.Where(r => !_seen.Contains(r.Id));

        /// <summary>The ids queued for deletion so far.</summary>
        public HashSet<int> QueuedIds { get; } = [];

        /// <summary>
        /// Queues the records stored under the scanned root that the scan didn't see, to be soft-deleted when the category is finished
        /// (except the ids in <paramref name="keep"/>).
        /// </summary>
        public void QueueMissing(List<TEntity> underRoot, ScanRun run, IReadOnlySet<int>? keep = null)
        {
            run.RowsBefore += underRoot.Count(r => !_new.Contains(r.Id));
            foreach (TEntity record in Unseen(underRoot).Where(r => keep == null || !keep.Contains(r.Id)))
            {
                int id = record.Id;
                QueuedIds.Add(id);
                run.Deletions.Add(new PendingDeletion(ct => service.DeleteRecordAsync(id, ct),
                    new MediaScanChange(MediaScanChangeKind.Deleted, entity, record.Name, folderOf(record))));
            }
        }
    }

    /// <summary>True if <paramref name="folder"/> is <paramref name="root"/> or under it.</summary>
    private static bool IsUnder(string? folder, string root) =>
        folder != null && (folder.Equals(root, StringComparison.OrdinalIgnoreCase)
            || folder.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || folder.StartsWith(root.TrimEnd('\\', '/') + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

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
}
