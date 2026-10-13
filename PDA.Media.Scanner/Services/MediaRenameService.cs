using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>
/// Renames a media item on disk and in the database together, so the next scan finds the same row (same id, favourites and notes) under its new name.
/// <see cref="PlanAsync"/> works out every folder and file rename and checks them (names Windows allows, nothing already there, no other row with that name,
/// and that the scan will read the new names back as the new name); <see cref="ApplyAsync"/> renames on disk (files first, then folders), then updates the
/// database in one save (<see cref="MediaPathService"/>), putting the disk back if anything fails.
/// </summary>
/// <remarks>
/// What is renamed: a movie's folder and/or its files (the main video file, and files named like it: samples, subtitles, artwork), a loose movie's files;
/// a TV show's folder and/or the show-name part of its episode files; an episode's or track's file (its title part; the number stays);
/// an album's folder; an artist's folder in every music category it is in.
/// It won't run during a scan, and a scan won't start during a rename (<see cref="MediaScanner.TryReserve"/>).
/// </remarks>
public class MediaRenameService
{
    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    private const string PlexWarning = "Plex is watching these folders: it will re-scan this item, and may not keep its watched state or edited details.";

    private readonly MediaScanner _scanner;
    private readonly MediaPathService _pathService;
    private readonly ArtistService _artistService;
    private readonly AlbumService _albumService;
    private readonly TrackService _trackService;
    private readonly MovieService _movieService;
    private readonly TVShowService _tvShowService;
    private readonly TVShowEpisodeService _episodeService;
    private readonly ILogger<MediaRenameService> _logger;

    public MediaRenameService(MediaScanner scanner, MediaPathService pathService, ArtistService artistService, AlbumService albumService, TrackService trackService,
        MovieService movieService, TVShowService tvShowService, TVShowEpisodeService episodeService, ILogger<MediaRenameService>? logger = null)
    {
        _scanner = scanner;
        _pathService = pathService;
        _artistService = artistService;
        _albumService = albumService;
        _trackService = trackService;
        _movieService = movieService;
        _tvShowService = tvShowService;
        _episodeService = episodeService;
        _logger = logger ?? NullLogger<MediaRenameService>.Instance;
    }

    /// <summary>For tests: runs before each disk step (throw to make the step fail).</summary>
    internal Action<MediaRenameStep>? BeforeStep { get; set; }

    // Planning.
    // ==================================================================================================

