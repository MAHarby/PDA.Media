using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The media library's Search tab: search the ticked media categories by Name, Description and Notes, browse the results as a tree
/// (Artist → Album → Track, source folder → Movie, Show → Season → Episode; branches load when opened), see a row's details and edit the fields
/// that can be edited by hand (Save Changes / Discard).
/// </summary>
public partial class MediaSearchViewModel : ViewModelBase
{
    /// <summary>Paths opened automatically after a search (each opening reads a branch).</summary>
    private const int MaxPathsOpened = 50;

    private readonly MediaScanner _scanner;
    private readonly MediaSearchService _searchService;
    private readonly AlbumTypeService _albumTypeService;
    private readonly MovieTypeService _movieTypeService;
    private readonly TVShowTypeService _tvShowTypeService;
    private readonly ILogger<MediaSearchViewModel> _logger;
    private Dictionary<Type, IReadOnlyList<LookupOption>>? _typeOptions;
    private CancellationTokenSource? _detailsLoad;
    private Action? _onConfirm;
    private Action? _onDismiss;
    private bool _closeWhenDone;

    public MediaSearchViewModel(MediaScanner scanner, MediaSearchService searchService, AlbumTypeService albumTypeService, MovieTypeService movieTypeService,
        TVShowTypeService tvShowTypeService, ILogger<MediaSearchViewModel>? logger = null)
    {
        _scanner = scanner;
        _searchService = searchService;
        _albumTypeService = albumTypeService;
        _movieTypeService = movieTypeService;
        _tvShowTypeService = tvShowTypeService;
        _logger = logger ?? NullLogger<MediaSearchViewModel>.Instance;
    }

    /// <summary>Raised when the window should close: it was closed with unsaved changes, and they have now been saved or discarded.</summary>
    public event Action? CloseRequested;

    // Categories and criteria.
    // ==================================================================================================

