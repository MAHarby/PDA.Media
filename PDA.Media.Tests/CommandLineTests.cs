using System.IO;
using System.Text.Json;
using PDA.Media.CommandLine;
using PDA.Media.CommandLine.Commands;
using PDA.Media.CommandLine.Configuration;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using Spectre.Console;

namespace PDA.Media.Tests;

/// <summary>The pda-media command: its config file, path mappings, and search / config commands (against SqlServerTestDatabase where they need it).</summary>
[TestClass]
public sealed class CommandLineTests
{
    // Path mappings and settings (no database).
    // ==================================================================================================

    [TestMethod]
    [DataRow(@"\\pda-hp-z620\data\Movies\Heat (1995)\Heat (1995).mkv", "/mnt/nas/data/Movies/Heat (1995)/Heat (1995).mkv", DisplayName = "Windows path to Linux")]
    [DataRow(@"\\PDA-HP-Z620\Data\Music", "/mnt/nas/data/Music", DisplayName = "Ignores case")]
    [DataRow(@"\\pda-hp-z620\data", "/mnt/nas/data", DisplayName = "The folder itself")]
    [DataRow(@"\\pda-hp-z620\data\TV\Firefly", "/mnt/tv/Firefly", DisplayName = "The longest mapping wins")]
    [DataRow(@"\\pda-hp-z620\database\x", @"\\pda-hp-z620\database\x", DisplayName = "Only whole folder names")]
    [DataRow(@"\\Aubrey-NAS\Media\x.mkv", @"\\Aubrey-NAS\Media\x.mkv", DisplayName = "No mapping: as stored")]
    [DataRow(@"\\other\share\x.mkv", @"Z:\share\x.mkv", DisplayName = "Windows target keeps backslashes")]
    public void TestPathMapper(string stored, string expected)
    {
        var mapper = new PathMapper([
            new PathMapping(@"\\pda-hp-z620\data", "/mnt/nas/data"),
            new PathMapping(@"\\pda-hp-z620\data\TV", "/mnt/tv/"),
            new PathMapping(@"\\other", @"Z:\"),
        ]);
        Assert.AreEqual(expected, mapper.Map(stored));
    }

    [TestMethod]
    public void TestConfigFileLocation()
    {
        var environment = new Dictionary<string, string?> { ["HOME"] = "/home/mark" };
        var store = new CliSettingsStore(name => environment.GetValueOrDefault(name));

        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual("/home/mark/.config/pda-media/config.json", store.DefaultPath());
            environment["XDG_CONFIG_HOME"] = "/home/mark/.cfg";
            Assert.AreEqual("/home/mark/.cfg/pda-media/config.json", store.DefaultPath());
        }
        else
        {
            StringAssert.EndsWith(store.DefaultPath(), @"\PDA.Media\pda-media.json");
        }

        environment[CliSettingsStore.ConfigVariable] = Path.Combine(Path.GetTempPath(), "from-variable.json");
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "from-variable.json"), store.ResolvePath(null));
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "explicit.json"), store.ResolvePath(Path.Combine(Path.GetTempPath(), "explicit.json")), "--config wins");
    }

    [TestMethod]
    public void TestSettingsAndConnectionStrings()
    {
        using var temp = new TempFolder();
        var store = new CliSettingsStore();

        CliSettings defaults = store.Load(temp.Path("missing.json"));
        Assert.AreEqual("PDA-Main / Media.Master (Windows authentication)", defaults.Describe());
        StringAssert.Contains(defaults.BuildConnectionString(), "Integrated Security=True");

        File.WriteAllText(temp.Path("login.json"), """
            {
              // Comments are allowed.
              "Server": "pda-main.local",
              "UserId": "pda_media",
              "Password": "secret",
              "PathMappings": [ { "From": "\\\\pda-hp-z620\\data", "To": "/mnt/nas" } ],
            }
            """);
        CliSettings login = store.Load(temp.Path("login.json"));
        Assert.AreEqual("pda-main.local / Media.Master (SQL login pda_media)", login.Describe(), "never the password");
        string connection = login.BuildConnectionString();
        StringAssert.Contains(connection, "User ID=pda_media");
        StringAssert.Contains(connection, "Integrated Security=False");
        Assert.AreEqual(@"\\pda-hp-z620\data", login.PathMappings.Single().From);

        File.WriteAllText(temp.Path("bad.json"), "{ not json");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Load(temp.Path("bad.json")));
    }

    [TestMethod]
    public void TestSplitList()
    {
        CollectionAssert.AreEqual(new[] { "Music", "Movies", "TV Shows" }, SearchCommand.SplitList(["Music,Movies", " TV Shows "]));
        Assert.IsEmpty(SearchCommand.SplitList(null));
    }

    // Commands.
    // ==================================================================================================

    [TestMethod]
    public async Task TestHelpAndNoText()
    {
        using var cli = new Cli();

        Assert.AreEqual(0, await cli.RunAsync("--help"));
        StringAssert.Contains(cli.Output, "search <text>");
        Assert.AreEqual(2, await cli.RunAsync("search"));
        StringAssert.Contains(cli.Errors, "Say what to search for");
    }

    [TestMethod]
    public async Task TestConfigInitShowAndTest()
    {
        using var db = new SqlServerTestDatabase();
        using var cli = new Cli();
        string path = cli.Temp.Path("cfg", "pda-media.json");

        Assert.AreEqual(0, await cli.RunAsync("config", "--init", "--config", path));
        Assert.IsTrue(File.Exists(path));
        if (!OperatingSystem.IsWindows()) Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path), "it can hold a password");
        Assert.AreEqual(2, await cli.RunAsync("config", "--init", "--config", path), "an existing file is left as it is");

        cli.WriteConfig(path, new CliSettings { ConnectionString = db.ConnectionString, Password = "never shown" });
        Assert.AreEqual(0, await cli.RunAsync("config", "--test", "--config", path));
        StringAssert.Contains(cli.Output, "Connected to the connection string in the config file");
        Assert.DoesNotContain("never shown", cli.Output + cli.Errors);
    }

    [TestMethod]
    public async Task TestSearch()
    {
        using var db = new SqlServerTestDatabase();
        using var cli = new Cli();
        string library = cli.Temp.Path("nas");
        await ScanLibraryAsync(db, library);
        string config = cli.Temp.Path("config.json");
        cli.WriteConfig(config, new CliSettings { ConnectionString = db.ConnectionString, PathMappings = [new PathMapping(library, "/mnt/nas")] });

        // A track match opens the way down to it; the artist's other album isn't shown.
        Assert.AreEqual(0, await cli.RunAsync("search", "dancing", "--config", config));
        StringAssert.Contains(cli.Output, "Music (1 artist)");
        StringAssert.Contains(cli.Output, "└── ABBA");
        StringAssert.Contains(cli.Output, "Gold (Live · 1 track)");
        StringAssert.Contains(cli.Output, "01 Dancing Queen");
        Assert.DoesNotContain("Arrival", cli.Output);

        // Categories and types; paths through the mapping.
        Assert.AreEqual(0, await cli.RunAsync("search", "*", "--category:Movies", "--paths", "--config", config));
        StringAssert.Contains(cli.Output, "Raw Blu-Rays (1 movie)");
        StringAssert.Contains(cli.Output, "/mnt/nas/Movies/Raw Blu-Rays/Heat (1995)/Heat (1995).mkv");
        Assert.DoesNotContain("Music", cli.Output);

        Assert.AreEqual(0, await cli.RunAsync("search", "*", "-t", "Live", "-e", "--config", config));
        StringAssert.Contains(cli.Output, "Gold (Live · 1 track)");
        Assert.DoesNotContain("Arrival", cli.Output, "an Album, not Live");
        Assert.DoesNotContain("Heat", cli.Output, "no movie type named, so no movies");

        // TV: the show's seasons, with --expand its episodes.
        Assert.AreEqual(0, await cli.RunAsync("search", "firefly", "-e", "--config", config));
        StringAssert.Contains(cli.Output, "Season 1 (2 episodes, missing 2)");
        StringAssert.Contains(cli.Output, "s01e03 Our Mrs. Reynolds");

        Assert.AreEqual(1, await cli.RunAsync("search", "nothing like it", "--config", config));
        StringAssert.Contains(cli.Output, "Nothing found.");
        Assert.AreEqual(2, await cli.RunAsync("search", "abba", "-c", "Books", "--config", config));
        StringAssert.Contains(cli.Errors, "Books can't be searched: its media class");
        Assert.AreEqual(2, await cli.RunAsync("search", "abba", "-c", "Comics", "--config", config));
        StringAssert.Contains(cli.Errors, "There is no media category called \"Comics\". The categories are: Books, Movies, Music, TV.");
        Assert.AreEqual(2, await cli.RunAsync("search", "abba", "-t", "Bogus", "--config", config));
        StringAssert.Contains(cli.Errors, "Album types: Album, Live");
    }

    [TestMethod]
    public async Task TestSearchWithoutTheDatabase()
    {
        using var cli = new Cli();
        string config = cli.Temp.Path("config.json");
        cli.WriteConfig(config, new CliSettings { Server = "localhost,1", Database = "Nowhere", UserId = "nobody", Password = "x" });

        Assert.AreEqual(2, await cli.RunAsync("search", "abba", "--config", config));
        StringAssert.Contains(cli.Errors, "Couldn't search localhost,1 / Nowhere (SQL login nobody)");
        StringAssert.Contains(cli.Errors, "pda-media config --test");
    }

    /// <summary>ABBA (Gold: Live, Arrival: Album), a movie and Firefly scanned from folders under <paramref name="root"/>.</summary>
    private static async Task ScanLibraryAsync(SqlServerTestDatabase db, string root)
    {
        void File(params string[] parts)
        {
            string path = Path.Combine([root, .. parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, [0]);
        }
        File("Music", "ABBA", "Gold", "01 - Dancing Queen.flac");
        File("Music", "ABBA", "Arrival", "01 - When I Kissed the Teacher.flac");
        File("Movies", "Raw Blu-Rays", "Heat (1995)", "Heat (1995).mkv");
        File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e01 - Serenity.mkv");
        File("TV", "Firefly (2002)", "Season 01", "Firefly (2002) - s01e03 - Our Mrs. Reynolds.mkv");
        var f = db.ContextFactory;
        int live = new AlbumTypeService(f).AddRecord(new AlbumType { Name = "Live" }).Id;
        var categories = new MediaCategoryService(f);
        categories.AddRecord(new MediaCategory { Name = "Music", MediaClass = "Music", RootFolder = Path.Combine(root, "Music"), AlbumTypeId = live });
        categories.AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie", RootFolder = Path.Combine(root, "Movies") });
        categories.AddRecord(new MediaCategory { Name = "TV", MediaClass = "TV", RootFolder = Path.Combine(root, "TV") });
        categories.AddRecord(new MediaCategory { Name = "Books", MediaClass = "Audiobooks", RootFolder = Path.Combine(root, "Books") });
        var scanner = new MediaScanner(categories, new ArtistService(f), new AlbumService(f), new AlbumTypeService(f), new TrackService(f), new MovieService(f),
            new TVShowService(f), new TVShowEpisodeService(f), new MediaCacheService(f), new SettingService(f));
        await scanner.ScanAllMediaCategoriesAsync();

        // Arrival is an Album (type 1).
        var albums = new AlbumService(f);
        Album arrival = albums.GetAllRecords("Arrival").Single();
        arrival.AlbumTypeId = 1;
        albums.UpdateRecord(arrival.Id, arrival);
    }

    /// <summary>pda-media with its output captured (no colours) and a temp folder; no environment variable chooses the config file.</summary>
    private sealed class Cli : IDisposable
    {
        private readonly StringWriter _out = new(), _error = new();

        public TempFolder Temp { get; } = new();
        public string Output { get; private set; } = "";
        public string Errors { get; private set; } = "";

        public async Task<int> RunAsync(params string[] args)
        {
            _out.GetStringBuilder().Clear();
            _error.GetStringBuilder().Clear();
            var context = new CliContext
            {
                Out = Console(_out),
                Error = Console(_error),
                OutWriter = _out,
                ErrorWriter = _error,
                Store = new CliSettingsStore(name => name == "HOME" ? Temp.Path("home") : null),
            };
            int exit = await PdaMediaCli.RunAsync(args, context);
            (Output, Errors) = (_out.ToString(), _error.ToString());
            return exit;
        }

        private static IAnsiConsole Console(TextWriter writer)
        {
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(writer),
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
            });
            console.Profile.Width = 250;
            return console;
        }

        public void WriteConfig(string path, CliSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
        }

        public void Dispose() => Temp.Dispose();
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pda_cli_test_" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(_root);

        public string Path(params string[] parts) => System.IO.Path.Combine([_root, .. parts]);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