    /// <summary>Works out and checks a rename without changing anything. Reads the row again, and the folders on disk.</summary>
    public MediaRenamePlan Plan(MediaRenameRequest request) => PlanAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc cref="Plan"/>
    public async Task<MediaRenamePlan> PlanAsync(MediaRenameRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IEntity? item = await ReloadAsync(request.Item, cancellationToken).ConfigureAwait(false);
        var plan = new MediaRenamePlan(request, item, item == null ? request.Item.Name : CurrentName(item));
        if (item == null)
        {
            plan.Problems.Add("It isn't in the database any more (or it has been deleted): search again.");
            return plan;
        }

        string newName = plan.NewName;
        string? nameProblem = CheckName(newName);
        if (nameProblem != null)
        {
            plan.Problems.Add(nameProblem);
            return plan;
        }
        if (newName == plan.OldName)
        {
            plan.Problems.Add("That is its current name.");
            return plan;
        }

        try
        {
            switch (item)
            {
                case Movie movie: PlanMovie(plan, movie); break;
                case TVShow show: PlanShow(plan, show); break;
                case TVShowEpisode episode: PlanEpisode(plan, episode); break;
                case Track track: PlanTrack(plan, track); break;
                case Album album: PlanFolderRename(plan, album.Folder); break;
                case Artist artist: await PlanArtistAsync(plan, artist, cancellationToken).ConfigureAwait(false); break;
                default: plan.Problems.Add($"{item.GetType().Name} rows can't be renamed."); break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            plan.Problems.Add($"The folders couldn't be read: {ex.Message}");
        }

        if (plan.Problems.Count == 0) await CheckDatabaseNameAsync(plan, item, cancellationToken).ConfigureAwait(false);
        CheckSteps(plan);
        if (plan.Problems.Count == 0 && plan.Steps.Count == 0) plan.Problems.Add("Nothing to rename: tick the folder or the files.");

        plan.Changes = new MediaRenameChanges(item, plan.NewDatabaseName, plan.NewReleaseYear,
            plan.Steps.Where(s => s.Kind == MediaRenameStepKind.Folder).ToDictionary(s => s.From, s => s.To, StringComparer.OrdinalIgnoreCase),
            plan.Steps.Where(s => s.Kind == MediaRenameStepKind.File).ToDictionary(s => s.From, s => Path.GetFileName(s.To), StringComparer.OrdinalIgnoreCase));
        DescribeDatabaseChanges(plan, item);
        return plan;
    }

    /// <summary>The name a rename starts from: a TV show's folder-style <c>Name (Year)</c>, otherwise the row's name.</summary>
    public static string CurrentName(IEntity item) => item is TVShow { ReleaseYear: > 0 } show ? $"{show.Name} ({show.ReleaseYear})" : item.Name;

    private void PlanMovie(MediaRenamePlan plan, Movie movie)
    {
        plan.Warnings.Add(PlexWarning);
        string folder = movie.Folder ?? "";
        bool ownFolder = string.Equals(Path.GetFileName(folder), movie.Name, StringComparison.OrdinalIgnoreCase);
        string newName = plan.NewName;
        plan.NewDatabaseName = movie.Name;

        if (ownFolder && plan.Request.RenameFiles) RenameMovieFiles(plan, folder, movie, newName);
        if (!ownFolder)
        {
            // A loose movie (a file in a source folder) is named after its file.
            if (!plan.Request.RenameFiles)
            {
                plan.Problems.Add("This movie isn't in a folder of its own: it is renamed by its file, so tick \"Rename the files\".");
                return;
            }
            RenameMovieFiles(plan, folder, movie, newName);
            plan.NewDatabaseName = newName;
        }
        else if (plan.Request.RenameFolder)
        {
            plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.Folder, folder, Sibling(folder, newName)));
            plan.NewDatabaseName = newName;
        }
        else
        {
            plan.Warnings.Add($"Its name comes from its folder, so it stays {movie.Name} until the folder is renamed.");
        }
    }

    /// <summary>The main video file becomes <c>NewName.ext</c>; other files named like the movie (or like the main file) take the new name's start too.</summary>
    private static void RenameMovieFiles(MediaRenamePlan plan, string folder, Movie movie, string newName)
    {
        string? main = movie.OriginalFilename;
        string? mainStem = main == null ? null : Path.GetFileNameWithoutExtension(main);
        foreach (string file in Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase))
        {
            string? renamed = string.Equals(file, main, StringComparison.OrdinalIgnoreCase) ? newName + Path.GetExtension(file)
                : RenamePrefixed(file, movie.Name, newName) ?? (mainStem == null ? null : RenamePrefixed(file, mainStem, newName));
            if (renamed != null && renamed != file) plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.File, Path.Combine(folder, file), Path.Combine(folder, renamed)));
        }
        if (main != null && !File.Exists(Path.Combine(folder, main))) plan.Problems.Add($"Its file {main} isn't in {folder} any more: scan first.");
    }

    private void PlanShow(MediaRenamePlan plan, TVShow show)
    {
        plan.Warnings.Add(PlexWarning);
        string folder = show.Folder ?? "";
        string oldFolderName = Path.GetFileName(folder);
        (string name, int year) = MediaFileNames.ParseNameAndYear(plan.NewName);
        plan.NewDatabaseName = show.Name;
        plan.NewReleaseYear = show.ReleaseYear;
        if (!Directory.Exists(folder))
        {
            plan.Problems.Add($"Its folder {folder} isn't there any more: scan first.");
            return;
        }

        if (plan.Request.RenameFiles)
        {
            // The show-name part of each episode file ("Firefly (2002) - s01e01 - Serenity.mkv"); files without an episode number keep their name,
            // because their whole name is their title.
            int left = 0;
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.GetFileName(path);
                if (MediaFileNames.EpisodeMarkerPrefix(Path.GetFileNameWithoutExtension(file)) == null)
                {
                    if (MediaFileNames.IsVideoFile(file)) left++;
                    continue;
                }
                string? renamed = RenamePrefixed(file, oldFolderName, plan.NewName) ?? RenamePrefixed(file, show.Name, plan.NewName);
                if (renamed != null && renamed != file) plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.File, path, Path.Combine(Path.GetDirectoryName(path)!, renamed)));
            }
            if (left > 0) plan.Warnings.Add($"{left} video file(s) without an episode number (sNNeNN) keep their names: their name is their title.");
        }

        if (plan.Request.RenameFolder)
        {
            plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.Folder, folder, Sibling(folder, plan.NewName)));
            (plan.NewDatabaseName, plan.NewReleaseYear) = (name, year);
        }
        else
        {
            plan.Warnings.Add($"Its name comes from its folder, so it stays {CurrentName(show)} until the folder is renamed.");
        }
    }

    private static void PlanEpisode(MediaRenamePlan plan, TVShowEpisode episode)
    {
        plan.Warnings.Add(PlexWarning);
        string folder = episode.Folder ?? "", file = episode.OriginalFilename ?? "";
        string stem = Path.GetFileNameWithoutExtension(file), seasonFolder = Path.GetFileName(folder);
        (int season, int number, _) = MediaFileNames.ParseEpisodeFileName(file, seasonFolder);
        string? prefix = MediaFileNames.EpisodeMarkerPrefix(stem);
        string newStem = prefix == null ? plan.NewName : $"{prefix} - {plan.NewName}";

        RenameFileAndCompanions(plan, folder, file, newStem);
        plan.NewDatabaseName = plan.NewName;
        if (MediaFileNames.ParseEpisodeFileName(newStem + Path.GetExtension(file), seasonFolder) != (season, number, plan.NewName))
        {
            plan.Problems.Add($"The new file name {newStem}{Path.GetExtension(file)} wouldn't be read back as season {season}, episode {number}, \"{plan.NewName}\".");
        }
    }

    private static void PlanTrack(MediaRenamePlan plan, Track track)
    {
        string folder = track.Folder ?? "", file = track.OriginalFilename ?? "";
        string stem = Path.GetFileNameWithoutExtension(file);
        (int number, string title) = MediaFileNames.ParseTrackFileName(file);
        string prefix = stem.EndsWith(title, StringComparison.Ordinal) ? stem[..^title.Length] : number > 0 ? $"{number:00} - " : "";
        string newStem = prefix + plan.NewName;

        RenameFileAndCompanions(plan, folder, file, newStem);
        plan.NewDatabaseName = plan.NewName;
        if (MediaFileNames.ParseTrackFileName(newStem + Path.GetExtension(file)) != (number, plan.NewName))
        {
            plan.Problems.Add($"The new file name {newStem}{Path.GetExtension(file)} wouldn't be read back as track {number}, \"{plan.NewName}\".");
        }
    }

    /// <summary>Renames a media file to <paramref name="newStem"/> (keeping its extension), and files beside it with the same name start (subtitles, artwork).</summary>
    private static void RenameFileAndCompanions(MediaRenamePlan plan, string folder, string file, string newStem)
    {
        if (!File.Exists(Path.Combine(folder, file)))
        {
            plan.Problems.Add($"Its file {file} isn't in {folder} any more: scan first.");
            return;
        }
        string stem = Path.GetFileNameWithoutExtension(file);
        foreach (string other in Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase))
        {
            string? renamed = RenamePrefixed(other, stem, newStem);
            if (renamed != null && renamed != other) plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.File, Path.Combine(folder, other), Path.Combine(folder, renamed)));
        }
    }

    private static void PlanFolderRename(MediaRenamePlan plan, string? folder)
    {
        if (folder == null || !Directory.Exists(folder))
        {
            plan.Problems.Add($"Its folder {folder} isn't there any more: scan first.");
            return;
        }
        plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.Folder, folder, Sibling(folder, plan.NewName)));
        plan.NewDatabaseName = plan.NewName;
    }

    /// <summary>An artist's folder in every music category it is in: its own folder, and the folder above each of its albums, where named after it.</summary>
    private async Task PlanArtistAsync(MediaRenamePlan plan, Artist artist, CancellationToken cancellationToken)
    {
        List<Album> albums = await _albumService.GetAllRecordsByArtistIdAsync(artist.Id, cancellationToken).ConfigureAwait(false);
        List<string> folders = new[] { artist.Folder }.Concat(albums.Select(a => a.Folder == null ? null : Path.GetDirectoryName(a.Folder)))
            .OfType<string>()
            .Where(f => string.Equals(Path.GetFileName(f), artist.Name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folders.Count == 0)
        {
            plan.Problems.Add("None of its folders are there any more: scan first.");
            return;
        }
        foreach (string folder in folders) plan.Steps.Add(new MediaRenameStep(MediaRenameStepKind.Folder, folder, Sibling(folder, plan.NewName)));
        plan.NewDatabaseName = plan.NewName;
        if (folders.Count > 1) plan.Warnings.Add($"It is in {folders.Count} music categories: its folder is renamed in each.");
    }

    // Checks.
    // ==================================================================================================

    /// <summary>Why a name can't be used for a Windows file or folder, or null if it can.</summary>
    public static string? CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Type a name.";
        if (name.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || name.Any(char.IsControl))
        {
            return "A name can't contain < > : \" / \\ | ? or *.";
        }
        if (name.EndsWith('.') || name.EndsWith(' ')) return "A name can't end with a dot or a space.";
        if (ReservedNames.Contains(name.Split('.')[0].Trim(), StringComparer.OrdinalIgnoreCase)) return $"Windows doesn't allow the name {name}.";
        return null;
    }

    /// <summary>Each step's source must be there and its target mustn't (a change of case only is fine); long paths are a warning.</summary>
    private static void CheckSteps(MediaRenamePlan plan)
    {
        foreach (MediaRenameStep step in plan.Steps)
        {
            bool isFolder = step.Kind == MediaRenameStepKind.Folder;
            if (!(isFolder ? Directory.Exists(step.From) : File.Exists(step.From))) plan.Problems.Add($"{step.From} isn't there any more: scan first.");
            bool caseOnly = string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (Directory.Exists(step.To) || File.Exists(step.To))) plan.Problems.Add($"{step.To} already exists.");
            if (step.To.Length >= 260) plan.Warnings.Add($"{Path.GetFileName(step.To)}: the full path is {step.To.Length} characters, which some programs can't open.");
        }
    }

    /// <summary>Another live row with the new name (the scan matches rows by it) stops the rename.</summary>
    private async Task CheckDatabaseNameAsync(MediaRenamePlan plan, IEntity item, CancellationToken cancellationToken)
    {
        string name = plan.NewDatabaseName;
        if (name == item.Name && (item is not TVShow show || plan.NewReleaseYear == show.ReleaseYear)) return;

        IEntity? same = item switch
        {
            Artist => await _artistService.FindSameRecordAsync(new Artist { Name = name }, cancellationToken).ConfigureAwait(false),
            Album a => await _albumService.FindSameRecordAsync(new Album { ArtistId = a.ArtistId, Name = name }, cancellationToken).ConfigureAwait(false),
            Track t => await _trackService.FindSameRecordAsync(new Track { AlbumId = t.AlbumId, TrackNo = t.TrackNo, Name = name }, cancellationToken).ConfigureAwait(false),
            Movie => await _movieService.FindSameRecordAsync(new Movie { Name = name }, cancellationToken).ConfigureAwait(false),
            TVShow => await _tvShowService.FindSameRecordAsync(new TVShow { Name = name, ReleaseYear = plan.NewReleaseYear ?? 0 }, cancellationToken).ConfigureAwait(false),
            TVShowEpisode { EpisodeNo: 0 } e => await _episodeService.FindSameRecordAsync(
                new TVShowEpisode { TVShowId = e.TVShowId, SeasonNo = e.SeasonNo, EpisodeNo = 0, Name = name }, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
        if (same != null && same.Id != item.Id && same is not ISoftDeletable { IsDeleted: true })
        {
            plan.Problems.Add($"Another {Describe(item)} is already called {name} (id {same.Id}).");
        }
    }

    private static void DescribeDatabaseChanges(MediaRenamePlan plan, IEntity item)
    {
        if (plan.NewDatabaseName != item.Name) plan.DatabaseChanges.Add($"Name: {item.Name} → {plan.NewDatabaseName}");
        if (item is TVShow show && plan.NewReleaseYear != show.ReleaseYear) plan.DatabaseChanges.Add($"Release year: {show.ReleaseYear} → {plan.NewReleaseYear}");
        int folders = plan.Steps.Count(s => s.Kind == MediaRenameStepKind.Folder), files = plan.Steps.Count - folders;
        if (folders > 0) plan.DatabaseChanges.Add("Folder paths of this row and every row under the renamed folder(s)");
        if (files > 0) plan.DatabaseChanges.Add($"File names of the rows whose files are renamed ({files} file(s) on disk)");
        plan.DatabaseChanges.Add("Id, favourites, notes and the other fields stay as they are");
    }

    private static string Describe(IEntity item) => item switch
    {
        Artist => "artist",
        Album => "album by this artist",
        Track => "track with this number on this album",
        Movie => "movie",
        TVShow => "TV show",
        _ => "episode in this season",
    };

    /// <summary>
    /// <paramref name="file"/> with its start changed from <paramref name="oldStart"/> to <paramref name="newStart"/>, when it starts with it as a whole name
    /// (followed by the extension, a space, '.', '-', '_', '(' or '['; ignoring case); null when it doesn't.
    /// </summary>
    internal static string? RenamePrefixed(string file, string oldStart, string newStart)
    {
        if (oldStart.Length == 0 || !file.StartsWith(oldStart, StringComparison.OrdinalIgnoreCase)) return null;
        if (file.Length > oldStart.Length && " .-_([".IndexOf(file[oldStart.Length]) < 0) return null;
        return newStart + file[oldStart.Length..];
    }

    private static string Sibling(string folder, string name) => Path.Combine(Path.GetDirectoryName(folder.TrimEnd('\\', '/'))!, name);

    /// <summary>The row as stored now (live rows only), e.g. to show it after a rename; null if it isn't there.</summary>
    public Task<IEntity?> GetCurrentAsync(IEntity item, CancellationToken cancellationToken = default) => ReloadAsync(item, cancellationToken);

    private async Task<IEntity?> ReloadAsync(IEntity item, CancellationToken cancellationToken) => item switch
    {
        Artist => await _artistService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        Album => await _albumService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        Track => await _trackService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        Movie => await _movieService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        TVShow => await _tvShowService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        TVShowEpisode => await _episodeService.GetRecordByIdAsync(item.Id, cancellationToken).ConfigureAwait(false),
        _ => null,
    };

    // Applying.
    // ==================================================================================================

    /// <summary>Applies a plan: see <see cref="ApplyAsync"/>.</summary>
    public MediaRenameResult Apply(MediaRenamePlan plan) => ApplyAsync(plan).GetAwaiter().GetResult();

    /// <summary>
    /// Renames on disk (files first, then folders), then updates the database in one save. If a disk step or the database update fails, the disk renames
    /// already done are put back (any that can't be are listed in the result). Nothing is done if the plan has problems, a scan is running, or the disk has
    /// changed since the plan was made.
    /// </summary>
    public async Task<MediaRenameResult> ApplyAsync(MediaRenamePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply || plan.Changes == null) return new MediaRenameResult(false, "The rename has problems: " + string.Join(" ", plan.Problems), []);

        using IDisposable? reservation = _scanner.TryReserve();
        if (reservation == null) return new MediaRenameResult(false, "A media scan (or another rename) is running: rename when it has finished.", []);

        // The disk may have changed since the preview.
        var check = new MediaRenamePlan(plan.Request, plan.Item, plan.OldName);
        check.Steps.AddRange(plan.Steps);
        CheckSteps(check);
        if (check.Problems.Count > 0) return new MediaRenameResult(false, "The folders have changed since the preview: " + string.Join(" ", check.Problems), []);

        var done = new List<MediaRenameStep>();
        try
        {
            foreach (MediaRenameStep step in plan.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BeforeStep?.Invoke(step);
                Move(step.Kind, step.From, step.To);
                done.Add(step);
            }
        }
        catch (Exception ex)
        {
            List<string> failures = RollBack(done);
            _logger.LogWarning("Rename of {Name} stopped: {Message}", plan.OldName, ex.Message);
            return new MediaRenameResult(false, $"Couldn't rename: {ex.Message} " + RolledBackText(done, failures), failures);
        }

        try
        {
            await _pathService.ApplyRenameAsync(plan.Changes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            List<string> failures = RollBack(done);
            _logger.LogError(ex, "Rename of {Name}: the database update failed", plan.OldName);
            return new MediaRenameResult(false, $"The database update failed: {ex.GetBaseException().Message} " + RolledBackText(done, failures), failures);
        }

        _logger.LogInformation("Renamed {Name} to {NewName} ({Steps} folders / files)", plan.OldName, plan.NewName, plan.Steps.Count);
        return new MediaRenameResult(true, $"Renamed {plan.OldName} to {plan.NewName}", []);
    }

    /// <summary>Puts the steps done back, last first; returns those that couldn't be.</summary>
    private List<string> RollBack(List<MediaRenameStep> done)
    {
        var failures = new List<string>();
        for (int i = done.Count - 1; i >= 0; i--)
        {
            MediaRenameStep step = done[i];
            try
            {
                Move(step.Kind, step.To, step.From);
            }
            catch (Exception ex)
            {
                failures.Add($"{step.To} couldn't be renamed back to {Path.GetFileName(step.From)}: {ex.Message}");
                _logger.LogError("Rename rollback: {To} couldn't be renamed back to {From}: {Message}", step.To, step.From, ex.Message);
            }
        }
        return failures;
    }

    private static string RolledBackText(List<MediaRenameStep> done, List<string> failures) =>
        done.Count == 0 ? "Nothing was changed."
        : failures.Count == 0 ? "Everything renamed so far was put back; nothing was changed."
        : $"{failures.Count} rename(s) couldn't be put back (see the list); a scan will catch up with what is on disk.";

    /// <summary>Renames a folder or file; a change of case only goes through a temporary name (Windows treats the names as the same).</summary>
    private static void Move(MediaRenameStepKind kind, string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && from != to)
        {
            string temporary = to + ".renaming-" + Guid.NewGuid().ToString("N")[..8];
            Move(kind, from, temporary);
            Move(kind, temporary, to);
            return;
        }
        if (kind == MediaRenameStepKind.Folder) Directory.Move(from, to);
        else File.Move(from, to);
    }
}
