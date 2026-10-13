using System.Diagnostics;
using System.IO;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>Play, Play from here, Shuffle and Add to VLC playlist in the Search tab, with a pretend player (nothing is started).</summary>
[TestClass]
public sealed class MediaPlayViewModelTests
{
    [TestMethod]
    public async Task TestPlayAnEpisodeASeasonAndFromHere()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.SearchAsync("firefly");
        MediaTreeNode firefly = vm.Results.Single().Children.Single();
        await firefly.LoadChildrenAsync();
        MediaTreeNode season = firefly.Children.Single();
        MediaTreeNode trainJob = season.Children[1];

        vm.SelectedNode = trainJob;
        Assert.IsTrue(vm.CanPlayFromHere);
        await vm.PlayCommand.ExecuteAsync(null);
        StringAssert.EndsWith(setup.Started.Last().ArgumentList.Single(), "Firefly (2002) - s01e02 - The Train Job.mkv");
        Assert.AreEqual("Playing Firefly (2002) - s01e02 - The Train Job in VLC", vm.StatusText);

        await vm.PlayFromHereCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "s01e02", "s01e03" }, setup.PlaylistEpisodes(), "this one and the rest of the season");

        vm.SelectedNode = season;
        Assert.IsFalse(vm.CanPlayFromHere);
        await vm.PlayCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "s01e01", "s01e02", "s01e03" }, setup.PlaylistEpisodes(), "the season in order");
        Assert.AreEqual("Playing 3 files in VLC", vm.StatusText);
    }

    [TestMethod]
    public async Task TestPlayAnArtistSkipsDeletedTracks()
    {
        using var setup = await Setup.CreateAsync();
        var tracks = new TrackService(setup.Db.ContextFactory);
        tracks.DeleteRecord(tracks.GetAllRecords("Waterloo").Single().Id);
        MediaSearchViewModel vm = await setup.SearchAsync("abba", showDeleted: true);
        vm.SelectedNode = vm.Results.Single().Children.Single();

        await vm.PlayCommand.ExecuteAsync(null);

        ProcessStartInfo started = setup.Started.Last();
        Assert.AreEqual(Setup.Wmp, started.FileName, "music in Windows Media Player");
        string playlist = File.ReadAllText(started.ArgumentList.Single());
        StringAssert.Contains(playlist, "01 - Dancing Queen.flac");
        StringAssert.Contains(playlist, "01 - When I Kissed the Teacher.flac");
        Assert.DoesNotContain("Waterloo", playlist, "deleted (and shown greyed out), so not played");
        Assert.AreEqual("Playing 2 files in Windows Media Player", vm.StatusText);
    }

    [TestMethod]
    public async Task TestShuffleAndEnqueue()
    {
        using var setup = await Setup.CreateAsync();
        MediaSearchViewModel vm = await setup.SearchAsync("firefly");
        vm.SelectedNode = vm.Results.Single().Children.Single();

        await vm.ShuffleCommand.ExecuteAsync(null);
        Assert.AreEqual("Playing 3 files (shuffled) in VLC", vm.StatusText);
        CollectionAssert.AreEquivalent(new[] { "s01e01", "s01e02", "s01e03" }, setup.PlaylistEpisodes());

        await vm.EnqueueCommand.ExecuteAsync(null);
        ProcessStartInfo enqueued = setup.Started.Last();
        CollectionAssert.AreEqual(new[] { "--one-instance", "--playlist-enqueue" }, enqueued.ArgumentList.Take(2).ToArray());
        StringAssert.StartsWith(vm.StatusText, "Added 3 files to VLC's playlist");
    }

    /// <summary>ABBA (two albums) and Firefly (three episodes) scanned into a test database; VLC and Windows Media Player "installed".</summary>
    private sealed class Setup : IDisposable
    {
        public const string Vlc = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
        public const string Wmp = @"C:\Program Files\Windows Media Player\wmplayer.exe";

        public SqlServerTestDatabase Db { get; } = new();
        public TempServices Temp { get; } = new();
        public List<ProcessStartInfo> Started { get; } = [];
        private MediaScanner Scanner { get; }

        private Setup()
        {
            var f = Db.ContextFactory;
            Scanner = new MediaScanner(new MediaCategoryService(f), new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f),
                new MovieService(f), new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
        }

        public static async Task<Setup> CreateAsync()
        {
            var setup = new Setup();
            setup.File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
            setup.File("Music", "ABBA", "Gold", "02 - Waterloo.flac");
            setup.File("Music", "ABBA", "Arrival", "01 - When I Kissed the Teacher.flac");
            setup.File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e03 - Our Mrs. Reynolds.mkv");
            setup.File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e01 - Serenity.mkv");
            setup.File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e02 - The Train Job.mkv");
            var categories = new MediaCategoryService(setup.Db.ContextFactory);
            categories.AddRecord(new MediaCategory { Name = "Music", MediaClass = "Music", RootFolder = setup.Path("Music") });
            categories.AddRecord(new MediaCategory { Name = "TV", MediaClass = "TV", RootFolder = setup.Path("TV") });
            await setup.Scanner.ScanAllMediaCategoriesAsync();
            return setup;
        }

        public string Path(params string[] parts) => System.IO.Path.Combine([Temp.SourceFolder, .. parts]);

        private void File(params string[] parts)
        {
            string path = Path(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, [0]);
        }

        public async Task<MediaSearchViewModel> SearchAsync(string text, bool showDeleted = false)
        {
            var f = Db.ContextFactory;
            var player = new MediaPlayerService(Temp.SettingsService, playlistFolder: Path("playlists"))
            {
                FileExists = p => p is Vlc or Wmp,
                AppPath = exe => exe == "vlc.exe" ? Vlc : Wmp,
                Launch = Started.Add,
                Random = new Random(3),
            };
            var vm = new MediaSearchViewModel(Scanner, new MediaSearchService(f), new AlbumTypeService(f), new MovieTypeService(f), new TVShowTypeService(f),
                null, null, player);
            await vm.LoadCategoriesCommand.ExecuteAsync(null);
            vm.SearchText = text;
            vm.ShowDeleted = showDeleted;
            await vm.SearchCommand.ExecuteAsync(null);
            return vm;
        }

        /// <summary>The sNNeNN of each file in the last playlist started, in order.</summary>
        public string[] PlaylistEpisodes() => System.IO.File.ReadAllLines(Started.Last().ArgumentList.Last())
            .Where(l => !l.StartsWith('#')).Select(l => System.Text.RegularExpressions.Regex.Match(l, @"s\d\de\d\d").Value).ToArray();

        public void Dispose()
        {
            Temp.Dispose();
            Db.Dispose();
        }
    }
}
