using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>
    /// TV: <c>Root/Show (Year)/Season NN/Show (Year) - sNNeNN - Title.ext</c>. Each show folder is a show (its name and year read from the folder name, so it is
    /// kept even before any episodes arrive), and each video file anywhere under it is an episode, numbered from its "sNNeNN" marker.
    /// </summary>
    private async Task ScanTVShowsAsync(string root, MediaScanResult counts, CancellationToken cancellationToken)
    {
        // Read every folder first: if one can't be read, the scan stops here, before anything is written.
        var shows = new List<(TVShow Show, List<TVShowEpisode> Episodes)>();
        foreach (string showFolder in SubFolders(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string name, int year) = MediaFileNames.ParseNameAndYear(Path.GetFileName(showFolder));
            var show = new TVShow
            {
                Name = name, ReleaseYear = year, Folder = showFolder, CoverArtFilename = MediaFileNames.FindCoverArt(FilesIn(showFolder).Select(f => f.Name)),
            };

            var episodes = new List<TVShowEpisode>();
            foreach ((string folder, FileInfo file) in VideoFilesUnder(showFolder))
            {
                string seasonFolder = folder == showFolder ? "" : Path.GetFileName(folder);
                (int season, int episode, string title) = MediaFileNames.ParseEpisodeFileName(file.Name, seasonFolder);
                episodes.Add(new TVShowEpisode { SeasonNo = season, EpisodeNo = episode, Name = title, Folder = folder, OriginalFilename = file.Name });
            }
            shows.Add((show, episodes));
        }

        HashSet<int> seenShows = [], seenEpisodes = [];
        foreach ((TVShow foundShow, List<TVShowEpisode> episodes) in shows)
        {
            TVShow show = await SyncAsync(_tvShowService, foundShow,
                (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                    | Set(stored.CoverArtFilename, found.CoverArtFilename, v => stored.CoverArtFilename = v),
                seenShows, counts, cancellationToken).ConfigureAwait(false);

            // One record per episode (the first file wins if an episode is there twice); numbered episodes are matched by number, unnumbered ones by name.
            foreach (TVShowEpisode found in episodes.DistinctBy(e => e.EpisodeNo > 0 ? $"{e.SeasonNo}x{e.EpisodeNo}" : $"{e.SeasonNo}x0 {e.Name.ToUpperInvariant()}"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                found.TVShowId = show.Id;
                await SyncAsync(_episodeService, found,
                    (stored, f) => Set(stored.Folder, f.Folder, v => stored.Folder = v)
                        | Set(stored.OriginalFilename, f.OriginalFilename, v => stored.OriginalFilename = v)
                        | (stored.EpisodeNo > 0 && Set(stored.Name, f.Name, v => stored.Name = v)),
                    seenEpisodes, counts, cancellationToken).ConfigureAwait(false);
            }
        }

        await DeleteMissingAsync(_episodeService, await _episodeService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seenEpisodes, counts, cancellationToken).ConfigureAwait(false);
        await DeleteMissingAsync(_tvShowService, await _tvShowService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seenShows, counts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The video files in <paramref name="folder"/> and every folder under it (skipping hidden and NAS housekeeping folders), with their folders.</summary>
    private static IEnumerable<(string Folder, FileInfo File)> VideoFilesUnder(string folder)
    {
        foreach (FileInfo file in FilesIn(folder).Where(f => MediaFileNames.IsVideoFile(f.Name))) yield return (folder, file);
        foreach (string subFolder in SubFolders(folder))
        {
            foreach (var item in VideoFilesUnder(subFolder)) yield return item;
        }
    }
}
