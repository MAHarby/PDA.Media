using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils;
using PDA.Media.Utils.Logging;

namespace PDA.Media.Tests;

[TestClass]
public sealed class MediaScannerTests
{
    [TestMethod]
    public void TestAppContainerCreatesTheScanner()
    {
        // The app's own registrations: AddMediaData + AddMediaScanner, with the logging the app sets up. Nothing
        // connects to a database until a service is used.
        using var services = ServiceConfiguration.BuildServiceProvider(new AuditLogSink(), "test.log");

        var scanner = services.GetRequiredService<MediaScanner>();

        Assert.AreSame(scanner, services.GetRequiredService<MediaScanner>(), "one scanner for the app (singleton)");
    }

    [TestMethod]
    public void TestScanUsesTheInjectedServicesAndLogger()
    {
        using var db = new SqlServerTestDatabase();
        var categories = new MediaCategoryService(db.ContextFactory);
        categories.AddRecord(new MediaCategory { Name = "Movies", MediaClass = "Movie", RootFolder = @"\\nas\Movies" });
        categories.AddRecord(new MediaCategory { Name = "Old", MediaClass = "Movie", IsActive = false });
        var logger = new CapturingLogger<MediaScanner>();

        // Without DI: build the services from one context factory and pass them in.
        var scanner = new MediaScanner(categories, new ArtistService(db.ContextFactory), new AlbumService(db.ContextFactory),
            new TrackService(db.ContextFactory), new MovieService(db.ContextFactory), new TVShowService(db.ContextFactory),
            new TVShowEpisodeService(db.ContextFactory), logger);

        Assert.IsTrue(scanner.ScanAllMediaCategories());
        CollectionAssert.AreEqual(new[]
        {
            "Scanning 1 active media categories",
            @"Media category Movies (Movie): \\nas\Movies",
        }, logger.Messages);
    }

    /// <summary>An ILogger that keeps the formatted messages, for checking what a class logged.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
