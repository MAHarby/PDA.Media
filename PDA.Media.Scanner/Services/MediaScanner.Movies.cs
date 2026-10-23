using Microsoft.Extensions.Logging;
using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>
    /// Movies: <c>Root/Source/…</c>, where each source folder (e.g. "Compressed Blu-Rays", "Raw Blu-Rays") holds movie files and folders.
    /// A video file directly in a source folder (or in the root) is a movie named after the file.
    /// Below a source folder, a folder holding a video file is a movie named after the folder: its largest video file is the movie (smaller ones are samples or
    /// extras) and nothing under it is searched; a folder without a video file is a group of movies, searched the same way. A folder holding several
    /// movies' files (a collection, see <see cref="CollectionFiles"/>) gives a movie per file, each named after its file.
    /// </summary>
    private CategoryPlan ReadMovies(MediaCategory category, string root, ScanRun run)
    {
        var movies = new List<Movie>();
        AddLooseMovies(root, movies, run);
        foreach (string sourceFolder in run.SubFolders(root))
        {
            AddLooseMovies(sourceFolder, movies, run);
            foreach (string folder in run.SubFolders(sourceFolder)) FindMovies(folder, movies, run);
        }

        // There shouldn't be two movies with the same name; if there are, the first one found is kept and the other is reported.
        var unique = new List<Movie>();
        foreach (IGrouping<string, Movie> sameName in movies.GroupBy(m => m.Name.ToUpperInvariant()))
        {
            Movie first = sameName.First();
            unique.Add(first);
            foreach (Movie duplicate in sameName.Skip(1))
            {
                Note(run, LogLevel.Warning, null, "Movie {Name} is in {Folder} and {OtherFolder}; only the first is kept", first.Name, first.Folder, duplicate.Folder);
            }
        }

        return new CategoryPlan(category, unique.Count, run => SaveMoviesAsync(root, unique, run));
    }

    private async Task SaveMoviesAsync(string root, List<Movie> movies, ScanRun run)
    {
        var movieSync = new EntitySync<Movie>(_movieService, "movie", m => m.Folder,
            (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                | Set(stored.OriginalFilename, found.OriginalFilename, v => stored.OriginalFilename = v)
                | Set(stored.CoverArtFilename, found.CoverArtFilename, v => stored.CoverArtFilename = v)
                | SetFileDetails(stored, found));
        foreach (Movie found in movies)
        {
            await movieSync.SyncAsync(found, run).ConfigureAwait(false);
        }

        movieSync.QueueMissing(await _movieService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false), run);
    }

    /// <summary>Adds each video file directly in <paramref name="folder"/> as a movie named after the file.</summary>
    private static void AddLooseMovies(string folder, List<Movie> movies, ScanRun run)
    {
        foreach (FileInfo file in run.FilesIn(folder).Where(f => MediaFileNames.IsVideoFile(f.Name)))
        {
            movies.Add(new Movie
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                Folder = folder,
                OriginalFilename = file.Name,
                FileSize = file.Length,
                FileDate = file.LastWriteTime,
            });
        }
    }

    /// <summary>
    /// Adds <paramref name="folder"/> as a movie if it holds a video file (the largest one), otherwise searches the folders in it.
    /// </summary>
    private static void FindMovies(string folder, List<Movie> movies, ScanRun run)
    {
        List<FileInfo> files = run.FilesIn(folder);
        List<FileInfo> videos = files.Where(f => MediaFileNames.IsVideoFile(f.Name)).ToList();
        List<FileInfo> collection = CollectionFiles(videos);
        if (collection.Count > 1)
        {
            foreach (FileInfo file in collection)
            {
                movies.Add(new Movie
                {
                    Name = Path.GetFileNameWithoutExtension(file.Name),
                    Folder = folder,
                    OriginalFilename = file.Name,
                    FileSize = file.Length,
                    FileDate = file.LastWriteTime,
                });
            }
            return;
        }

        FileInfo? main = videos.MaxBy(f => f.Length);
        if (main != null)
        {
            movies.Add(new Movie
            {
                Name = Path.GetFileName(folder),
                Folder = folder,
                OriginalFilename = main.Name,
                FileSize = main.Length,
                FileDate = main.LastWriteTime,
                CoverArtFilename = MediaFileNames.FindCoverArt(files.Select(f => f.Name)),
            });
            return;
        }

        foreach (string subFolder in run.SubFolders(folder)) FindMovies(subFolder, movies, run);
    }

    /// <summary>
    /// The movies in a folder of video files, when it holds more than one (a collection, e.g. "Star Trek" with every film in it): any file at least half
    /// the size of the largest, and, when the files name two or more different years ("Star Trek - Generations (1994).m4v"), the largest file of each year.
    /// Samples, trailers and extras are much smaller and share their film's year (or have none), so a folder with one film and its extras gives one file.
    /// </summary>
    internal static List<FileInfo> CollectionFiles(IReadOnlyCollection<FileInfo> videos)
    {
        if (videos.Count < 2) return [.. videos];
        long largest = videos.Max(f => f.Length);
        List<IGrouping<int, FileInfo>> years = videos.GroupBy(f => MediaFileNames.ParseNameAndYear(Path.GetFileNameWithoutExtension(f.Name)).Year)
            .Where(g => g.Key > 0).ToList();
        IEnumerable<FileInfo> byYear = years.Count > 1 ? years.Select(g => g.MaxBy(f => f.Length)!) : [];
        return videos.Where(f => f.Length * 2 >= largest).Union(byYear).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
