using System.CommandLine;
using System.Globalization;
using PDA.Media.CommandLine.Configuration;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using Spectre.Console;

namespace PDA.Media.CommandLine.Commands;

/// <summary>What <c>pda-media search</c> was asked.</summary>
/// <param name="Categories">Media category names (Music, Movies, ...); empty or "All" for every active category.</param>
/// <param name="Types">Album, movie or TV show type names (Single, Live, Concert, ...); empty or "All" for any type. Naming a type leaves out the
/// kinds of media with no type named (--type Single shows only music).</param>
internal sealed record SearchOptions(
    string? Text,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Types,
    bool Favourites = false,
    bool Deleted = false,
    int Max = 500,
    bool Expand = false,
    bool Paths = false);

/// <summary>
/// <c>pda-media search &lt;text&gt;</c>: the same search as Media Library Services (Name, Description and Notes, <c>*</c> as a wildcard) in the chosen
/// categories and types, shown as a coloured tree: Music → Artist → Album → Track, Movies → source folder → Movie, TV shows → Show → Season → Episode.
/// Matches are bold; the tree opens down to each match (<c>--expand</c> shows everything below the results). Paths (<c>--paths</c>) are mapped through
/// the config file's path mappings.
/// </summary>
internal static class SearchCommand
{
    public static Command Create(CliContext context, Option<string?> configOption)
    {
        var text = new Argument<string?>("text")
        {
            Description = "What to look for in names, descriptions and notes; * is a wildcard (quote it on Linux: 'abba*'), * alone lists everything",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var categories = new Option<string[]>("--category", "-c")
        {
            Description = "Media categories to search, e.g. Music,Movies (default: All)",
            AllowMultipleArgumentsPerToken = true,
        };
        var types = new Option<string[]>("--type", "-t")
        {
            Description = "Album, movie or TV show types, e.g. Single,Live (default: All; naming types leaves out the kinds of media with none named)",
            AllowMultipleArgumentsPerToken = true,
        };
        var favourites = new Option<bool>("--favourites", "-f") { Description = "Only favourites" };
        var deleted = new Option<bool>("--deleted") { Description = "Include rows a scan has soft-deleted (shown struck through)" };
        var max = new Option<int>("--max") { Description = "The most results of each kind", DefaultValueFactory = _ => 500 };
        var expand = new Option<bool>("--expand", "-e") { Description = "Show everything below the results (albums and tracks, seasons and episodes)" };
        var paths = new Option<bool>("--paths", "-p") { Description = "Show folders and files (mapped through the config file's path mappings)" };

        var command = new Command("search", "Search the media library.") { text, categories, types, favourites, deleted, max, expand, paths };
        command.SetAction((parse, cancellationToken) =>
        {
            var options = new SearchOptions(parse.GetValue(text), SplitList(parse.GetValue(categories)), SplitList(parse.GetValue(types)),
                parse.GetValue(favourites), parse.GetValue(deleted), parse.GetValue(max), parse.GetValue(expand), parse.GetValue(paths));
            return RunAsync(context, parse.GetValue(configOption), options, cancellationToken);
        });
        return command;
    }

    /// <summary>Names given as "Music,Movies", "-c Music -c Movies" or both.</summary>
    internal static List<string> SplitList(IEnumerable<string>? values) =>
        (values ?? []).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();

    public static async Task<int> RunAsync(CliContext context, string? configPath, SearchOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Text) && !options.Favourites)
        {
            context.Error.MarkupLine("[red]Say what to search for ('*' lists everything), or use --favourites.[/]");
            return PdaMediaCli.Failed;
        }

        string path = context.Store.ResolvePath(configPath);
        CliSettings settings;
        try
        {
            settings = context.Store.Load(path);
        }
        catch (InvalidDataException ex)
        {
            context.Error.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return PdaMediaCli.Failed;
        }

