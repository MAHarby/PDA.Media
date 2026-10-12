using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>
    /// Movies: <c>Root/Movie (Year)/Movie (Year).ext</c>. Each folder holding a video file is a movie named after the folder; its largest video file is the movie
    /// (smaller ones are samples or extras). A video file directly in the root folder is a movie named after the file.
    /// </summary>
    private async Task ScanMoviesAsync(string root, MediaScanResult counts, CancellationToken cancellationToken)
    {
        // Read every folder first: if one can't be read, the scan stops here, before anything is written.
        var movies = new List<Movie>();
        foreach (string folder in SubFolders(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileInfo> files = FilesIn(folder);
            FileInfo? main = files.Where(f => MediaFileNames.IsVideoFile(f.Name)).MaxBy(f => f.Length);
            if (main == null) continue;
            movies.Add(new Movie
            {
                Name = Path.GetFileName(folder), Folder = folder, OriginalFilename = main.Name, CoverArtFilename = MediaFileNames.FindCoverArt(files.Select(f => f.Name)),
            });
        }
        foreach (FileInfo file in FilesIn(root).Where(f => MediaFileNames.IsVideoFile(f.Name)))
        {
            movies.Add(new Movie { Name = Path.GetFileNameWithoutExtension(file.Name), Folder = root, OriginalFilename = file.Name });
        }

        HashSet<int> seen = [];
        foreach (Movie found in movies.DistinctBy(m => m.Name.ToUpperInvariant()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncAsync(_movieService, found,
                (stored, f) => Set(stored.Folder, f.Folder, v => stored.Folder = v)
                    | Set(stored.OriginalFilename, f.OriginalFilename, v => stored.OriginalFilename = v)
                    | Set(stored.CoverArtFilename, f.CoverArtFilename, v => stored.CoverArtFilename = v),
                seen, counts, cancellationToken).ConfigureAwait(false);
        }

        await DeleteMissingAsync(_movieService, await _movieService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seen, counts, cancellationToken).ConfigureAwait(false);
    }
}
