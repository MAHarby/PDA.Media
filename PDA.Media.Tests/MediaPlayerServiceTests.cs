using System.Diagnostics;
using System.IO;
using PDA.Media.Utils.Services;

namespace PDA.Media.Tests;

/// <summary>MediaPlayerService with pretend players (no process is started) and a temp playlist folder.</summary>
[TestClass]
public sealed class MediaPlayerServiceTests
{
    private const string Vlc = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
    private const string Wmp = @"C:\Program Files\Windows Media Player\wmplayer.exe";

    [TestMethod]
    public void TestVideoPlaysInVlcAndASingleFileNeedsNoPlaylist()
    {
        using var temp = new Temp();
        MediaPlayerService player = temp.Player(installed: [Vlc, Wmp]);

        MediaPlayResult result = player.Play(new MediaPlayRequest([@"\\nas\Movies\Heat (1995)\Heat (1995).mkv"], MediaPlayKind.Video, "Heat (1995)"));

        Assert.AreEqual("Playing Heat (1995) in VLC", result.Message);
        ProcessStartInfo started = temp.Started.Single();
        Assert.AreEqual(Vlc, started.FileName);
        CollectionAssert.AreEqual(new[] { @"\\nas\Movies\Heat (1995)\Heat (1995).mkv" }, started.ArgumentList.ToArray());
        Assert.IsFalse(started.UseShellExecute);
        Assert.IsFalse(Directory.Exists(temp.Folder) && Directory.EnumerateFiles(temp.Folder).Any(), "no playlist for one file");
    }

    [TestMethod]
    public void TestMusicPlaysInWindowsMediaPlayerWithAWplPlaylist()
    {
        using var temp = new Temp();
        MediaPlayerService player = temp.Player(installed: [Vlc, Wmp]);
        string[] tracks = [@"\\nas\Music\Björk\Homogenic\01 - Hunter.flac", @"\\nas\Music\Björk\Homogenic\05 - Jóga.flac"];

        MediaPlayResult result = player.Play(new MediaPlayRequest(tracks, MediaPlayKind.Music, "Björk - Homogenic"));

        Assert.AreEqual("Playing 2 files in Windows Media Player", result.Message);
        ProcessStartInfo started = temp.Started.Single();
        Assert.AreEqual(Wmp, started.FileName);
        string playlist = started.ArgumentList.Single();
        StringAssert.EndsWith(playlist, ".wpl");
        string text = File.ReadAllText(playlist);
        StringAssert.Contains(text, "<title>Björk - Homogenic</title>");
        Assert.IsLessThan(text.IndexOf("05 - Jóga"), text.IndexOf("01 - Hunter"), "in order");
    }

    [TestMethod]
    public void TestMusicFallsBackToVlcThenTheDefaultApp()
    {
        using var temp = new Temp();
        string[] tracks = [@"\\nas\Music\A\B\01 - One.flac", @"\\nas\Music\A\B\02 - Two.flac"];

        Assert.AreEqual("Playing 2 files in VLC", temp.Player(installed: [Vlc]).Play(new MediaPlayRequest(tracks, MediaPlayKind.Music, "B")).Message);
        StringAssert.EndsWith(temp.Started.Last().ArgumentList.Single(), ".m3u8");

        MediaPlayResult fallback = temp.Player(installed: []).Play(new MediaPlayRequest(tracks, MediaPlayKind.Music, "B"));
        StringAssert.Contains(fallback.Message, "default player (neither Windows Media Player nor VLC was found)");
        ProcessStartInfo started = temp.Started.Last();
        Assert.IsTrue(started.UseShellExecute);
        StringAssert.EndsWith(started.FileName, ".m3u8");
        string m3u = File.ReadAllText(started.FileName);
        Assert.AreEqual("#EXTM3U\n#PLAYLIST:B\n#EXTINF:-1,01 - One\n" + tracks[0] + "\n#EXTINF:-1,02 - Two\n" + tracks[1] + "\n", m3u);
    }

    [TestMethod]
    public void TestEnqueueNeedsVlc()
    {
        using var temp = new Temp();
        string[] episodes = [@"\\nas\TV\Firefly (2002)\Season 01\Firefly (2002) - s01e01 - Serenity.mkv"];

        MediaPlayResult enqueued = temp.Player(installed: [Vlc, Wmp]).Play(new MediaPlayRequest(episodes, MediaPlayKind.Music, "Serenity", Enqueue: true));
        Assert.AreEqual("Added Firefly (2002) - s01e01 - Serenity to VLC's playlist", enqueued.Message);
        CollectionAssert.AreEqual(new[] { "--one-instance", "--playlist-enqueue", episodes[0] }, temp.Started.Single().ArgumentList.ToArray(),
            "music goes to VLC too when it is enqueued");

        MediaPlayResult noVlc = temp.Player(installed: [Wmp]).Play(new MediaPlayRequest(episodes, MediaPlayKind.Video, "Serenity", Enqueue: true));
        Assert.IsFalse(noVlc.Succeeded);
        StringAssert.Contains(noVlc.Message, "needs VLC");
    }

    [TestMethod]
    public void TestShuffleAndSettingsAndNothingToPlay()
    {
        using var temp = new Temp();
        string[] files = Enumerable.Range(1, 20).Select(i => $@"\\nas\Music\A\B\{i:00} - Track.flac").ToArray();
        MediaPlayerService player = temp.Player(installed: [Vlc], random: new Random(7));

        MediaPlayResult result = player.Play(new MediaPlayRequest(files, MediaPlayKind.Video, "B", Shuffle: true));

        Assert.AreEqual("Playing 20 files (shuffled) in VLC", result.Message);
        string[] played = File.ReadAllLines(temp.Started.Single().ArgumentList.Single()).Where(l => !l.StartsWith('#')).ToArray();
        CollectionAssert.AreEquivalent(files, played);
        CollectionAssert.AreNotEqual(files, played);

        // A player path in the settings wins.
        temp.Settings.SaveSettings(new UserSettings { VideoPlayerPath = @"D:\Tools\vlc\vlc.exe" });
        Assert.AreEqual(@"D:\Tools\vlc\vlc.exe", temp.Player(installed: [Vlc, @"D:\Tools\vlc\vlc.exe"]).FindVlc());
        Assert.AreEqual(Vlc, temp.Player(installed: [Vlc]).FindVlc(), "a setting that isn't there is ignored");

        Assert.IsFalse(player.Play(new MediaPlayRequest([], MediaPlayKind.Music, "Empty")).Succeeded);
    }

    private sealed class Temp : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pda_player_test_" + Guid.NewGuid().ToString("N"));

        public Temp()
        {
            Settings = new AppSettingsService(Path.Combine(_root, "settings.json"));
        }

        public string Folder => Path.Combine(_root, "playlists");
        public AppSettingsService Settings { get; }
        public List<ProcessStartInfo> Started { get; } = [];

        /// <summary>A player service where only <paramref name="installed"/> exist, nothing is registered, and nothing is really started.</summary>
        public MediaPlayerService Player(string[] installed, Random? random = null) => new(Settings, playlistFolder: Folder)
        {
            FileExists = path => installed.Contains(path) || File.Exists(path) && path.StartsWith(_root),
            AppPath = exe => installed.FirstOrDefault(p => p.EndsWith(exe)),
            Launch = Started.Add,
            Random = random ?? new Random(1),
        };

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