    /// <summary>The media categories that can be searched (known media class and a root folder); the active ones are ticked to start with.</summary>
    public ObservableCollection<SearchCategoryItem> Categories { get; } = [];

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool FavouritesOnly { get; set; }
    [ObservableProperty] public partial bool ShowDeleted { get; set; }
    [ObservableProperty] public partial bool IsSearching { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Type a name, description or note to search for (* lists everything)";

    [RelayCommand]
    private async Task LoadCategoriesAsync()
    {
        try
        {
            List<MediaScanCategory> categories = await Task.Run(() => _scanner.GetCategoriesAsync());
            Categories.Clear();
            foreach (MediaScanCategory category in categories.Where(c => c.CanScan))
            {
                Categories.Add(new SearchCategoryItem(category.Name, category.Kind, category.Category.RootFolder!.Trim().TrimEnd('\\', '/'), category.Category.IsActive));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't load the media categories");
            StatusText = "Couldn't load the media categories (is the database there?)";
        }
    }

    // Search and tree.
    // ==================================================================================================

    /// <summary>The top of the tree: a group for each kind of media found.</summary>
    public ObservableCollection<MediaTreeNode> Results { get; } = [];

    [ObservableProperty] public partial MediaTreeNode? SelectedNode { get; set; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        List<SearchCategoryItem> ticked = Categories.Where(c => c.IsSelected).ToList();
        if (ticked.Count == 0)
        {
            StatusText = "Tick at least one media category to search";
            return;
        }

        var criteria = new MediaSearchCriteria
        {
            Text = SearchText.Trim(),
            MusicRoots = Roots(ticked, MediaKind.Music),
            MovieRoots = Roots(ticked, MediaKind.Movie),
            TVRoots = Roots(ticked, MediaKind.TV),
            FavouritesOnly = FavouritesOnly,
            IncludeDeleted = ShowDeleted,
        };
        if (string.IsNullOrEmpty(criteria.Text) && !criteria.FavouritesOnly)
        {
            StatusText = "Type something to search for, or * to list everything";
            return;
        }

        IsSearching = true;
        StatusText = "Searching...";
        try
        {
            _typeOptions ??= await LoadTypeOptionsAsync();
            MediaSearchResult result = await Task.Run(() => _searchService.SearchAsync(criteria));
            SelectedNode = null;
            Results.Clear();
            await BuildTreeAsync(result, criteria, ticked);
            StatusText = Summarise(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media search failed");
            StatusText = $"Search failed: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static List<string> Roots(IEnumerable<SearchCategoryItem> categories, MediaKind kind) =>
        categories.Where(c => c.Kind == kind).Select(c => c.RootFolder).ToList();

    private static string Summarise(MediaSearchResult result)
    {
        var parts = new List<string>();
        if (result.Artists.Count > 0) parts.Add(Count(result.Artists.Count, "artist"));
        if (result.Movies.Count > 0) parts.Add(Count(result.Movies.Count, "movie"));
        if (result.Shows.Count > 0) parts.Add(Count(result.Shows.Count, "TV show"));
        string text = parts.Count == 0 ? "Nothing found" : "Found " + string.Join(", ", parts);
        return result.IsTruncated ? text + " (not all shown: narrow the search)" : text;
    }

    private static string Count(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";

    private async Task BuildTreeAsync(MediaSearchResult result, MediaSearchCriteria criteria, List<SearchCategoryItem> ticked)
    {
        HashSet<int> matchedTrackIds = result.MatchedTracks.Select(t => t.Id).ToHashSet();
        HashSet<int> matchedEpisodeIds = result.MatchedEpisodes.Select(e => e.Id).ToHashSet();
        var pathNodes = new List<MediaTreeNode>();

        if (result.Artists.Count > 0)
        {
            var music = new MediaTreeNode(MediaNodeKind.Group, "Music") { Subtitle = Count(result.Artists.Count, "artist"), IsExpanded = true };
            HashSet<int> pathArtistIds = result.PathAlbums.Select(a => a.ArtistId).ToHashSet();
            HashSet<int> pathAlbumIds = result.PathAlbums.Select(a => a.Id).ToHashSet();
            foreach (Artist artist in result.Artists)
            {
                MediaTreeNode node = ArtistNode(artist, criteria, result.MatchedAlbumIds, matchedTrackIds, pathAlbumIds);
                node.IsMatch = result.MatchedArtistIds.Contains(artist.Id);
                music.AddChild(node);
                if (pathArtistIds.Contains(artist.Id)) pathNodes.Add(node);
            }
            Results.Add(music);
        }

        if (result.Movies.Count > 0)
        {
            var movies = new MediaTreeNode(MediaNodeKind.Group, "Movies") { Subtitle = Count(result.Movies.Count, "movie"), IsExpanded = true };
            bool severalCategories = ticked.Count(c => c.Kind == MediaKind.Movie) > 1;
            foreach (var source in result.Movies.GroupBy(m => SourceOf(m, ticked)).OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var sourceNode = new MediaTreeNode(MediaNodeKind.Source, source.Key.Name)
                {
                    Subtitle = Count(source.Count(), "movie") + (severalCategories ? $" · {source.Key.Category}" : ""),
                    IsExpanded = !result.IsBrowse,
                };
                foreach (Movie movie in source) sourceNode.AddChild(new MediaTreeNode(MediaNodeKind.Movie, movie.Name, movie) { IsMatch = !result.IsBrowse });
                movies.AddChild(sourceNode);
            }
            Results.Add(movies);
        }

        if (result.Shows.Count > 0)
        {
            var tv = new MediaTreeNode(MediaNodeKind.Group, "TV shows") { Subtitle = Count(result.Shows.Count, "show"), IsExpanded = true };
            HashSet<int> pathShowIds = result.MatchedEpisodes.Select(e => e.TVShowId).ToHashSet();
            foreach (TVShow show in result.Shows)
            {
                MediaTreeNode node = ShowNode(show, criteria, matchedEpisodeIds);
                node.IsMatch = result.MatchedShowIds.Contains(show.Id);
                tv.AddChild(node);
                if (pathShowIds.Contains(show.Id)) pathNodes.Add(node);
            }
            Results.Add(tv);
        }

        // Open the way down to the matches below the top level: artists to the albums holding matching tracks, shows to the seasons holding matching episodes.
        foreach (MediaTreeNode node in pathNodes.Take(MaxPathsOpened))
        {
            node.IsExpanded = true;
            await node.LoadChildrenAsync();
            foreach (MediaTreeNode child in node.Children.Where(c => c.IsOnMatchPath))
            {
                child.IsExpanded = true;
                await child.LoadChildrenAsync();
            }
        }
    }

    /// <summary>The movie source folder a movie is in (the first folder under its category's root) and that category's name.</summary>
    private static (string Name, string Category) SourceOf(Movie movie, IEnumerable<SearchCategoryItem> categories)
    {
        foreach (SearchCategoryItem category in categories.Where(c => c.Kind == MediaKind.Movie))
        {
            string folder = movie.Folder ?? "";
            if (string.Equals(folder, category.RootFolder, StringComparison.OrdinalIgnoreCase)) return ("(loose files)", category.Name);
            string prefix = category.RootFolder + Path.DirectorySeparatorChar;
            if (folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string relative = folder[prefix.Length..];
                int separator = relative.IndexOfAny(['\\', '/']);
                return (separator < 0 ? relative : relative[..separator], category.Name);
            }
        }
        return ("(other)", "");
    }

    private MediaTreeNode ArtistNode(Artist artist, MediaSearchCriteria criteria, HashSet<int> matchedAlbumIds, HashSet<int> matchedTrackIds, HashSet<int> pathAlbumIds) =>
        new(MediaNodeKind.Artist, artist.Name, artist, async () =>
        {
            List<Album> albums = await Task.Run(() => _searchService.GetArtistAlbumsAsync(artist.Id, criteria.MusicRoots, criteria.IncludeDeleted));
            return albums.Select(album => new MediaTreeNode(MediaNodeKind.Album, album.Name, album, async () =>
                {
                    List<Track> tracks = await Task.Run(() => _searchService.GetAlbumTracksAsync(album.Id, criteria.IncludeDeleted));
                    return tracks.Select(track => new MediaTreeNode(MediaNodeKind.Track, TrackTitle(track), track) { IsMatch = matchedTrackIds.Contains(track.Id) }).ToList();
                })
            {
                Subtitle = Count(album.TrackCount, "track"),
                IsMatch = matchedAlbumIds.Contains(album.Id),
                IsOnMatchPath = pathAlbumIds.Contains(album.Id) && !matchedAlbumIds.Contains(album.Id),
            })
                .ToList();
        });

    private static string TrackTitle(Track track) => track.TrackNo > 0 ? $"{track.TrackNo:00}  {track.Name}" : track.Name;

    private MediaTreeNode ShowNode(TVShow show, MediaSearchCriteria criteria, HashSet<int> matchedEpisodeIds) =>
        new(MediaNodeKind.Show, show.ReleaseYear > 0 ? $"{show.Name} ({show.ReleaseYear})" : show.Name, show, async () =>
        {
            List<TVShowEpisode> episodes = await Task.Run(() => _searchService.GetShowEpisodesAsync(show.Id, criteria.IncludeDeleted));
            return episodes.GroupBy(e => e.SeasonNo).Select(season =>
            {
                var seasonNode = new MediaTreeNode(MediaNodeKind.Season, season.Key == 0 ? "Specials" : $"Season {season.Key}")
                {
                    Subtitle = Count(season.Count(), "episode") + MissingEpisodes(season),
                    IsOnMatchPath = season.Any(e => matchedEpisodeIds.Contains(e.Id)),
                };
                foreach (TVShowEpisode episode in season)
                {
                    seasonNode.AddChild(new MediaTreeNode(MediaNodeKind.Episode, episode.Name, episode)
                    {
                        Subtitle = episode.EpisodeNo > 0 ? $"s{episode.SeasonNo:00}e{episode.EpisodeNo:00}" : null,
                        IsMatch = matchedEpisodeIds.Contains(episode.Id),
                    });
                }
                return seasonNode;
            }).ToList();
        });

    /// <summary>" · missing 3, 5" when a season's episode numbers have gaps (up to its highest number).</summary>
    private static string MissingEpisodes(IEnumerable<TVShowEpisode> season)
    {
        HashSet<int> numbers = season.Select(e => e.EpisodeNo).Where(n => n > 0).ToHashSet();
        if (numbers.Count == 0) return "";
        List<int> missing = Enumerable.Range(1, numbers.Max()).Where(n => !numbers.Contains(n)).ToList();
        return missing.Count == 0 ? "" : " · missing " + string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : "");
    }

    private async Task<Dictionary<Type, IReadOnlyList<LookupOption>>> LoadTypeOptionsAsync()
    {
        List<AlbumType> albumTypes = await Task.Run(() => _albumTypeService.GetAllRecordsAsync());
        List<MovieType> movieTypes = await Task.Run(() => _movieTypeService.GetAllRecordsAsync());
        List<TVShowType> showTypes = await Task.Run(() => _tvShowTypeService.GetAllRecordsAsync());
        return new Dictionary<Type, IReadOnlyList<LookupOption>>
        {
            [typeof(Album)] = albumTypes.Select(t => new LookupOption(t.Id, t.Name)).ToList(),
            [typeof(Movie)] = movieTypes.Select(t => new LookupOption(t.Id, t.Name)).ToList(),
            [typeof(TVShow)] = showTypes.Select(t => new LookupOption(t.Id, t.Name)).ToList(),
        };
    }

    // Details.
    // ==================================================================================================

    /// <summary>The selected row's editable fields; null for a heading (group, source, season).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    public partial MediaItemEditor? Editor { get; set; }

    public bool HasEditor => Editor != null;
    public bool HasSelection => SelectedNode != null && SelectedNode.Kind != MediaNodeKind.Message;

    [ObservableProperty] public partial string DetailsTitle { get; set; } = "";
    [ObservableProperty] public partial string DetailsSubtitle { get; set; } = "";
    public ObservableCollection<DetailRow> DetailRows { get; } = [];

    /// <summary>The media file's size and date (or the folder's), read from disk when the row is selected.</summary>
    [ObservableProperty] public partial string FileText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoverArt))]
    public partial Bitmap? CoverArt { get; set; }

    public bool HasCoverArt => CoverArt != null;

    /// <summary>The selected row's file (or folder) path, for Open folder / Copy path.</summary>
    public string? SelectedPath => MediaEntityInfo.Path(SelectedNode?.Entity);

    private MediaTreeNode? _shownNode;

    partial void OnSelectedNodeChanged(MediaTreeNode? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedPath));
        OpenFolderCommand.NotifyCanExecuteChanged();
        if (value == _shownNode) return;

        // Unsaved changes on the row shown so far: ask first, then show the new one.
        if (Editor is { HasChanges: true } && _shownNode != null)
        {
            MediaTreeNode previous = _shownNode;
            Ask(new ConfirmationPrompt("Save your changes?", $"You changed {previous.Title}. Save the changes before showing {value?.Title ?? "something else"}?",
                    "Save changes", "Discard"),
                async () =>
                {
                    await SaveChangesAsync();
                    ShowDetails(value);
                },
                () => ShowDetails(value));
            return;
        }
        ShowDetails(value);
    }

