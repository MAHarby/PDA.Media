using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

public partial class MediaScanner
{
    /// <summary>An album folder as read from disk: its audio files (one per track) and cover art.</summary>
    private sealed record AlbumOnDisk(string Folder, string? CoverArt, List<(string FileName, int TrackNo, string Title)> Tracks);

    /// <summary>
    /// Music: <c>Root/Artist/Album/NN - Title.ext</c>. Each artist folder is an artist, each album folder holding audio files is an album
    /// (of the category's album type), and each audio file is a track. Album folders without audio files are ignored.
    /// </summary>
    private CategoryPlan ReadMusic(MediaCategory category, string root, ScanRun run)
    {
        var artists = new List<(string Folder, List<AlbumOnDisk> Albums)>();
        foreach (string artistFolder in run.SubFolders(root))
        {
            var albums = new List<AlbumOnDisk>();
            foreach (string albumFolder in run.SubFolders(artistFolder))
            {
                List<FileInfo> files = run.FilesIn(albumFolder);
                // One track per number and title (the first file wins if an album holds the same track twice, e.g. as .flac and .mp3).
                var tracks = files.Where(f => MediaFileNames.IsAudioFile(f.Name))
                    .Select(f => (FileName: f.Name, Parsed: MediaFileNames.ParseTrackFileName(f.Name)))
                    .DistinctBy(t => (t.Parsed.TrackNo, t.Parsed.Title.ToUpperInvariant()))
                    .Select(t => (t.FileName, t.Parsed.TrackNo, t.Parsed.Title))
                    .ToList();
                if (tracks.Count > 0) albums.Add(new AlbumOnDisk(albumFolder, MediaFileNames.FindCoverArt(files.Select(f => f.Name)), tracks));
            }
            artists.Add((artistFolder, albums));
        }

        int itemCount = artists.Count + artists.Sum(a => a.Albums.Count + a.Albums.Sum(album => album.Tracks.Count));
        return new CategoryPlan(category, itemCount, run => SaveMusicAsync(category, root, artists, run));
    }

    private async Task SaveMusicAsync(MediaCategory category, string root, List<(string Folder, List<AlbumOnDisk> Albums)> artists, ScanRun run)
    {
        // The category's album type, if it exists (albums fall back to type 1).
        int albumTypeId = await _albumTypeService.GetRecordByIdAsync(category.AlbumTypeId, run.Token).ConfigureAwait(false) != null ? category.AlbumTypeId : 1;

        // An artist can be in more than one music category, but has one row: its folder only moves to this category when the stored folder isn't
        // another music category's copy of the artist that is still there (so it doesn't flip on every scan).
        List<string> otherRoots = run.MusicRoots.Where(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).ToList();
        var artistSync = new EntitySync<Artist>(_artistService, "artist", a => a.Folder,
            (stored, found) => !(otherRoots.Any(r => IsUnder(stored.Folder, r)) && Directory.Exists(stored.Folder))
                && Set(stored.Folder, found.Folder, v => stored.Folder = v));
        // The album type isn't copied onto an existing album: it may have been changed by hand (e.g. to Live).
        var albumSync = new EntitySync<Album>(_albumService, "album", a => a.Folder,
            (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                | Set(stored.TrackCount, found.TrackCount, v => stored.TrackCount = v)
                | Set(stored.CoverArtFilename, found.CoverArtFilename, v => stored.CoverArtFilename = v));
        var trackSync = new EntitySync<Track>(_trackService, "track", t => t.Folder,
            (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                | Set(stored.OriginalFilename, found.OriginalFilename, v => stored.OriginalFilename = v)
                | Set(stored.ArtistId, found.ArtistId, v => stored.ArtistId = v));

        foreach ((string artistFolder, List<AlbumOnDisk> albums) in artists)
        {
            Artist artist = await artistSync.SyncAsync(new Artist { Name = Path.GetFileName(artistFolder), Folder = artistFolder }, run).ConfigureAwait(false);

            foreach (AlbumOnDisk albumOnDisk in albums)
            {
                var foundAlbum = new Album
                {
                    ArtistId = artist.Id,
                    Name = Path.GetFileName(albumOnDisk.Folder),
                    Folder = albumOnDisk.Folder,
                    AlbumTypeId = albumTypeId,
                    TrackCount = albumOnDisk.Tracks.Count,
                    CoverArtFilename = albumOnDisk.CoverArt,
                };
                Album album = await albumSync.SyncAsync(foundAlbum, run).ConfigureAwait(false);

                foreach ((string fileName, int trackNo, string title) in albumOnDisk.Tracks)
                {
                    var foundTrack = new Track
                    {
                        AlbumId = album.Id,
                        ArtistId = artist.Id,
                        TrackNo = trackNo,
                        Name = title,
                        Folder = albumOnDisk.Folder,
                        OriginalFilename = fileName,
                    };
                    await trackSync.SyncAsync(foundTrack, run).ConfigureAwait(false);
                }
            }
        }

        // Everything was read: whatever is stored under this root and wasn't seen has gone from disk (deleted tracks first, then albums, then artists).
        trackSync.QueueMissing(await _trackService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false), run);
        albumSync.QueueMissing(await _albumService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false), run);

        // An artist that has gone from this category is only deleted when it has no albums left anywhere (it may be in another music category).
        List<Artist> artistsUnderRoot = await _artistService.GetAllRecordsUnderFolderAsync(root, run.Token).ConfigureAwait(false);
        var keep = new HashSet<int>();
        foreach (Artist gone in artistSync.Unseen(artistsUnderRoot))
        {
            List<Album> albums = await _albumService.GetAllRecordsByArtistIdAsync(gone.Id, run.Token).ConfigureAwait(false);
            if (albums.Any(a => !albumSync.QueuedIds.Contains(a.Id))) keep.Add(gone.Id);
        }
        artistSync.QueueMissing(artistsUnderRoot, run, keep);
    }
}
