using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace PDA.Media.Utils.Services;

/// <summary>What is played: video (movies, TV) or music.</summary>
public enum MediaPlayKind
{
    Video,
    Music,
}

/// <summary>Files to play, in order.</summary>
/// <param name="Title">The playlist's title ("Firefly - Season 1").</param>
/// <param name="Shuffle">Play them in a random order.</param>
/// <param name="Enqueue">Add them to the playlist of the VLC already running instead of playing them now (VLC only).</param>
public sealed record MediaPlayRequest(IReadOnlyList<string> Files, MediaPlayKind Kind, string Title, bool Shuffle = false, bool Enqueue = false);

public sealed record MediaPlayResult(bool Succeeded, string Message);

/// <summary>
/// Plays media files in an external player: VLC for video, Windows Media Player (wmplayer.exe) for music (VLC when it isn't there), and the file type's
/// default app when neither is found. Several files (or a shuffle) go into a temporary playlist: <c>.m3u8</c> for VLC and the default app, <c>.wpl</c> for
/// Windows Media Player (both keep non-Western characters). Enqueue adds to the running VLC's playlist (<c>--one-instance --playlist-enqueue</c>).
/// The players come from <see cref="UserSettings.VideoPlayerPath"/> / <see cref="UserSettings.MusicPlayerPath"/>, else the Windows "App Paths"
/// registration, else the usual install folders.
/// </summary>
public class MediaPlayerService
{
    public const string VlcName = "VLC";
    public const string WindowsMediaPlayerName = "Windows Media Player";
    public static readonly string DefaultPlaylistFolder = Path.Combine(Path.GetTempPath(), "PDA.Media", "Playlists");

    private readonly AppSettingsService? _settingsService;
    private readonly ILogger<MediaPlayerService> _logger;
    private readonly string _playlistFolder;

    public MediaPlayerService(AppSettingsService? settingsService = null, ILogger<MediaPlayerService>? logger = null, string? playlistFolder = null)
    {
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<MediaPlayerService>.Instance;
        _playlistFolder = playlistFolder ?? DefaultPlaylistFolder;
    }

    /// <summary>Whether a file exists (replaced in tests).</summary>
    public Func<string, bool> FileExists { get; init; } = File.Exists;

    /// <summary>A program's path from the Windows "App Paths" registration ("vlc.exe"); null when not registered (replaced in tests).</summary>
    public Func<string, string?> AppPath { get; init; } = RegisteredAppPath;

    /// <summary>Starts a process (replaced in tests).</summary>
    public Action<ProcessStartInfo> Launch { get; init; } = info => Process.Start(info)?.Dispose();

    /// <summary>The order of a shuffle (replaced in tests).</summary>
    public Random Random { get; init; } = Random.Shared;

    // Players.
    // ==================================================================================================

    /// <summary>VLC: the video player setting, the App Paths registration, or Program Files; null when not found.</summary>
    public string? FindVlc() => FirstExisting(
        _settingsService?.LoadSettings().VideoPlayerPath,
        AppPath("vlc.exe"),
        ProgramFiles(Environment.SpecialFolder.ProgramFiles, "VideoLAN", "VLC", "vlc.exe"),
        ProgramFiles(Environment.SpecialFolder.ProgramFilesX86, "VideoLAN", "VLC", "vlc.exe"));

    /// <summary>Windows Media Player: the music player setting, the App Paths registration, or Program Files; null when not found.</summary>
    public string? FindWindowsMediaPlayer() => FirstExisting(
        _settingsService?.LoadSettings().MusicPlayerPath,
        AppPath("wmplayer.exe"),
        ProgramFiles(Environment.SpecialFolder.ProgramFiles, "Windows Media Player", "wmplayer.exe"),
        ProgramFiles(Environment.SpecialFolder.ProgramFilesX86, "Windows Media Player", "wmplayer.exe"));

