using Microsoft.Extensions.Logging;
using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>
    /// Movies: <c>Root/Source/…</c>, where each source folder (e.g. "Compressed Blu-Rays", "Raw Blu-Rays") holds movie files and folders.
    /// A video file directly in a source folder (or in the root) is a movie named after the file.
    /// Below a source folder, a folder holding a video file is a movie named after the folder: its largest video file is the movie (smaller ones are samples or
    /// extras) and nothing under it is searched; a folder without a video file is a group of movies, searched the same way.
    /// </summary>
    private async Task ScanMoviesAsync(string root, MediaScanResult counts, CancellationToken cancellationToken)
    {
        // Read every folder first: if one can't be read, the scan stops here, before anything is written.
        var movies = new List<Movie>();
        AddLooseMovies(root, movies);
        foreach (string sourceFolder in SubFolders(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddLooseMovies(sourceFolder, movies);
            foreach (string folder in SubFolders(sourceFolder)) FindMovies(folder, movies, cancellationToken);
        }

        // There shouldn't be two movies with the same name; if there are, the first one found is kept and the other is reported.
        HashSet<int> seen = [];
        foreach (IGrouping<string, Movie> sameName in movies.GroupBy(m => m.Name.ToUpperInvariant()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Movie found = sameName.First();
            foreach (Movie duplicate in sameName.Skip(1))
            {
                _logger.LogWarning("Movie {Name} is in {Folder} and {OtherFolder}; only the first is kept", found.Name, found.Folder, duplicate.Folder);
            }

            await SyncAsync(_movieService, found,
                (stored, f) => Set(stored.Folder, f.Folder, v => stored.Folder = v)
                    | Set(stored.OriginalFilename, f.OriginalFilename, v => stored.OriginalFilename = v)
                    | Set(stored.CoverArtFilename, f.CoverArtFilename, v => stored.CoverArtFilename = v),
                seen, counts, cancellationToken).ConfigureAwait(false);
        }

        await DeleteMissingAsync(_movieService, await _movieService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seen, counts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds each video file directly in <paramref name="folder"/> as a movie named after the file.</summary>
    private static void AddLooseMovies(string folder, List<Movie> movies)
    {
        foreach (FileInfo file in FilesIn(folder).Where(f => MediaFileNames.IsVideoFile(f.Name)))
        {
            movies.Add(new Movie { Name = Path.GetFileNameWithoutExtension(file.Name), Folder = folder, OriginalFilename = file.Name });
        }
    }

    /// <summary>
    /// Adds <paramref name="folder"/> as a movie if it holds a video file (the largest one), otherwise searches the folders in it.
    /// </summary>
    private static void FindMovies(string folder, List<Movie> movies, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<FileInfo> files = FilesIn(folder);
        FileInfo? main = files.Where(f => MediaFileNames.IsVideoFile(f.Name)).MaxBy(f => f.Length);
        if (main != null)
        {
            movies.Add(new Movie
            {
                Name = Path.GetFileName(folder),
                Folder = folder,
                OriginalFilename = main.Name,
                CoverArtFilename = MediaFileNames.FindCoverArt(files.Select(f => f.Name)),
            });
            return;
        }

        foreach (string subFolder in SubFolders(folder)) FindMovies(subFolder, movies, cancellationToken);
    }
}
