using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDA.Media.Data;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

/// <summary>
/// How the app connects to the database: the connection string, the DI registration (AddMediaData), the
/// connection check and the database settings. Tests marked as needing SQL Server use SqlServerTestDatabase.
/// </summary>
[TestClass]
public sealed class DatabaseConnectionTests
{
    // An address nothing listens on: connecting fails straight away (connection refused).
    private const string UnreachableServer = "localhost,1";

    // Connection string and options.
    // ==================================================================================================

    [TestMethod]
    public void TestConnectionStringUsesWindowsAuthentication()
    {
        var builder = new SqlConnectionStringBuilder(DataConnection.BuildConnectionString("PDA-Main", "Media.Master"));

        Assert.AreEqual("PDA-Main", builder.DataSource);
        Assert.AreEqual("Media.Master", builder.InitialCatalog);
        Assert.IsTrue(builder.IntegratedSecurity);
        Assert.AreEqual("", builder.Password);
        Assert.AreEqual(5, builder.ConnectTimeout);
    }

    [TestMethod]
    public void TestUserSettingsDefaultToTheMediaDatabase()
    {
        var settings = new UserSettings();

        Assert.AreEqual("PDA-Main", settings.DatabaseServer);
        Assert.AreEqual("Media.Master", settings.DatabaseName);
        StringAssert.Contains(settings.ConnectionString, "Data Source=PDA-Main");
    }

    // AddMediaData.
    // ==================================================================================================

    [TestMethod]
    public void TestAddMediaDataRegistersTheDataLayer()
    {
        int connectionStringReads = 0;
        var services = new ServiceCollection().AddLogging();
        services.AddMediaData(_ =>
        {
            connectionStringReads++;
            return DataConnection.BuildConnectionString("TestServer", "TestDb");
        }, auditUser: "Tester");

        using var provider = services.BuildServiceProvider();
        Assert.AreEqual(0, connectionStringReads, "the connection string is only read when the data layer is first used");

        var factory = provider.GetRequiredService<IDbContextFactory<DataContext>>();
        using var context = factory.CreateDbContext();

        Assert.AreSame(factory, provider.GetRequiredService<IDbContextFactory<DataContext>>());
        Assert.AreEqual(1, connectionStringReads);
        Assert.AreEqual("Tester", context.AuditUser);
        StringAssert.Contains(context.Database.GetConnectionString(), "TestServer");
        Assert.IsInstanceOfType<SqlServerRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        Assert.IsNotNull(provider.GetRequiredService<AlbumService>());
        Assert.AreEqual("TestServer", provider.GetRequiredService<DatabaseStatusService>().Server);
    }

    [TestMethod]
    public void TestContextFactoryUsesTheSameOptionsAsTheApp()
    {
        using var context = new DataContextFactory(DataConnection.BuildConnectionString("TestServer", "TestDb")).CreateDbContext();

        Assert.AreEqual(DataContext.DefaultAuditUser, context.AuditUser);
        Assert.IsInstanceOfType<SqlServerRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
    }

    [TestMethod]
    public void TestAuditUserIsWrittenToTheDatabase()
    {
        using var db = new SqlServerTestDatabase();
        using var provider = new ServiceCollection().AddLogging().AddMediaData(db.ConnectionString, auditUser: "Tester").BuildServiceProvider();
        var service = provider.GetRequiredService<AlbumService>();

        Album album = service.AddRecord(new Album { ArtistId = 0, Name = "Gold" });

        Assert.AreEqual("Tester", db.Scalar<string>($"SELECT CreatedBy FROM Albums WHERE Id = {album.Id}"));
    }

    // Connection check.
    // ==================================================================================================

    [TestMethod]
    public async Task TestCanConnectToTheDatabase()
    {
        using var db = new SqlServerTestDatabase();
        var status = new DatabaseStatusService(db.ContextFactory);

        Assert.IsTrue(await status.CanConnectAsync());
    }

    [TestMethod]
    public async Task TestUnreachableServerIsReportedQuickly()
    {
        var status = new DatabaseStatusService(new DataContextFactory(DataConnection.BuildConnectionString(UnreachableServer, "Media.Master")));
        var timer = Stopwatch.StartNew();

        bool online = await status.CanConnectAsync();

        Assert.IsFalse(online);
        Assert.IsLessThan(TimeSpan.FromSeconds(30), timer.Elapsed);
        Assert.AreEqual(UnreachableServer, status.Server);
        Assert.AreEqual("Media.Master", status.Database);
    }

    // Main window.
    // ==================================================================================================

    [TestMethod]
    public void TestSavingSettingsKeepsTheDatabaseSettings()
    {
        using var temp = new TempServices();
        var saved = new UserSettings { DatabaseServer = "OtherServer", DatabaseName = "OtherDb" };
        temp.SettingsService.SaveSettings(saved);
        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService);

        vm.DestinationPath = Path.Combine(Path.GetTempPath(), "somewhere-else");

        var reloaded = temp.SettingsService.LoadSettings();
        Assert.AreEqual(vm.DestinationPath, reloaded.DestinationPath);
        Assert.AreEqual("OtherServer", reloaded.DatabaseServer);
        Assert.AreEqual("OtherDb", reloaded.DatabaseName);
    }

    [TestMethod]
    public void TestMainWindowWithoutADataLayerHidesTheDatabaseStatus()
    {
        using var temp = new TempServices();

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService);

        Assert.AreEqual(DatabaseState.NotConfigured, vm.DatabaseState);
        Assert.IsFalse(vm.IsDatabaseConfigured);
    }

    [TestMethod]
    public async Task TestMainWindowShowsAnUnreachableDatabaseAsOffline()
    {
        using var temp = new TempServices();
        var status = new DatabaseStatusService(new DataContextFactory(DataConnection.BuildConnectionString(UnreachableServer, "Media.Master")));

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(),
            new MediaEncodingService(), LoggerFactory.Create(_ => { }).CreateLogger<MainViewModel>(), status);
        await WaitUntil(() => vm.DatabaseState != DatabaseState.Checking);

        Assert.AreEqual(DatabaseState.Offline, vm.DatabaseState);
        Assert.IsTrue(vm.IsDatabaseOffline);
        Assert.AreEqual("localhost,1 / Media.Master", vm.DatabaseDisplayName);
        StringAssert.Contains(vm.DatabaseToolTip, "Can't reach");
    }

    [TestMethod]
    public async Task TestMainWindowShowsAReachableDatabaseAsOnline()
    {
        using var db = new SqlServerTestDatabase();
        using var temp = new TempServices();

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), new FFmpegService(),
            new MediaEncodingService(), LoggerFactory.Create(_ => { }).CreateLogger<MainViewModel>(),
            new DatabaseStatusService(db.ContextFactory));
        await WaitUntil(() => vm.DatabaseState != DatabaseState.Checking);

        Assert.AreEqual(DatabaseState.Online, vm.DatabaseState);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
    }
}
