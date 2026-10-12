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
    private async Task ScanMusicAsync(MediaCategory category, string root, MediaScanResult counts, CancellationToken cancellationToken)
    {
        // Read every folder first: if one can't be read, the scan stops here, before anything is written.
        var artists = new List<(string Folder, List<AlbumOnDisk> Albums)>();
        foreach (string artistFolder in SubFolders(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var albums = new List<AlbumOnDisk>();
            foreach (string albumFolder in SubFolders(artistFolder))
            {
                List<FileInfo> files = FilesIn(albumFolder);
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

        // The category's album type, if it exists (albums fall back to type 1).
        int albumTypeId = await _albumTypeService.GetRecordByIdAsync(category.AlbumTypeId, cancellationToken).ConfigureAwait(false) != null ? category.AlbumTypeId : 1;

        HashSet<int> seenArtists = [], seenAlbums = [], seenTracks = [];
        foreach ((string artistFolder, List<AlbumOnDisk> albums) in artists)
        {
            Artist artist = await SyncAsync(_artistService, new Artist { Name = Path.GetFileName(artistFolder), Folder = artistFolder },
                (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v),
                seenArtists, counts, cancellationToken).ConfigureAwait(false);

            foreach (AlbumOnDisk albumOnDisk in albums)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var foundAlbum = new Album
                {
                    ArtistId = artist.Id, Name = Path.GetFileName(albumOnDisk.Folder), Folder = albumOnDisk.Folder, AlbumTypeId = albumTypeId,
                    TrackCount = albumOnDisk.Tracks.Count, CoverArtFilename = albumOnDisk.CoverArt,
                };
                // The album type isn't copied onto an existing album: it may have been changed by hand (e.g. to Live).
                Album album = await SyncAsync(_albumService, foundAlbum,
                    (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                        | Set(stored.TrackCount, found.TrackCount, v => stored.TrackCount = v)
                        | Set(stored.CoverArtFilename, found.CoverArtFilename, v => stored.CoverArtFilename = v),
                    seenAlbums, counts, cancellationToken).ConfigureAwait(false);

                foreach ((string fileName, int trackNo, string title) in albumOnDisk.Tracks)
                {
                    var foundTrack = new Track
                    {
                        AlbumId = album.Id, ArtistId = artist.Id, TrackNo = trackNo, Name = title, Folder = albumOnDisk.Folder, OriginalFilename = fileName,
                    };
                    await SyncAsync(_trackService, foundTrack,
                        (stored, found) => Set(stored.Folder, found.Folder, v => stored.Folder = v)
                            | Set(stored.OriginalFilename, found.OriginalFilename, v => stored.OriginalFilename = v)
                            | Set(stored.ArtistId, found.ArtistId, v => stored.ArtistId = v),
                        seenTracks, counts, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // Everything was read: whatever is stored under this root and wasn't seen has gone from disk.
        await DeleteMissingAsync(_trackService, await _trackService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seenTracks, counts, cancellationToken).ConfigureAwait(false);
        await DeleteMissingAsync(_albumService, await _albumService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seenAlbums, counts, cancellationToken).ConfigureAwait(false);
        await DeleteMissingAsync(_artistService, await _artistService.GetAllRecordsUnderFolderAsync(root, cancellationToken).ConfigureAwait(false), seenArtists, counts, cancellationToken).ConfigureAwait(false);
    }
}
