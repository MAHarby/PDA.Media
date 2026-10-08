using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

[TestClass]
public sealed class MediaNodeTests
{
    [TestMethod]
    public void TestFolder_TotalsFileCountAndSizeAtAnyDepth()
    {
        var season = new MediaNode("Season 1", "/tv/Show/Season 1", new ObservableCollection<MediaNode>
        {
            new("Episode 1", "/tv/Show/Season 1/e1.mkv", 1000),
            new("Episode 2", "/tv/Show/Season 1/e2.mkv", 2000)
        });
        var show = new MediaNode("Show", "/tv/Show", new ObservableCollection<MediaNode>
        {
            season,
            new("Extra", "/tv/Show/extra.mkv", 500)
        });

        Assert.IsTrue(show.IsFolder);
        Assert.AreEqual(3, show.FileCount, "Two in the season plus one in the show folder");
        Assert.AreEqual(3500, show.Size);
        Assert.AreEqual("3 files", show.FileCountText);
        Assert.AreEqual("2 files", season.FileCountText);
    }

    [TestMethod]
    public void TestFile_ShowsSizeAndSingularCount()
    {
        var file = new MediaNode("Episode", "/tv/e.mkv", 13421772800L);
        var folder = new MediaNode("Season", "/tv/Season", new ObservableCollection<MediaNode> { file });

        Assert.IsFalse(file.IsFolder);
        Assert.AreEqual("- 12.5 GB", file.SizeText);
        Assert.AreEqual("1 file", folder.FileCountText);
    }

    [TestMethod]
    public void TestSourceScan_ReadsFileSizesFromTheFolderListing()
    {
        using var temp = new TempServices();
        string season = Path.Combine(temp.SourceFolder, "Show", "Season 1");
        Directory.CreateDirectory(season);
        File.WriteAllBytes(Path.Combine(season, "Show - S01E01.mkv"), new byte[2048]);
        File.WriteAllBytes(Path.Combine(season, "Show - S01E02.mkv"), new byte[1024]);
        temp.SettingsService.SaveSettings(new UserSettings { GeneralProfile = "", SourcePath = temp.SourceFolder });

        var vm = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), NullLogger<MainViewModel>.Instance);

        var root = vm.SourceMediaNodes.Single();
        Assert.AreEqual(2, root.FileCount);
        Assert.AreEqual(3072, root.Size);
        var episodeSizes = root.SubNodes!.Single().SubNodes!.Single().SubNodes!.Select(n => n.Size).OrderBy(s => s);
        CollectionAssert.AreEqual(new long[] { 1024, 2048 }, episodeSizes.ToList());
    }
}
