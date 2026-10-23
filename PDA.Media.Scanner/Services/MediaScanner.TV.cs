using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>
    /// TV: <c>Root/Show (Year)/Season NN/Show (Year) - sNNeNN - Title.ext</c>. Each show folder is a show (its name and year read from the folder name, so it is
    /// kept even before any episodes arrive), and each video file anywhere under it is an episode, numbered from its "sNNeNN" marker.
    /// </summary>
    private CategoryPlan ReadTVShows(MediaCategory category, string root, ScanRun run)
    {
        var shows = new List<(TVShow Show, List<TVShowEpisode> Episodes)>();
        foreach (string showFolder in run.SubFolders(root))
        {
            (string name, int year) = MediaFileNames.ParseNameAndYear(Path.GetFileName(showFolder));
            var show = new TVShow
            {
                Name = name,
                ReleaseYear = year,
                Folder = showFolder,
                CoverArtFilename = MediaFileNames.FindCoverArt(run.FilesIn(showFolder).Select(f => f.Name)),
            };

            var episodes = new List<TVShowEpisode>();
            foreach ((string folder, FileInfo file) in VideoFilesUnder(showFolder, run))
            {
                string seasonFolder = folder == showFolder ? "" : Path.GetFileName(folder);
                (int season, int episode, string title) = MediaFileNames.ParseEpisodeFileName(file.Name, seasonFolder);
                episodes.Add(new TVShowEpisode
                {
                    SeasonNo = season,
                    EpisodeNo = episode,
                    Name = title,
                    Folder = folder,
                    OriginalFilename = file.Name,
                    FileSize = file.Length,
                    FileDate = file.LastWriteTime,
                });
            }

            // One record per episode (the first file wins if an episode is there twice); numbered episodes are matched by number, unnumbered ones by name.
            shows.Add((show, episodes.DistinctBy(e => e.EpisodeNo > 0 ? $"{e.SeasonNo}x{e.EpisodeNo}" : $"{e.SeasonNo}x0 {e.Name.ToUpperInvariant()}").ToList()));
        }

        return new CategoryPlan(category, shows.Count + shows.Sum(s => s.Episodes.Count), run => SaveTVShowsAsync(root, shows, run));
    }

    private async Task SaveTVShowsAsync(string root, List<(TVShow Show, List<TVShowEpisode> Episodes)> shows, ScanRun run)
    {
        var showSync = new EntitySync<TVShow>(_tvShowService, "TV show", s => s.Folder,
            (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                | Set(stored.CoverArtFilename, found.CoverArtFilename, v => stored.CoverArtFilename = v));
        var episodeSync = new EntitySync<TVShowEpisode>(_episodeService, "episode", e => e.Folder,
            (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                | Set(stored.OriginalFilename, found.OriginalFilename, v => stored.OriginalFilename = v)
                | (stored.EpisodeNo > 0 && Set(stored.Name, found.Name, v => stored.Name = v))
                | SetFileDetails(stored, found));

        foreach ((TVShow foundShow, List<TVShowEpisode> episodes) in shows)
        {
            TVShow show = await showSync.SyncAsync(foundShow, run).ConfigureAwait(false);
            foreach (TVShowEpisode found in episodes)
            {
                found.TVShowId = show.Id;
                await episodeSync.SyncAsync(found, run).ConfigureAwait(false);
            }
        }

        episodeSync.QueueMissing(await _episodeService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false), run);
        showSync.QueueMissing(await _tvShowService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false), run);
    }

    /// <summary>The video files in <paramref name="folder"/> and every folder under it (skipping hidden and NAS housekeeping folders), with their folders.</summary>
    private static IEnumerable<(string Folder, FileInfo File)> VideoFilesUnder(string folder, ScanRun run)
    {
        foreach (FileInfo file in run.FilesIn(folder).Where(f => MediaFileNames.IsVideoFile(f.Name))) yield return (folder, file);
        foreach (string subFolder in run.SubFolders(folder))
        {
            foreach (var item in VideoFilesUnder(subFolder, run)) yield return item;
        }
    }
}
