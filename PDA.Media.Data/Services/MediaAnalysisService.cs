using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Figures for the Media Library's Analysis tab, from the media files the scan stored (tracks, movies, episodes with their size and date): totals for a
/// tree node and each of its children, the largest files / albums / shows, the file types, and housekeeping checks. A node is a
/// <see cref="MediaAnalysisScope"/>: a category root narrowed to an artist, album, source folder, show, season or file. Files belong to a category by folder.
/// </summary>
/// <remarks>
/// Each method creates its own short-lived <see cref="DataContext"/>. Totals are worked out by SQL Server; file types, movie source folders and missing
/// episodes are grouped here, from a small projection of the scope's rows.
/// The sync methods run the same queries without awaiting (the async core is called with <c>isAsync: false</c>).
/// </remarks>
public class MediaAnalysisService
{
    private readonly IDbContextFactory<DataContext> _contextFactory;

    public MediaAnalysisService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public MediaAnalysisService(IDbContextFactory<DataContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // Totals.
    // ==================================================================================================

    /// <summary>The totals of every file in <paramref name="scope"/>.</summary>
    public MediaStats GetStats(MediaAnalysisScope scope) => Run(GetStatsCoreAsync, scope, false, CancellationToken.None);

    /// <inheritdoc cref="GetStats"/>
    public Task<MediaStats> GetStatsAsync(MediaAnalysisScope scope, CancellationToken cancellationToken = default) =>
        GetStatsCoreAsync(scope, true, cancellationToken);

    private async Task<MediaStats> GetStatsCoreAsync(MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        List<GroupStats<int>> stats = await ToListAsync(GroupTotals(Files(context, scope), f => 0), isAsync, cancellationToken).ConfigureAwait(false);
        return stats.Count == 0 ? MediaStats.Empty : stats[0].ToStats();
    }

    /// <summary>
    /// The totals of each category root, with its number of artists and albums (music), source folders (movies) or shows and seasons (TV), and of
    /// soft-deleted files.
    /// </summary>
    public MediaCategorySummary GetSummary(MediaAnalysisScope root) => Run(GetSummaryCoreAsync, root, false, CancellationToken.None);

    /// <inheritdoc cref="GetSummary"/>
    public Task<MediaCategorySummary> GetSummaryAsync(MediaAnalysisScope root, CancellationToken cancellationToken = default) =>
        GetSummaryCoreAsync(root, true, cancellationToken);

    private async Task<MediaCategorySummary> GetSummaryCoreAsync(MediaAnalysisScope root, bool isAsync, CancellationToken cancellationToken)
    {
        MediaStats stats = await GetStatsCoreAsync(root, isAsync, cancellationToken).ConfigureAwait(false);
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<FileRow> files = Files(context, root);
        int groups, subGroups = 0;
        switch (root.Kind)
        {
            case MediaAnalysisKind.Music:
                groups = await CountAsync(files.Select(f => f.GroupId).Distinct(), isAsync, cancellationToken).ConfigureAwait(false);
                subGroups = await CountAsync(files.Select(f => f.ParentId).Distinct(), isAsync, cancellationToken).ConfigureAwait(false);
                break;
            case MediaAnalysisKind.Movies:
                List<string?> folders = await ToListAsync(files.Select(f => f.Folder).Distinct(), isAsync, cancellationToken).ConfigureAwait(false);
                groups = folders.Select(f => SourceFolderOf(root.Root, f)).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                break;
            default:
                groups = await CountAsync(files.Select(f => f.ParentId).Distinct(), isAsync, cancellationToken).ConfigureAwait(false);
                subGroups = await CountAsync(files.Select(f => new { f.ParentId, f.SeasonNo }).Distinct(), isAsync, cancellationToken).ConfigureAwait(false);
                break;
        }
        int deleted = await CountAsync(Files(context, root with { IncludeDeleted = true }).Where(f => f.IsDeleted), isAsync, cancellationToken).ConfigureAwait(false);
        return new MediaCategorySummary(root, stats, groups, subGroups, deleted);
    }

    /// <summary>
    /// The children of a tree node, each with its totals: a music root's artists, an artist's albums, an album's tracks; a movie root's source folders (and
    /// any movie straight in the root), a source folder's movies; a TV root's shows, a show's seasons, a season's episodes. Only children that hold files
    /// in the scope are listed, by name (seasons, tracks and episodes by number). A file has no children.
    /// </summary>
    public List<MediaAnalysisNode> GetChildren(MediaAnalysisScope scope) => Run(GetChildrenCoreAsync, scope, false, CancellationToken.None);

    /// <inheritdoc cref="GetChildren"/>
    public Task<List<MediaAnalysisNode>> GetChildrenAsync(MediaAnalysisScope scope, CancellationToken cancellationToken = default) =>
        GetChildrenCoreAsync(scope, true, cancellationToken);

    private async Task<List<MediaAnalysisNode>> GetChildrenCoreAsync(MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<FileRow> files = Files(context, scope);
        switch (scope.Kind, scope.Level)
        {
            case (MediaAnalysisKind.Music, MediaAnalysisLevel.Root):
                {
                    List<GroupStats<int>> groups = await ToListAsync(GroupTotals(files, f => f.GroupId), isAsync, cancellationToken).ConfigureAwait(false);
                    List<int> ids = groups.Select(g => g.Key).ToList();
                    Dictionary<int, Artist> artists = (await ToListAsync(Rows<Artist>(context, true).Where(a => ids.Contains(a.Id)), isAsync, cancellationToken)
                        .ConfigureAwait(false)).ToDictionary(a => a.Id);
                    return groups.Where(g => artists.ContainsKey(g.Key)).Select(g =>
                    {
                        Artist artist = artists[g.Key];
                        // The artist's folder in this category: its stored folder may be another music category's copy.
                        string? folder = SourceFolderOf(scope.Root, artist.Folder) != null ? artist.Folder : ParentFolder(g.FirstFolder);
                        return new MediaAnalysisNode(scope with { ArtistId = artist.Id }, artist.Name, folder, null, g.ToStats(), artist.IsDeleted);
                    }).OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
            case (MediaAnalysisKind.Music, MediaAnalysisLevel.Artist):
                {
                    List<GroupStats<int>> groups = await ToListAsync(GroupTotals(files, f => f.ParentId), isAsync, cancellationToken).ConfigureAwait(false);
                    List<int> ids = groups.Select(g => g.Key).ToList();
                    Dictionary<int, Album> albums = (await ToListAsync(Rows<Album>(context, true).Where(a => ids.Contains(a.Id)), isAsync, cancellationToken)
                        .ConfigureAwait(false)).ToDictionary(a => a.Id);
                    return groups.Where(g => albums.ContainsKey(g.Key))
                        .Select(g => new MediaAnalysisNode(scope with { AlbumId = g.Key }, albums[g.Key].Name, albums[g.Key].Folder ?? g.FirstFolder, null, g.ToStats(),
                            albums[g.Key].IsDeleted))
                        .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
            case (MediaAnalysisKind.Movies, MediaAnalysisLevel.Root):
                {
                    // Source folders are worked out from the movies' folders (the first folder under the root); a movie straight in the root is a leaf.
                    List<FileRow> movies = await ToListAsync(files, isAsync, cancellationToken).ConfigureAwait(false);
                    var nodes = new List<MediaAnalysisNode>();
                    foreach (IGrouping<string, FileRow> source in movies.Where(m => SourceFolderOf(scope.Root, m.Folder) != null)
                                 .GroupBy(m => SourceFolderOf(scope.Root, m.Folder)!, StringComparer.OrdinalIgnoreCase))
                    {
                        nodes.Add(new MediaAnalysisNode(scope with { SourceFolder = source.Key }, LastSegment(source.Key), source.Key, null, StatsOf(source), false));
                    }
                    nodes.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
                    nodes.AddRange(movies.Where(m => SourceFolderOf(scope.Root, m.Folder) == null).OrderBy(m => m.Name).Select(m => Leaf(scope, m)));
                    return nodes;
                }
            case (MediaAnalysisKind.TV, MediaAnalysisLevel.Root):
                {
                    List<GroupStats<int>> groups = await ToListAsync(GroupTotals(files, f => f.ParentId), isAsync, cancellationToken).ConfigureAwait(false);
                    List<int> ids = groups.Select(g => g.Key).ToList();
                    Dictionary<int, TVShow> shows = (await ToListAsync(Rows<TVShow>(context, true).Where(s => ids.Contains(s.Id)), isAsync, cancellationToken)
                        .ConfigureAwait(false)).ToDictionary(s => s.Id);
                    return groups.Where(g => shows.ContainsKey(g.Key))
                        .Select(g => new MediaAnalysisNode(scope with { ShowId = g.Key }, ShowName(shows[g.Key]), shows[g.Key].Folder, null, g.ToStats(), shows[g.Key].IsDeleted)
                        { Title = shows[g.Key].Name })
                        .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
            case (MediaAnalysisKind.TV, MediaAnalysisLevel.Show):
                {
                    List<GroupStats<int>> groups = await ToListAsync(GroupTotals(files, f => f.SeasonNo), isAsync, cancellationToken).ConfigureAwait(false);
                    return groups.OrderBy(g => g.Key)
                        .Select(g => new MediaAnalysisNode(scope with { SeasonNo = g.Key }, SeasonName(g.Key), g.FirstFolder, null, g.ToStats(), false)).ToList();
                }
            case (_, MediaAnalysisLevel.Album or MediaAnalysisLevel.Source or MediaAnalysisLevel.Season):
                {
                    List<FileRow> leaves = await ToListAsync(files, isAsync, cancellationToken).ConfigureAwait(false);
                    return leaves.OrderBy(f => f.SeasonNo).ThenBy(f => f.Number == 0 ? int.MaxValue : f.Number).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(f => Leaf(scope, f)).ToList();
                }
            default:
                return [];
        }
    }

    // Largest, file types and every file.
    // ==================================================================================================

    /// <summary>
    /// The <paramref name="count"/> largest files in <paramref name="scope"/>, or (<see cref="MediaLargestBy.Groups"/>) the largest albums or shows by the
    /// total of their files there. Files whose size isn't known are left out.
    /// </summary>
    public List<MediaAnalysisItem> GetLargest(MediaAnalysisScope scope, int count = 20, MediaLargestBy by = MediaLargestBy.Files) =>
        GetLargestCoreAsync(scope, count, by, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="GetLargest"/>
    public Task<List<MediaAnalysisItem>> GetLargestAsync(MediaAnalysisScope scope, int count = 20, MediaLargestBy by = MediaLargestBy.Files,
        CancellationToken cancellationToken = default) => GetLargestCoreAsync(scope, count, by, true, cancellationToken);

    private async Task<List<MediaAnalysisItem>> GetLargestCoreAsync(MediaAnalysisScope scope, int count, MediaLargestBy by, bool isAsync,
        CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<FileRow> files = Files(context, scope).Where(f => f.FileSize != null);
        if (by == MediaLargestBy.Files || scope.Kind == MediaAnalysisKind.Movies)
        {
            List<FileRow> largest = await ToListAsync(files.OrderByDescending(f => f.FileSize).ThenBy(f => f.Name).Take(count), isAsync, cancellationToken)
                .ConfigureAwait(false);
            return await ItemsAsync(context, scope, largest, isAsync, cancellationToken).ConfigureAwait(false);
        }

        List<GroupStats<int>> groups = await ToListAsync(GroupTotals(files, f => f.ParentId).OrderByDescending(g => g.Size).Take(count), isAsync, cancellationToken)
            .ConfigureAwait(false);
        return await GroupItemsAsync(context, scope, groups, isAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The files in <paramref name="scope"/> by extension, largest total first.</summary>
    public List<MediaFileType> GetFileTypes(MediaAnalysisScope scope) => Run(GetFileTypesCoreAsync, scope, false, CancellationToken.None);

    /// <inheritdoc cref="GetFileTypes"/>
    public Task<List<MediaFileType>> GetFileTypesAsync(MediaAnalysisScope scope, CancellationToken cancellationToken = default) =>
        GetFileTypesCoreAsync(scope, true, cancellationToken);

    private async Task<List<MediaFileType>> GetFileTypesCoreAsync(MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        var files = await ToListAsync(Files(context, scope).Select(f => new { f.FileName, f.FileSize }), isAsync, cancellationToken).ConfigureAwait(false);
        return files.GroupBy(f => ExtensionOf(f.FileName))
            .Select(g => new MediaFileType(g.Key, g.Count(), g.Sum(f => f.FileSize ?? 0)))
            .OrderByDescending(t => t.Size).ThenByDescending(t => t.Items).ToList();
    }

    /// <summary>Every file in <paramref name="scope"/> (e.g. to export), in tree order.</summary>
    public List<MediaAnalysisItem> GetFiles(MediaAnalysisScope scope) => Run(GetFilesCoreAsync, scope, false, CancellationToken.None);

    /// <inheritdoc cref="GetFiles"/>
    public Task<List<MediaAnalysisItem>> GetFilesAsync(MediaAnalysisScope scope, CancellationToken cancellationToken = default) =>
        GetFilesCoreAsync(scope, true, cancellationToken);

    private async Task<List<MediaAnalysisItem>> GetFilesCoreAsync(MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        List<FileRow> files = await ToListAsync(Files(context, scope), isAsync, cancellationToken).ConfigureAwait(false);
        List<MediaAnalysisItem> items = await ItemsAsync(context, scope, files, isAsync, cancellationToken).ConfigureAwait(false);
        return items.OrderBy(i => i.Parent ?? "", StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Housekeeping.
    // ==================================================================================================

    /// <summary>
    /// The housekeeping checks for <paramref name="scope"/>'s kind of media, each with how many rows it found (and the first
    /// <see cref="MediaCheck.MaxRows"/>). Music: albums without cover art, albums whose track count is wrong, artists without albums; movies: without cover
    /// art, straight in a source folder; TV: shows without cover art, seasons with missing episodes, episodes without a number, shows without episodes;
    /// any: files without a size (not scanned since sizes were added) and soft-deleted rows.
    /// </summary>
    public List<MediaCheck> GetChecks(MediaAnalysisScope scope) => Run(GetChecksCoreAsync, scope, false, CancellationToken.None);

    /// <inheritdoc cref="GetChecks"/>
    public Task<List<MediaCheck>> GetChecksAsync(MediaAnalysisScope scope, CancellationToken cancellationToken = default) =>
        GetChecksCoreAsync(scope, true, cancellationToken);

    private async Task<List<MediaCheck>> GetChecksCoreAsync(MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken)
    {
        await using DataContext context = await CreateContextAsync(isAsync, cancellationToken).ConfigureAwait(false);
        IQueryable<FileRow> files = Files(context, scope);
        IReadOnlyList<string> roots = [scope.Root];
        var checks = new List<MediaCheck>();

        async Task AddFileCheck(string title, string description, IQueryable<FileRow> rows, Func<FileRow, string?>? note = null)
        {
            int count = await CountAsync(rows, isAsync, cancellationToken).ConfigureAwait(false);
            List<FileRow> first = await ToListAsync(rows.OrderBy(f => f.Folder).ThenBy(f => f.FileName).Take(MediaCheck.MaxRows), isAsync, cancellationToken)
                .ConfigureAwait(false);
            List<MediaAnalysisItem> items = await ItemsAsync(context, scope, first, isAsync, cancellationToken).ConfigureAwait(false);
            if (note != null) items = items.Zip(first, (item, row) => item with { Note = note(row) }).ToList();
            checks.Add(new MediaCheck(title, description, count, items));
        }

        switch (scope.Kind)
        {
            case MediaAnalysisKind.Music:
                {
                    IQueryable<int> albumIds = files.Select(f => f.ParentId);
                    IQueryable<Album> albums = Rows<Album>(context, scope.IncludeDeleted).Where(a => albumIds.Contains(a.Id));
                    var noCover = await ToListAsync(albums.Where(a => a.CoverArtFilename == null).OrderBy(a => a.Folder).Take(MediaCheck.MaxRows), isAsync, cancellationToken)
                        .ConfigureAwait(false);
                    checks.Add(new MediaCheck("Albums without cover art", "No cover.jpg / folder.jpg (or similar) was found in the album folder.",
                        await CountAsync(albums.Where(a => a.CoverArtFilename == null), isAsync, cancellationToken).ConfigureAwait(false),
                        await AlbumItemsAsync(context, scope, noCover, null, isAsync, cancellationToken).ConfigureAwait(false)));

                    var wrongCount = albums.Select(a => new { Album = a, Tracks = context.Tracks.Count(t => t.AlbumId == a.Id) }).Where(x => x.Album.TrackCount != x.Tracks);
                    var wrong = await ToListAsync(wrongCount.OrderBy(x => x.Album.Folder).Take(MediaCheck.MaxRows), isAsync, cancellationToken).ConfigureAwait(false);
                    checks.Add(new MediaCheck("Albums whose track count is wrong", "The album's stored track count isn't the number of tracks stored for it; scan it again.",
                        await CountAsync(wrongCount, isAsync, cancellationToken).ConfigureAwait(false),
                        await AlbumItemsAsync(context, scope, wrong.Select(x => x.Album).ToList(), wrong.Select(x => $"{x.Album.TrackCount} stored, {x.Tracks} found").ToList(),
                            isAsync, cancellationToken).ConfigureAwait(false)));

                    if (scope.Level is MediaAnalysisLevel.Root or MediaAnalysisLevel.Artist)
                    {
                        IQueryable<Artist> artists = Rows<Artist>(context, false).Where(FolderQueries.UnderAny<Artist>(nameof(Artist.Folder), roots))
                            .Where(a => a.Id != 0 && !context.Albums.Any(al => al.ArtistId == a.Id));
                        if (scope.ArtistId is int artistId) artists = artists.Where(a => a.Id == artistId);
                        List<Artist> lonely = await ToListAsync(artists.OrderBy(a => a.Name).Take(MediaCheck.MaxRows), isAsync, cancellationToken).ConfigureAwait(false);
                        checks.Add(new MediaCheck("Artists without albums", "The artist's folder holds no album folders with audio files.",
                            await CountAsync(artists, isAsync, cancellationToken).ConfigureAwait(false),
                            lonely.Select(a => new MediaAnalysisItem(scope with { ArtistId = a.Id }, a.Name, null, a.Folder, null, null, null, 0, a.IsDeleted)).ToList()));
                    }
                    break;
                }
            case MediaAnalysisKind.Movies:
                {
                    await AddFileCheck("Movies without cover art", "No poster.jpg / folder.jpg (or similar) was found in the movie's folder.",
                        files.Where(f => f.CoverArt == null)).ConfigureAwait(false);

                    // A movie file straight in a source folder (or the root) has no folder of its own: Plex likes "Movie (Year)/Movie (Year).mkv".
                    List<FileRow> movies = await ToListAsync(files, isAsync, cancellationToken).ConfigureAwait(false);
                    List<FileRow> loose = movies.Where(m => SourceFolderOf(scope.Root, m.Folder) == null || SameFolder(m.Folder, SourceFolderOf(scope.Root, m.Folder)))
                        .OrderBy(m => m.Folder).ThenBy(m => m.FileName).ToList();
                    checks.Add(new MediaCheck("Movies without their own folder", "The movie file is straight in a source folder; Plex prefers Movie (Year)/Movie (Year).mkv.",
                        loose.Count, await ItemsAsync(context, scope, loose.Take(MediaCheck.MaxRows).ToList(), isAsync, cancellationToken).ConfigureAwait(false)));
                    break;
                }
            case MediaAnalysisKind.TV:
                {
                    IQueryable<TVShow> shows = Rows<TVShow>(context, scope.IncludeDeleted).Where(FolderQueries.UnderAny<TVShow>(nameof(TVShow.Folder), roots));
                    if (scope.ShowId is int showId) shows = shows.Where(s => s.Id == showId);
                    List<TVShow> noCover = await ToListAsync(shows.Where(s => s.CoverArtFilename == null).OrderBy(s => s.Name).Take(MediaCheck.MaxRows), isAsync, cancellationToken)
                        .ConfigureAwait(false);
                    checks.Add(new MediaCheck("Shows without cover art", "No poster.jpg / folder.jpg (or similar) was found in the show's folder.",
                        await CountAsync(shows.Where(s => s.CoverArtFilename == null), isAsync, cancellationToken).ConfigureAwait(false),
                        noCover.Select(s => ShowItem(scope, s)).ToList()));

                    // Missing episode numbers: the numbers from 1 to the highest one in each season that aren't there.
                    var numbered = await ToListAsync(files.Where(f => f.Number > 0).Select(f => new { f.ParentId, f.SeasonNo, f.Number, f.Folder }), isAsync, cancellationToken)
                        .ConfigureAwait(false);
                    var gaps = numbered.GroupBy(e => (e.ParentId, e.SeasonNo))
                        .Select(g => (Season: g.Key, g.Min(e => e.Folder), Missing: Enumerable.Range(1, g.Max(e => e.Number)).Except(g.Select(e => e.Number)).ToList()))
                        .Where(g => g.Missing.Count > 0).ToList();
                    List<int> gapShowIds = gaps.Select(g => g.Season.ParentId).Distinct().ToList();
                    Dictionary<int, TVShow> gapShows = (await ToListAsync(Rows<TVShow>(context, true).Where(s => gapShowIds.Contains(s.Id)), isAsync, cancellationToken)
                        .ConfigureAwait(false)).ToDictionary(s => s.Id);
                    checks.Add(new MediaCheck("Seasons with missing episodes", "Episode numbers from 1 to the season's highest that aren't there.", gaps.Count,
                        gaps.Where(g => gapShows.ContainsKey(g.Season.ParentId))
                            .OrderBy(g => ShowName(gapShows[g.Season.ParentId]), StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Season.SeasonNo)
                            .Take(MediaCheck.MaxRows)
                            .Select(g => new MediaAnalysisItem(scope with { ShowId = g.Season.ParentId, SeasonNo = g.Season.SeasonNo }, SeasonName(g.Season.SeasonNo),
                                ShowName(gapShows[g.Season.ParentId]), g.Item2, null, null, null, 0, false, "missing " + NumberRanges(g.Missing)))
                            .ToList()));

                    await AddFileCheck("Episodes without a number", "No sNNeNN in the file name, so the episode is matched by its title.",
                        files.Where(f => f.Number == 0)).ConfigureAwait(false);

                    IQueryable<TVShow> empty = shows.Where(s => !context.TVShowEpisodes.Any(e => e.TVShowId == s.Id));
                    List<TVShow> emptyShows = await ToListAsync(empty.OrderBy(s => s.Name).Take(MediaCheck.MaxRows), isAsync, cancellationToken).ConfigureAwait(false);
                    checks.Add(new MediaCheck("Shows without episodes", "The show's folder holds no video files.",
                        await CountAsync(empty, isAsync, cancellationToken).ConfigureAwait(false), emptyShows.Select(s => ShowItem(scope, s)).ToList()));
                    break;
                }
        }

        await AddFileCheck("Files without a size", "Scanned before file sizes were stored; scan the category again to fill them in.",
            files.Where(f => f.FileSize == null)).ConfigureAwait(false);
        await AddFileCheck("Deleted files still in the database",
            "Soft-deleted: gone from disk at the last scan, kept with their favourites and notes in case they come back.",
            Files(context, scope with { IncludeDeleted = true }).Where(f => f.IsDeleted)).ConfigureAwait(false);
        return checks;
    }

    // Rows to items.
    // ==================================================================================================

    /// <summary>Files as list items, with their artist · album or show · season.</summary>
    private static async Task<List<MediaAnalysisItem>> ItemsAsync(DataContext context, MediaAnalysisScope scope, List<FileRow> files, bool isAsync,
        CancellationToken cancellationToken)
    {
        Func<FileRow, string?> parent = _ => null;
        if (scope.Kind == MediaAnalysisKind.Music)
        {
            List<int> albumIds = files.Select(f => f.ParentId).Distinct().ToList();
            List<int> artistIds = files.Select(f => f.GroupId).Distinct().ToList();
            Dictionary<int, string> albums = (await ToListAsync(Rows<Album>(context, true).Where(a => albumIds.Contains(a.Id)).Select(a => new { a.Id, a.Name }),
                isAsync, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Name);
            Dictionary<int, string> artists = (await ToListAsync(Rows<Artist>(context, true).Where(a => artistIds.Contains(a.Id)).Select(a => new { a.Id, a.Name }),
                isAsync, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Name);
            parent = f => $"{artists.GetValueOrDefault(f.GroupId, "?")} · {albums.GetValueOrDefault(f.ParentId, "?")}";
        }
        else if (scope.Kind == MediaAnalysisKind.TV)
        {
            List<int> showIds = files.Select(f => f.ParentId).Distinct().ToList();
            Dictionary<int, string> shows = (await ToListAsync(Rows<TVShow>(context, true).Where(s => showIds.Contains(s.Id)), isAsync, cancellationToken)
                .ConfigureAwait(false)).ToDictionary(s => s.Id, ShowName);
            parent = f => $"{shows.GetValueOrDefault(f.ParentId, "?")} · {SeasonName(f.SeasonNo)}";
        }

        return files.Select(f => new MediaAnalysisItem(ItemScope(scope, f), FileTitle(scope.Kind, f), parent(f), f.Folder, f.FileName, f.FileSize, f.FileDate, 1,
            f.IsDeleted)
        { Title = f.Name }).ToList();
    }

    /// <summary>Album (music) or show (TV) totals as list items.</summary>
    private static async Task<List<MediaAnalysisItem>> GroupItemsAsync(DataContext context, MediaAnalysisScope scope, List<GroupStats<int>> groups, bool isAsync,
        CancellationToken cancellationToken)
    {
        List<int> ids = groups.Select(g => g.Key).ToList();
        if (scope.Kind == MediaAnalysisKind.TV)
        {
            Dictionary<int, TVShow> shows = (await ToListAsync(Rows<TVShow>(context, true).Where(s => ids.Contains(s.Id)), isAsync, cancellationToken)
                .ConfigureAwait(false)).ToDictionary(s => s.Id);
            return groups.Where(g => shows.ContainsKey(g.Key))
                .Select(g => ShowItem(scope, shows[g.Key]) with { Size = g.Size, Date = g.Newest, Items = g.Items }).ToList();
        }

        List<Album> albums = await ToListAsync(Rows<Album>(context, true).Where(a => ids.Contains(a.Id)), isAsync, cancellationToken).ConfigureAwait(false);
        List<MediaAnalysisItem> items = await AlbumItemsAsync(context, scope, albums, null, isAsync, cancellationToken).ConfigureAwait(false);
        Dictionary<int, GroupStats<int>> byId = groups.ToDictionary(g => g.Key);
        return items.Select(i => i with { Size = byId[i.Scope.AlbumId!.Value].Size, Date = byId[i.Scope.AlbumId.Value].Newest, Items = byId[i.Scope.AlbumId.Value].Items })
            .OrderByDescending(i => i.Size).ToList();
    }

    private static async Task<List<MediaAnalysisItem>> AlbumItemsAsync(DataContext context, MediaAnalysisScope scope, List<Album> albums, List<string>? notes,
        bool isAsync, CancellationToken cancellationToken)
    {
        List<int> artistIds = albums.Select(a => a.ArtistId).Distinct().ToList();
        Dictionary<int, string> artists = (await ToListAsync(Rows<Artist>(context, true).Where(a => artistIds.Contains(a.Id)).Select(a => new { a.Id, a.Name }),
            isAsync, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Name);
        return albums.Select((a, i) => new MediaAnalysisItem(scope with { ArtistId = a.ArtistId, AlbumId = a.Id, ItemId = null }, a.Name,
            artists.GetValueOrDefault(a.ArtistId, "?"), a.Folder, null, null, null, a.TrackCount, a.IsDeleted, notes?[i])).ToList();
    }

    private static MediaAnalysisItem ShowItem(MediaAnalysisScope scope, TVShow show) =>
        new(scope with { ShowId = show.Id, SeasonNo = null, ItemId = null }, ShowName(show), null, show.Folder, null, null, null, 0, show.IsDeleted) { Title = show.Name };

    private static MediaAnalysisNode Leaf(MediaAnalysisScope scope, FileRow file) =>
        new(ItemScope(scope, file), FileTitle(scope.Kind, file), file.Folder, file.FileName, StatsOf([file]), file.IsDeleted) { Title = file.Name };

    /// <summary>The file's row as a scope, keeping the way down to it (artist and album, or show and season).</summary>
    private static MediaAnalysisScope ItemScope(MediaAnalysisScope scope, FileRow file) => scope.Kind switch
    {
        MediaAnalysisKind.Music => scope with { ArtistId = file.GroupId, AlbumId = file.ParentId, ItemId = file.Id },
        MediaAnalysisKind.TV => scope with { ShowId = file.ParentId, SeasonNo = file.SeasonNo, ItemId = file.Id },
        _ => scope with { ItemId = file.Id },
    };

    // Names.
    // ==================================================================================================

    /// <summary>"01 - Title" for a track, "s01e02 - Title" for an episode, the name for a movie.</summary>
    private static string FileTitle(MediaAnalysisKind kind, FileRow file) => kind switch
    {
        MediaAnalysisKind.Music when file.Number > 0 => $"{file.Number:00} - {file.Name}",
        MediaAnalysisKind.TV when file.Number > 0 => $"s{file.SeasonNo:00}e{file.Number:00} - {file.Name}",
        _ => file.Name,
    };

    /// <summary>"Firefly (2002)": the show's name with its year, unless the name already has it.</summary>
    public static string ShowName(TVShow show) =>
        show.ReleaseYear > 0 && !show.Name.Contains($"({show.ReleaseYear})", StringComparison.Ordinal) ? $"{show.Name} ({show.ReleaseYear})" : show.Name;

    public static string SeasonName(int seasonNo) => seasonNo == 0 ? "Specials" : $"Season {seasonNo}";

    /// <summary>"3, 5-7, 10".</summary>
    public static string NumberRanges(IEnumerable<int> numbers)
    {
        var parts = new List<string>();
        foreach (List<int> run in Runs(numbers.Order()))
        {
            parts.Add(run.Count == 1 ? $"{run[0]}" : $"{run[0]}-{run[^1]}");
        }
        return string.Join(", ", parts);

        static IEnumerable<List<int>> Runs(IEnumerable<int> sorted)
        {
            var run = new List<int>();
            foreach (int n in sorted)
            {
                if (run.Count > 0 && n != run[^1] + 1)
                {
                    yield return run;
                    run = [];
                }
                run.Add(n);
            }
            if (run.Count > 0) yield return run;
        }
    }

    /// <summary>".mkv" (lower case) for "Alien.MKV"; "(none)" without an extension.</summary>
    public static string ExtensionOf(string? fileName)
    {
        int dot = fileName?.LastIndexOf('.') ?? -1;
        return dot <= 0 || dot == fileName!.Length - 1 ? "(none)" : fileName[dot..].ToLowerInvariant();
    }

    // Folders (stored paths can use either separator: the scan stores Windows paths).
    // ==================================================================================================

    /// <summary>The source folder (the first folder under <paramref name="root"/>) a movie's folder is in; null for a movie straight in the root.</summary>
    public static string? SourceFolderOf(string root, string? folder)
    {
        string trimmed = root.TrimEnd('\\', '/');
        if (folder == null || folder.Length <= trimmed.Length + 1 || !folder.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) return null;
        char separator = folder[trimmed.Length];
        if (separator is not ('\\' or '/')) return null;
        int end = folder.IndexOfAny(['\\', '/'], trimmed.Length + 1);
        return end < 0 ? folder : folder[..end];
    }

    private static string? ParentFolder(string? folder)
    {
        int end = folder?.TrimEnd('\\', '/').LastIndexOfAny(['\\', '/']) ?? -1;
        return end <= 0 ? null : folder![..end];
    }

    private static string LastSegment(string folder)
    {
        string trimmed = folder.TrimEnd('\\', '/');
        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }

    private static bool SameFolder(string? a, string? b) =>
        a != null && b != null && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Queries.
    // ==================================================================================================

    /// <summary>A media file's row, the same for tracks, movies and episodes.</summary>
    private sealed class FileRow
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";

        /// <summary>Album (music) or show (TV); 0 for movies.</summary>
        public int ParentId { get; init; }

        /// <summary>Artist (music); 0 otherwise.</summary>
        public int GroupId { get; init; }

        public int SeasonNo { get; init; }

        /// <summary>Track or episode number (0: none).</summary>
        public int Number { get; init; }

        public string? Folder { get; init; }
        public string? FileName { get; init; }
        public string? CoverArt { get; init; }
        public long? FileSize { get; init; }
        public DateTime? FileDate { get; init; }
        public bool IsDeleted { get; init; }
    }

    /// <summary>The media files in <paramref name="scope"/> (tracks, movies or episodes under its root, narrowed by its fields).</summary>
    private static IQueryable<FileRow> Files(DataContext context, MediaAnalysisScope scope)
    {
        IReadOnlyList<string> roots = [scope.Root];
        int? itemId = scope.ItemId;
        switch (scope.Kind)
        {
            case MediaAnalysisKind.Music:
                {
                    IQueryable<Track> tracks = Rows<Track>(context, scope.IncludeDeleted).Where(FolderQueries.UnderAny<Track>(nameof(Track.Folder), roots));
                    if (scope.ArtistId is int artistId) tracks = tracks.Where(t => t.ArtistId == artistId);
                    if (scope.AlbumId is int albumId) tracks = tracks.Where(t => t.AlbumId == albumId);
                    if (itemId != null) tracks = tracks.Where(t => t.Id == itemId);
                    return tracks.Select(t => new FileRow
                    {
                        Id = t.Id,
                        Name = t.Name,
                        ParentId = t.AlbumId,
                        GroupId = t.ArtistId,
                        Number = t.TrackNo,
                        Folder = t.Folder,
                        FileName = t.OriginalFilename,
                        CoverArt = t.CoverArtFilename,
                        FileSize = t.FileSize,
                        FileDate = t.FileDate,
                        IsDeleted = t.IsDeleted,
                    });
                }
            case MediaAnalysisKind.Movies:
                {
                    IQueryable<Movie> movies = Rows<Movie>(context, scope.IncludeDeleted).Where(FolderQueries.UnderAny<Movie>(nameof(Movie.Folder), roots));
                    if (scope.SourceFolder != null) movies = movies.Where(FolderQueries.UnderAny<Movie>(nameof(Movie.Folder), [scope.SourceFolder]));
                    if (itemId != null) movies = movies.Where(m => m.Id == itemId);
                    return movies.Select(m => new FileRow
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Folder = m.Folder,
                        FileName = m.OriginalFilename,
                        CoverArt = m.CoverArtFilename,
                        FileSize = m.FileSize,
                        FileDate = m.FileDate,
                        IsDeleted = m.IsDeleted,
                    });
                }
            default:
                {
                    IQueryable<TVShowEpisode> episodes = Rows<TVShowEpisode>(context, scope.IncludeDeleted)
                        .Where(FolderQueries.UnderAny<TVShowEpisode>(nameof(TVShowEpisode.Folder), roots));
                    if (scope.ShowId is int showId) episodes = episodes.Where(e => e.TVShowId == showId);
                    if (scope.SeasonNo is int seasonNo) episodes = episodes.Where(e => e.SeasonNo == seasonNo);
                    if (itemId != null) episodes = episodes.Where(e => e.Id == itemId);
                    return episodes.Select(e => new FileRow
                    {
                        Id = e.Id,
                        Name = e.Name,
                        ParentId = e.TVShowId,
                        SeasonNo = e.SeasonNo,
                        Number = e.EpisodeNo,
                        Folder = e.Folder,
                        FileName = e.OriginalFilename,
                        CoverArt = e.CoverArtFilename,
                        FileSize = e.FileSize,
                        FileDate = e.FileDate,
                        IsDeleted = e.IsDeleted,
                    });
                }
        }
    }

    /// <summary>Totals for each group of files, worked out by SQL Server.</summary>
    private sealed class GroupStats<TKey>
    {
        public TKey Key { get; init; } = default!;
        public int Items { get; init; }
        public int Folders { get; init; }
        public long Size { get; init; }
        public int WithoutSize { get; init; }
        public DateTime? Oldest { get; init; }
        public DateTime? Newest { get; init; }

        /// <summary>The first of the group's folders (by name): an album's or season's folder.</summary>
        public string? FirstFolder { get; init; }

        public MediaStats ToStats() => new(Items, Folders, Size, WithoutSize, Oldest, Newest);
    }

    private static IQueryable<GroupStats<TKey>> GroupTotals<TKey>(IQueryable<FileRow> files, Expression<Func<FileRow, TKey>> key) =>
        files.GroupBy(key).Select(g => new GroupStats<TKey>
        {
            Key = g.Key,
            Items = g.Count(),
            Folders = g.Select(f => f.Folder).Distinct().Count(),
            Size = g.Sum(f => f.FileSize ?? 0),
            WithoutSize = g.Count(f => f.FileSize == null),
            Oldest = g.Min(f => f.FileDate),
            Newest = g.Max(f => f.FileDate),
            FirstFolder = g.Min(f => f.Folder),
        });

    private static MediaStats StatsOf(IEnumerable<FileRow> files)
    {
        List<FileRow> list = files.ToList();
        return new MediaStats(list.Count, list.Select(f => f.Folder).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            list.Sum(f => f.FileSize ?? 0), list.Count(f => f.FileSize == null), list.Min(f => f.FileDate), list.Max(f => f.FileDate));
    }

    private T Run<T>(Func<MediaAnalysisScope, bool, CancellationToken, Task<T>> core, MediaAnalysisScope scope, bool isAsync, CancellationToken cancellationToken) =>
        core(scope, isAsync, cancellationToken).GetAwaiter().GetResult();

    private async Task<DataContext> CreateContextAsync(bool isAsync, CancellationToken cancellationToken) =>
        isAsync ? await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false) : _contextFactory.CreateDbContext();

    private static async Task<List<T>> ToListAsync<T>(IQueryable<T> query, bool isAsync, CancellationToken cancellationToken) =>
        isAsync ? await query.ToListAsync(cancellationToken).ConfigureAwait(false) : query.ToList();

    private static async Task<int> CountAsync<T>(IQueryable<T> query, bool isAsync, CancellationToken cancellationToken) =>
        isAsync ? await query.CountAsync(cancellationToken).ConfigureAwait(false) : query.Count();

    /// <summary>A table without tracking; soft-deleted rows included or not.</summary>
    private static IQueryable<T> Rows<T>(DataContext context, bool includeDeleted) where T : class =>
        includeDeleted ? context.Set<T>().IgnoreQueryFilters([DataContext.SoftDeleteFilter]).AsNoTracking() : context.Set<T>().AsNoTracking();
}