    private void ShowDetails(MediaTreeNode? node)
    {
        _shownNode = node;
        if (Editor != null) Editor.PropertyChanged -= Editor_PropertyChanged;
        Editor = node?.Entity != null && _typeOptions != null
            ? new MediaItemEditor(node.Entity, _typeOptions.GetValueOrDefault(node.Entity.GetType()) ?? [])
            : node?.Entity != null ? new MediaItemEditor(node.Entity, []) : null;
        if (Editor != null) Editor.PropertyChanged += Editor_PropertyChanged;
        SaveChangesCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();

        DetailRows.Clear();
        DetailsTitle = node?.Title ?? "";
        DetailsSubtitle = node == null ? "" : Describe(node);
        if (node != null) foreach (DetailRow row in Rows(node)) DetailRows.Add(row);
        _ = LoadFromDiskAsync(node);
    }

    private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaItemEditor.HasChanges)) return;
        SaveChangesCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(MediaTreeNode node)
    {
        string kind = node.Kind switch
        {
            MediaNodeKind.Track => "Track",
            MediaNodeKind.Album => "Album",
            MediaNodeKind.Artist => "Artist",
            MediaNodeKind.Movie => "Movie",
            MediaNodeKind.Show => "TV show",
            MediaNodeKind.Episode => "Episode",
            MediaNodeKind.Season => "Season",
            MediaNodeKind.Source => "Movie source folder",
            _ => "Group",
        };
        // "Album · Queen", "Episode · Firefly · Season 1"
        IEnumerable<string> parents = node.Path().Where(n => n != node && n.Kind != MediaNodeKind.Group).Select(n => n.Title);
        return string.Join(" · ", new[] { kind }.Concat(parents)) + (node.IsDeleted ? " · deleted" : "");
    }

    private static IEnumerable<DetailRow> Rows(MediaTreeNode node)
    {
        switch (node.Entity)
        {
            case Artist a:
                yield return new DetailRow("Folder", a.Folder ?? "");
                break;
            case Album a:
                yield return new DetailRow("Folder", a.Folder ?? "");
                yield return new DetailRow("Tracks", a.TrackCount.ToString(CultureInfo.CurrentCulture));
                yield return new DetailRow("Cover art", a.CoverArtFilename ?? "none");
                break;
            case Track t:
                yield return new DetailRow("Track number", t.TrackNo.ToString(CultureInfo.CurrentCulture));
                yield return new DetailRow("Folder", t.Folder ?? "");
                yield return new DetailRow("File", t.OriginalFilename ?? "");
                break;
            case Movie m:
                yield return new DetailRow("Folder", m.Folder ?? "");
                yield return new DetailRow("File", m.OriginalFilename ?? "");
                yield return new DetailRow("Cover art", m.CoverArtFilename ?? "none");
                break;
            case TVShow s:
                yield return new DetailRow("Release year", s.ReleaseYear > 0 ? s.ReleaseYear.ToString(CultureInfo.InvariantCulture) : "unknown");
                yield return new DetailRow("Folder", s.Folder ?? "");
                yield return new DetailRow("Cover art", s.CoverArtFilename ?? "none");
                break;
            case TVShowEpisode e:
                yield return new DetailRow("Season / episode", e.EpisodeNo > 0 ? $"{e.SeasonNo} / {e.EpisodeNo}" : $"{e.SeasonNo} / (no number)");
                yield return new DetailRow("Folder", e.Folder ?? "");
                yield return new DetailRow("File", e.OriginalFilename ?? "");
                break;
            default:
                yield return new DetailRow("Contains", node.Subtitle ?? Count(node.Children.Count(c => !c.IsMessage), "item"));
                yield break;
        }

        if (node.Entity is IAuditable audit)
        {
            yield return new DetailRow("Added", $"{audit.CreatedOn:d MMM yyyy HH:mm} by {audit.CreatedBy}");
            yield return new DetailRow("Changed", $"{audit.ModifiedOn:d MMM yyyy HH:mm} by {audit.ModifiedBy}");
        }
        yield return new DetailRow("Id", node.Entity.Id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads the file's size and date and the cover art off the UI thread (the NAS can be slow); a newer selection cancels it.</summary>
    private async Task LoadFromDiskAsync(MediaTreeNode? node)
    {
        _detailsLoad?.Cancel();
        _detailsLoad = new CancellationTokenSource();
        CancellationToken token = _detailsLoad.Token;
        FileText = "";
        CoverArt = null;
        if (node?.Entity == null) return;

        string? path = MediaEntityInfo.Path(node.Entity);
        string? coverPath = CoverArtPath(node);
        try
        {
            (string fileText, Bitmap? cover) = await Task.Run(() =>
            {
                string text = path == null ? ""
                    : MediaEntityInfo.FileName(node.Entity) != null
                        ? File.Exists(path) ? FileDescription(new FileInfo(path)) : "File not found"
                        : Directory.Exists(path) ? "Folder is there" : "Folder not found";
                Bitmap? bitmap = null;
                if (coverPath != null && File.Exists(coverPath))
                {
                    using FileStream stream = File.OpenRead(coverPath);
                    bitmap = Bitmap.DecodeToWidth(stream, 240);
                }
                return (text, bitmap);
            }, token);
            if (token.IsCancellationRequested) return;
            FileText = fileText;
            CoverArt = cover;
        }
        catch (OperationCanceledException)
        {
            // A newer selection.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) FileText = $"Couldn't read the file: {ex.GetBaseException().Message}";
        }
    }

    private static string FileDescription(FileInfo file) => $"{FileSize.Format(file.Length)}, changed {file.LastWriteTime:d MMM yyyy HH:mm}";

    /// <summary>The row's cover art, or its album's (track) or show's (episode).</summary>
    private static string? CoverArtPath(MediaTreeNode node)
    {
        for (MediaTreeNode? n = node; n != null; n = n.Parent)
        {
            string? cover = MediaEntityInfo.CoverArt(n.Entity);
            string? folder = MediaEntityInfo.Folder(n.Entity);
            if (cover != null && folder != null) return Path.Combine(folder, cover);
            if (n.Kind is MediaNodeKind.Album or MediaNodeKind.Show or MediaNodeKind.Movie) break;
        }
        return null;
    }

    // Editing.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    public partial bool IsSaving { get; set; }

    private bool CanSaveChanges() => Editor is { HasChanges: true } && !IsSaving;

    /// <summary>Saves the editable fields of the row shown (MediaSearchService.SaveEditableFields) and updates the tree.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private async Task SaveChangesAsync()
    {
        MediaItemEditor? editor = Editor;
        MediaTreeNode? node = _shownNode;
        if (editor == null || node?.Entity == null) return;

        IsSaving = true;
        try
        {
            var edited = (IEntity)Activator.CreateInstance(node.Entity.GetType())!;
            edited.Id = node.Entity.Id;
            editor.ApplyTo(edited);
            IEntity saved = await Task.Run(() => Save(edited));
            node.Entity = saved;
            if (_shownNode == node) ShowDetails(node);
            StatusText = $"Saved {node.Title}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't save {Name}", node.Title);
            StatusText = $"Couldn't save {node.Title}: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private Task<IEntity> Save(IEntity edited) => edited switch
    {
        Artist a => Typed(_searchService.SaveEditableFieldsAsync(a)),
        Album a => Typed(_searchService.SaveEditableFieldsAsync(a)),
        Track t => Typed(_searchService.SaveEditableFieldsAsync(t)),
        Movie m => Typed(_searchService.SaveEditableFieldsAsync(m)),
        TVShow s => Typed(_searchService.SaveEditableFieldsAsync(s)),
        TVShowEpisode e => Typed(_searchService.SaveEditableFieldsAsync(e)),
        _ => throw new NotSupportedException(edited.GetType().Name),
    };

    private static async Task<IEntity> Typed<T>(Task<T> task) where T : IEntity => await task;

    private bool CanDiscardChanges() => Editor is { HasChanges: true };

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private void DiscardChanges() => ShowDetails(_shownNode);

    private bool CanOpenFolder() => SelectedPath != null;

    /// <summary>Opens the row's folder in Explorer (selecting the file on Windows).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        string? path = SelectedPath;
        if (path == null) return;
        try
        {
            bool isFile = MediaEntityInfo.FileName(SelectedNode?.Entity) != null;
            if (OperatingSystem.IsWindows() && isFile) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start(new ProcessStartInfo(isFile ? Path.GetDirectoryName(path)! : path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't open the folder: {ex.GetBaseException().Message}";
        }
    }

    // Confirmations and closing.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    public partial ConfirmationPrompt? Confirmation { get; set; }

    public bool HasConfirmation => Confirmation != null;

    private void Ask(ConfirmationPrompt prompt, Action onConfirm, Action? onDismiss = null)
    {
        _onConfirm = onConfirm;
        _onDismiss = onDismiss;
        Confirmation = prompt;
    }

    private void Ask(ConfirmationPrompt prompt, Func<Task> onConfirm, Action? onDismiss = null) => Ask(prompt, (Action)(() => _ = onConfirm()), onDismiss);

    [RelayCommand]
    private void Confirm()
    {
        Action? action = _onConfirm;
        (_onConfirm, _onDismiss, Confirmation) = (null, null, null);
        action?.Invoke();
    }

    [RelayCommand]
    private void DismissConfirmation()
    {
        Action? action = _onDismiss;
        (_onConfirm, _onDismiss, Confirmation) = (null, null, null);
        action?.Invoke();
    }

    /// <summary>
    /// Called when the window is closing: true if it can close now. With unsaved changes it asks first (save or discard), then raises
    /// <see cref="CloseRequested"/>.
    /// </summary>
    public bool CanClose()
    {
        if (Editor is not { HasChanges: true } || _closeWhenDone) return true;
        if (Confirmation == null)
        {
            Ask(new ConfirmationPrompt("Save your changes?", $"You changed {_shownNode?.Title}. Save the changes before closing?", "Save changes", "Discard"),
                async () =>
                {
                    await SaveChangesAsync();
                    _closeWhenDone = true;
                    CloseRequested?.Invoke();
                },
                () =>
                {
                    _closeWhenDone = true;
                    CloseRequested?.Invoke();
                });
        }
        return false;
    }
}