    private string? FirstExisting(params string?[] paths) => paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && FileExists(p));

    private static string? ProgramFiles(Environment.SpecialFolder folder, params string[] parts)
    {
        string root = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(root) ? null : Path.Combine([root, .. parts]);
    }

    private static string? RegisteredAppPath(string exe)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using RegistryKey? key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                if (key?.GetValue("") is string path && path.Length > 0) return path.Trim('"');
            }
        }
        catch (Exception)
        {
            // No access to the registry: fall back to Program Files.
        }
        return null;
    }

    // Playing.
    // ==================================================================================================

    /// <summary>Plays (or enqueues) the files; the result says which player and how many, or why it couldn't.</summary>
    public MediaPlayResult Play(MediaPlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Files.Count == 0) return new MediaPlayResult(false, "Nothing to play");

        string? vlc = FindVlc();
        (string? player, string name) = request.Enqueue || request.Kind == MediaPlayKind.Video ? (vlc, VlcName)
            : FindWindowsMediaPlayer() is { } wmp ? (wmp, WindowsMediaPlayerName)
            : (vlc, VlcName);
        if (request.Enqueue && player == null) return new MediaPlayResult(false, "Adding to a playlist needs VLC, which wasn't found (set its path in the settings)");

        List<string> files = request.Shuffle ? Shuffled(request.Files) : [.. request.Files];
        string target = files.Count == 1 ? files[0] : WritePlaylist(files, request.Title, name == WindowsMediaPlayerName && player != null ? ".wpl" : ".m3u8");
        string what = files.Count == 1 ? FileTitle(files[0]) : $"{files.Count} files" + (request.Shuffle ? " (shuffled)" : "");

        try
        {
            if (player == null)
            {
                Launch(new ProcessStartInfo(target) { UseShellExecute = true });
                _logger.LogInformation("Playing {Title} ({Count} files) in the default player", request.Title, files.Count);
                return new MediaPlayResult(true, $"Playing {what} in the default player ("
                    + (request.Kind == MediaPlayKind.Video ? "VLC wasn't found" : "neither Windows Media Player nor VLC was found") + ")");
            }

            var info = new ProcessStartInfo(player) { UseShellExecute = false };
            if (request.Enqueue)
            {
                info.ArgumentList.Add("--one-instance");
                info.ArgumentList.Add("--playlist-enqueue");
            }
            info.ArgumentList.Add(target);
            Launch(info);
            _logger.LogInformation("{Action} {Title} ({Count} files) in {Player}", request.Enqueue ? "Enqueued" : "Playing", request.Title, files.Count, name);
            return new MediaPlayResult(true, request.Enqueue ? $"Added {what} to {name}'s playlist" : $"Playing {what} in {name}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't start {Player} for {Title}", player ?? "the default player", request.Title);
            return new MediaPlayResult(false, $"Couldn't start {(player == null ? "the default player" : name)}: {ex.Message}");
        }
    }

    private List<string> Shuffled(IReadOnlyList<string> files)
    {
        List<string> list = [.. files];
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>Writes a playlist in the temp playlist folder (removing ones over a day old) and returns its path.</summary>
    private string WritePlaylist(IReadOnlyList<string> files, string title, string extension)
    {
        Directory.CreateDirectory(_playlistFolder);
        foreach (string old in Directory.EnumerateFiles(_playlistFolder).Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1)))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Still open in a player: next time.
            }
        }

        string safeTitle = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(_playlistFolder, $"{(safeTitle.Length > 0 ? safeTitle : "Playlist")} {DateTime.Now:yyyyMMdd-HHmmss-fff}{extension}");
        File.WriteAllText(path, extension == ".wpl" ? Wpl(files, title) : M3u8(files, title), new UTF8Encoding(false));
        return path;
    }

    /// <summary>An extended M3U playlist in UTF-8.</summary>
    public static string M3u8(IEnumerable<string> files, string title)
    {
        var text = new StringBuilder("#EXTM3U\n").Append("#PLAYLIST:").Append(title).Append('\n');
        foreach (string file in files) text.Append("#EXTINF:-1,").Append(FileTitle(file)).Append('\n').Append(file).Append('\n');
        return text.ToString();
    }

    /// <summary>A file's name without its folder or extension, for either kind of separator (NAS paths are Windows paths).</summary>
    private static string FileTitle(string path) => Path.GetFileNameWithoutExtension(path[(path.LastIndexOfAny(['\\', '/']) + 1)..]);

    /// <summary>A Windows Media Player playlist (SMIL XML).</summary>
    public static string Wpl(IEnumerable<string> files, string title) =>
        "<?wpl version=\"1.0\"?>\n" + new XElement("smil",
            new XElement("head", new XElement("title", title)),
            new XElement("body", new XElement("seq", files.Select(f => new XElement("media", new XAttribute("src", f)))))).ToString();
}