        try
        {
            var search = new Search(context, settings, options, new DataContextFactory(settings.BuildConnectionString()));
            return await search.RunAsync(cancellationToken);
        }
        catch (SearchException ex)
        {
            context.Error.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return PdaMediaCli.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Error.MarkupLine($"[red]Couldn't search {Markup.Escape(settings.Describe())}: {Markup.Escape(ex.GetBaseException().Message)}[/]");
            context.Error.MarkupLine($"[grey]Config file: {Markup.Escape(path)} (pda-media config --test checks the connection)[/]");
            return PdaMediaCli.Failed;
        }
    }

    /// <summary>A mistake in the options (an unknown category or type), shown without a stack trace.</summary>
    private sealed class SearchException(string message) : Exception(message);

    /// <summary>One search: works out the criteria, runs it and draws the tree.</summary>
    private sealed class Search(CliContext context, CliSettings settings, SearchOptions options, DataContextFactory factory)
    {
        private readonly MediaSearchService _search = new(factory);
        private readonly PathMapper _paths = new(settings.PathMappings);
        private Dictionary<int, string> _albumTypes = [], _movieTypes = [];
        private MediaSearchCriteria _criteria = new();
        private List<(MediaCategory Category, string Root)> _movieCategories = [];

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            _criteria = await BuildCriteriaAsync(cancellationToken);
            MediaSearchResult result = await _search.SearchAsync(_criteria, cancellationToken);

            int found = result.Artists.Count + result.Movies.Count + result.Shows.Count;
            if (result.Artists.Count > 0) context.Out.Write(await MusicTreeAsync(result, cancellationToken));
            if (result.Movies.Count > 0) context.Out.Write(MovieTree(result));
            if (result.Shows.Count > 0) context.Out.Write(await TVTreeAsync(result, cancellationToken));

            if (found == 0)
            {
                context.Out.MarkupLine("[grey]Nothing found.[/]");
                return PdaMediaCli.NothingFound;
            }
            var parts = new List<string>();
            if (result.Artists.Count > 0) parts.Add(Count(result.Artists.Count, "artist"));
            if (result.Movies.Count > 0) parts.Add(Count(result.Movies.Count, "movie"));
            if (result.Shows.Count > 0) parts.Add(Count(result.Shows.Count, "TV show"));
            context.Out.MarkupLine($"[grey]Found {string.Join(", ", parts)}.[/]"
                + (result.IsTruncated ? $" [yellow]Not everything is shown (more than {options.Max} of a kind): narrow the search or raise --max.[/]" : ""));
            return PdaMediaCli.Found;
        }

        private async Task<MediaSearchCriteria> BuildCriteriaAsync(CancellationToken cancellationToken)
        {
            List<MediaCategory> all = await new MediaCategoryService(factory).GetAllRecordsAsync(cancellationToken);
            List<MediaCategory> chosen;
            if (options.Categories.Count == 0 || options.Categories.Any(IsAll))
            {
                chosen = all.Where(c => c.IsActive && MediaScanner.CannotScanReason(c) == null).ToList();
            }
            else
            {
                chosen = [];
                foreach (string name in options.Categories)
                {
                    MediaCategory category = all.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new SearchException($"There is no media category called \"{name}\". The categories are: {string.Join(", ", all.Select(c => c.Name))}.");
                    if (MediaScanner.CannotScanReason(category) is { } reason) throw new SearchException($"{category.Name} can't be searched: {reason}.");
                    chosen.Add(category);
                }
            }

            List<string> RootsOf(MediaKind kind) => chosen.Where(c => MediaKinds.FromMediaClass(c.MediaClass) == kind).Select(Root).ToList();
            List<string> music = RootsOf(MediaKind.Music), movies = RootsOf(MediaKind.Movie), tv = RootsOf(MediaKind.TV);
            _movieCategories = chosen.Where(c => MediaKinds.FromMediaClass(c.MediaClass) == MediaKind.Movie).Select(c => (c, Root(c))).ToList();

            List<AlbumType> albumTypes = await new AlbumTypeService(factory).GetAllRecordsAsync(cancellationToken);
            List<MovieType> movieTypes = await new MovieTypeService(factory).GetAllRecordsAsync(cancellationToken);
            List<TVShowType> showTypes = await new TVShowTypeService(factory).GetAllRecordsAsync(cancellationToken);
            _albumTypes = albumTypes.ToDictionary(t => t.Id, t => t.Name);
            _movieTypes = movieTypes.ToDictionary(t => t.Id, t => t.Name);

            List<int>? albumTypeIds = null, movieTypeIds = null, showTypeIds = null;
            if (options.Types.Count > 0 && !options.Types.Any(IsAll))
            {
                bool Named(string name) => options.Types.Contains(name, StringComparer.OrdinalIgnoreCase);
                albumTypeIds = albumTypes.Where(t => Named(t.Name)).Select(t => t.Id).ToList();
                movieTypeIds = movieTypes.Where(t => Named(t.Name)).Select(t => t.Id).ToList();
                showTypeIds = showTypes.Where(t => Named(t.Name)).Select(t => t.Id).ToList();
                var known = albumTypes.Select(t => t.Name).Concat(movieTypes.Select(t => t.Name)).Concat(showTypes.Select(t => t.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (options.Types.FirstOrDefault(t => !known.Contains(t)) is { } unknown)
                {
                    throw new SearchException($"There is no album, movie or TV show type called \"{unknown}\". Album types: {string.Join(", ", albumTypes.Select(t => t.Name))}; "
                        + $"movie types: {string.Join(", ", movieTypes.Select(t => t.Name))}; TV show types: {string.Join(", ", showTypes.Select(t => t.Name))}.");
                }
                // A kind of media with no type named isn't searched.
                if (albumTypeIds.Count == 0) music = [];
                if (movieTypeIds.Count == 0) movies = [];
                if (showTypeIds.Count == 0) tv = [];
            }

            return new MediaSearchCriteria
            {
                Text = options.Text?.Trim(),
                MusicRoots = music,
                MovieRoots = movies,
                TVRoots = tv,
                AlbumTypeIds = albumTypeIds,
                MovieTypeIds = movieTypeIds,
                TVShowTypeIds = showTypeIds,
                FavouritesOnly = options.Favourites,
                IncludeDeleted = options.Deleted,
                MaxResults = Math.Max(1, options.Max),
            };
        }

        private static bool IsAll(string name) => name.Equals("All", StringComparison.OrdinalIgnoreCase);

        private static string Root(MediaCategory category) => category.RootFolder!.Trim().TrimEnd('\\', '/');

        // Trees.
        // ==================================================================================================

        private async Task<Tree> MusicTreeAsync(MediaSearchResult result, CancellationToken cancellationToken)
        {
            var tree = new Tree(Group("Music", Count(result.Artists.Count, "artist")));
            HashSet<int> matchedTracks = result.MatchedTracks.Select(t => t.Id).ToHashSet();
            foreach (Artist artist in result.Artists)
            {
                bool matched = result.MatchedArtistIds.Contains(artist.Id);
                TreeNode artistNode = tree.AddNode(Label(artist.Name, matched, artist.IsFavourite, artist.IsDeleted, null, artist.Folder));

                // The artist's albums when it matched (or with --expand), otherwise the ones on the way to a match.
                IEnumerable<Album> albums = matched || options.Expand
                    ? await _search.GetArtistAlbumsAsync(artist.Id, _criteria.MusicRoots, options.Deleted, _criteria.AlbumTypeIds, cancellationToken)
                    : result.PathAlbums.Where(a => a.ArtistId == artist.Id).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
                foreach (Album album in albums)
                {
                    string info = $"{_albumTypes.GetValueOrDefault(album.AlbumTypeId, "?")} · {Count(album.TrackCount, "track")}";
                    TreeNode albumNode = artistNode.AddNode(Label(album.Name, result.MatchedAlbumIds.Contains(album.Id), album.IsFavourite, album.IsDeleted, info, album.Folder));
                    IEnumerable<Track> tracks = options.Expand
                        ? await _search.GetAlbumTracksAsync(album.Id, options.Deleted, cancellationToken)
                        : result.MatchedTracks.Where(t => t.AlbumId == album.Id).OrderBy(t => t.TrackNo);
                    foreach (Track track in tracks)
                    {
                        string title = track.TrackNo > 0 ? $"{track.TrackNo:00} {track.Name}" : track.Name;
                        albumNode.AddNode(Label(title, matchedTracks.Contains(track.Id), track.IsFavourite, track.IsDeleted, null, FilePath(track.Folder, track.OriginalFilename)));
                    }
                }
            }
            return tree;
        }

        private Tree MovieTree(MediaSearchResult result)
        {
            var tree = new Tree(Group("Movies", Count(result.Movies.Count, "movie")));
            bool severalCategories = _movieCategories.Count > 1;
            foreach (var source in result.Movies.GroupBy(SourceOf).OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                string info = Count(source.Count(), "movie") + (severalCategories ? $" · {source.Key.Category}" : "");
                TreeNode sourceNode = tree.AddNode($"[blue]{Markup.Escape(source.Key.Name)}[/] [grey]({Markup.Escape(info)})[/]");

                // Movies in a collection folder (several films in one folder, e.g. Star Trek) go under a branch of their own, before the other movies.
                foreach (var collection in source.Where(m => CollectionOf(m) != null).GroupBy(m => CollectionOf(m)!, StringComparer.OrdinalIgnoreCase)
                             .OrderBy(g => LastSegment(g.Key), StringComparer.CurrentCultureIgnoreCase))
                {
                    TreeNode collectionNode = sourceNode.AddNode($"[blue]{Markup.Escape(LastSegment(collection.Key))}[/] [grey]({Count(collection.Count(), "movie")})[/]");
                    foreach (Movie movie in collection) collectionNode.AddNode(MovieLabel(movie, result));
                }
                foreach (Movie movie in source.Where(m => CollectionOf(m) == null)) sourceNode.AddNode(MovieLabel(movie, result));
            }
            return tree;
        }

        private string MovieLabel(Movie movie, MediaSearchResult result)
        {
            string? type = movie.MovieTypeId != 0 ? _movieTypes.GetValueOrDefault(movie.MovieTypeId) : null;
            return Label(movie.Name, !result.IsBrowse, movie.IsFavourite, movie.IsDeleted, type, FilePath(movie.Folder, movie.OriginalFilename));
        }

        /// <summary>The collection folder a movie is in (several films in one folder), or null.</summary>
        private string? CollectionOf(Movie movie) =>
            _movieCategories.Select(c => MediaAnalysisService.CollectionFolderOf(c.Root, movie.Folder, movie.Name)).FirstOrDefault(f => f != null);

        /// <summary>The last folder of a path stored with either separator (the database holds Windows paths, also when read on Linux).</summary>
        private static string LastSegment(string folder)
        {
            string trimmed = folder.TrimEnd('\\', '/');
            return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        }

        /// <summary>The source folder a movie is in (the first folder under its category's root), and the category.</summary>
        private (string Name, string Category) SourceOf(Movie movie)
        {
            foreach ((MediaCategory category, string root) in _movieCategories)
            {
                string folder = (movie.Folder ?? "").Replace('/', '\\');
                string rootPath = root.Replace('/', '\\');
                if (folder.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) return ("(loose files)", category.Name);
                if (folder.StartsWith(rootPath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = folder[(rootPath.Length + 1)..];
                    return (relative.Split('\\')[0], category.Name);
                }
            }
            return ("(other)", "");
        }

        private async Task<Tree> TVTreeAsync(MediaSearchResult result, CancellationToken cancellationToken)
        {
            var tree = new Tree(Group("TV shows", Count(result.Shows.Count, "show")));
            HashSet<int> matchedEpisodes = result.MatchedEpisodes.Select(e => e.Id).ToHashSet();
            foreach (TVShow show in result.Shows)
            {
                bool matched = result.MatchedShowIds.Contains(show.Id);
                string title = show.ReleaseYear > 0 ? $"{show.Name} ({show.ReleaseYear})" : show.Name;
                TreeNode showNode = tree.AddNode(Label(title, matched, show.IsFavourite, show.IsDeleted, null, show.Folder));

                // The show's seasons when it matched (episodes too with --expand), otherwise the matching episodes by season.
                bool allEpisodes = matched || options.Expand;
                List<TVShowEpisode> episodes = allEpisodes
                    ? await _search.GetShowEpisodesAsync(show.Id, options.Deleted, cancellationToken)
                    : result.MatchedEpisodes.Where(e => e.TVShowId == show.Id).ToList();
                foreach (IGrouping<int, TVShowEpisode> season in episodes.GroupBy(e => e.SeasonNo).OrderBy(g => g.Key))
                {
                    string info = allEpisodes ? Count(season.Count(), "episode") + MissingEpisodes(season) : "";
                    TreeNode seasonNode = showNode.AddNode($"[blue]{(season.Key == 0 ? "Specials" : $"Season {season.Key}")}[/]"
                        + (info.Length > 0 ? $" [grey]({Markup.Escape(info)})[/]" : ""));
                    if (allEpisodes && !options.Expand) continue;
                    foreach (TVShowEpisode episode in season.OrderBy(e => e.EpisodeNo).ThenBy(e => e.Name))
                    {
                        string name = episode.EpisodeNo > 0 ? $"s{episode.SeasonNo:00}e{episode.EpisodeNo:00} {episode.Name}" : episode.Name;
                        seasonNode.AddNode(Label(name, matchedEpisodes.Contains(episode.Id), episode.IsFavourite, episode.IsDeleted, null,
                            FilePath(episode.Folder, episode.OriginalFilename)));
                    }
                }
            }
            return tree;
        }

        /// <summary>", missing 3, 5" when a season's episode numbers have gaps.</summary>
        private static string MissingEpisodes(IEnumerable<TVShowEpisode> season)
        {
            HashSet<int> numbers = season.Select(e => e.EpisodeNo).Where(n => n > 0).ToHashSet();
            if (numbers.Count == 0) return "";
            List<int> missing = Enumerable.Range(1, numbers.Max()).Where(n => !numbers.Contains(n)).ToList();
            return missing.Count == 0 ? "" : ", missing " + string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : "");
        }

        private static string Group(string name, string count) => $"[bold]{name}[/] [grey]({count})[/]";

        /// <summary>A row's label: bold yellow when it matched, struck through when deleted, ★ for a favourite, then its info and (--paths) its path.</summary>
        private string Label(string name, bool matched, bool favourite, bool deleted, string? info, string? path)
        {
            string style = deleted ? "strikethrough grey" : matched ? "bold yellow" : "default";
            string label = $"[{style}]{Markup.Escape(name)}[/]";
            if (favourite) label += " [gold1]★[/]";
            if (!string.IsNullOrEmpty(info)) label += $" [grey]({Markup.Escape(info)})[/]";
            if (options.Paths && _paths.Map(path) is { Length: > 0 } mapped) label += $"\n[grey50]{Markup.Escape(mapped)}[/]";
            return label;
        }

        private static string? FilePath(string? folder, string? file) =>
            folder == null ? null : file == null ? folder : folder.TrimEnd('\\', '/') + (folder.Contains('/') && !folder.Contains('\\') ? "/" : "\\") + file;
    }

    private static string Count(int count, string noun) => $"{count.ToString("N0", CultureInfo.CurrentCulture)} {noun}{(count == 1 ? "" : "s")}";
}
