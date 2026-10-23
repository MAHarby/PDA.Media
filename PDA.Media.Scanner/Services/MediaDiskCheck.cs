using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>What a disk check found that doesn't match the database.</summary>
public enum MediaDiskIssueKind
{
    /// <summary>A media file on disk with no row (a new file, or a second copy of a track or episode the scan skips).</summary>
    NotInDatabase,

    /// <summary>A row whose file isn't there any more.</summary>
    MissingFromDisk,

    /// <summary>A file whose size or date isn't what the last scan stored.</summary>
    Changed,

    /// <summary>A smaller video file beside a movie (a sample or extra the scan ignores).</summary>
    Extra,

    /// <summary>A folder with no files anywhere under it.</summary>
    EmptyFolder,

    /// <summary>A folder that couldn't be read.</summary>
    Unreadable,
}

public sealed record MediaDiskIssue(MediaDiskIssueKind Kind, string Path, string Detail, long? Size = null);

/// <summary>
/// A folder as it is on disk now, against what the database has under it: every file (media and other: artwork, subtitles, extras), and the differences.
/// </summary>
public sealed class MediaDiskCheckResult
{
    /// <summary>The most issues kept (<see cref="IssueCount"/> is still the full number).</summary>
    public const int MaxIssues = 1000;

    public required string Folder { get; init; }
    public bool FolderFound { get; set; }
    public int Folders { get; set; }
    public int Files { get; set; }
    public long Size { get; set; }
    public int MediaFiles { get; set; }
    public long MediaSize { get; set; }
    public int OtherFiles => Files - MediaFiles;
    public long OtherSize => Size - MediaSize;

    /// <summary>Rows stored under the folder (live ones), and the total size the last scan stored for them.</summary>
    public int StoredFiles { get; set; }
    public long StoredSize { get; set; }

    public List<MediaDiskIssue> Issues { get; } = [];
    public int IssueCount { get; set; }
    public Dictionary<MediaDiskIssueKind, int> IssueCounts { get; } = [];
    public TimeSpan Elapsed { get; set; }

    internal void Add(MediaDiskIssue issue)
    {
        IssueCount++;
        IssueCounts[issue.Kind] = IssueCounts.GetValueOrDefault(issue.Kind) + 1;
        if (Issues.Count < MaxIssues) Issues.Add(issue);
    }
}

/// <summary>
/// The Analysis tab's live check: reads a folder (and everything under it) from disk now and compares it with the media rows stored under it, without
/// changing anything. Hidden, system and NAS housekeeping folders are skipped, as the scan does.
/// </summary>
public class MediaDiskCheckService(MediaAnalysisService analysisService)
{
    /// <summary>Checks <paramref name="folder"/>, holding <paramref name="kind"/> media; <paramref name="progress"/> gets the number of folders read so far.</summary>
    public async Task<MediaDiskCheckResult> CheckAsync(MediaAnalysisKind kind, string folder, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var result = new MediaDiskCheckResult { Folder = folder };
        List<MediaAnalysisItem> stored = await analysisService.GetFilesAsync(new MediaAnalysisScope(kind, folder), cancellationToken).ConfigureAwait(false);
        result.StoredFiles = stored.Count;
        result.StoredSize = stored.Sum(s => s.Size ?? 0);
        Dictionary<string, MediaAnalysisItem> byPath = stored.Where(s => s.Path != null).GroupBy(s => s.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // A movie's own folder (the scan names such a movie after its folder): another video file in it is an extra, not a missing movie.
        // A loose movie is named after its file, so a new file beside it in a source folder is still reported as not stored.
        HashSet<string> movieFolders = kind == MediaAnalysisKind.Movies
            ? stored.Where(s => s.Folder != null && string.Equals(s.Name, Path.GetFileName(s.Folder.TrimEnd('\\', '/')), StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Folder!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptyFolders = new List<string>();

        result.FolderFound = Directory.Exists(folder);
        if (result.FolderFound)
        {
            await Task.Run(() => Read(folder, isTop: true), cancellationToken).ConfigureAwait(false);
            foreach (string empty in emptyFolders) result.Add(new MediaDiskIssue(MediaDiskIssueKind.EmptyFolder, empty, "No files in it or under it."));
        }
        else
        {
            result.Add(new MediaDiskIssue(MediaDiskIssueKind.Unreadable, folder, "The folder can't be found or reached."));
        }

        foreach (MediaAnalysisItem row in stored.Where(s => s.Path != null && !seen.Contains(s.Path)).OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (result.FolderFound) result.Add(new MediaDiskIssue(MediaDiskIssueKind.MissingFromDisk, row.Path!, "Stored, but the file isn't there.", row.Size));
        }
        result.Elapsed = DateTime.UtcNow - started;
        return result;

        // Reads a folder and the folders under it; returns the number of files found under it.
        int Read(string path, bool isTop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileInfo> files;
            List<string> subFolders;
            try
            {
                files = MediaScanner.FilesIn(path);
                subFolders = MediaScanner.SubFolders(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Add(new MediaDiskIssue(MediaDiskIssueKind.Unreadable, path, ex.Message));
                return 1;
            }
            result.Folders++;
            progress?.Report(result.Folders);

            foreach (FileInfo file in files)
            {
                result.Files++;
                result.Size += file.Length;
                bool isMedia = kind == MediaAnalysisKind.Music ? MediaFileNames.IsAudioFile(file.Name) : MediaFileNames.IsVideoFile(file.Name);
                if (!isMedia) continue;
                result.MediaFiles++;
                result.MediaSize += file.Length;
                if (byPath.TryGetValue(file.FullName, out MediaAnalysisItem? row))
                {
                    seen.Add(row.Path!);
                    if (row.Size != file.Length || row.Date != file.LastWriteTime)
                    {
                        string detail = row.Size == null ? $"No size stored; {FileSize(file.Length)} on disk."
                            : row.Size != file.Length ? $"{FileSize(row.Size.Value)} stored, {FileSize(file.Length)} on disk."
                            : $"Modified {file.LastWriteTime:g} (stored {row.Date:g}).";
                        result.Add(new MediaDiskIssue(MediaDiskIssueKind.Changed, file.FullName, detail, file.Length));
                    }
                }
                else if (movieFolders.Contains(path))
                {
                    result.Add(new MediaDiskIssue(MediaDiskIssueKind.Extra, file.FullName, "A smaller video beside the movie (the scan uses the largest).", file.Length));
                }
                else
                {
                    result.Add(new MediaDiskIssue(MediaDiskIssueKind.NotInDatabase, file.FullName, "On disk, but not stored: scan the category to add it.", file.Length));
                }
            }

            int count = files.Count;
            foreach (string subFolder in subFolders) count += Read(subFolder, isTop: false);
            if (count == 0 && !isTop)
            {
                // Only the outermost empty folder is reported.
                emptyFolders.RemoveAll(f => f.StartsWith(path, StringComparison.OrdinalIgnoreCase) && f.Length > path.Length && f[path.Length] is '\\' or '/');
                emptyFolders.Add(path);
            }
            return count;
        }
    }

    /// <summary>"12.5 GB", "850 MB", "12 KB".</summary>
    private static string FileSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} bytes" : $"{size:0.#} {units[unit]}";
    }
}
