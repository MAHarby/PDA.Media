using System.IO;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;

namespace PDA.Media.Tests;

[TestClass]
public sealed class PlexNamingTests
{
    private static readonly string TvRoot = Path.Combine(Path.GetTempPath(), "media", "tv");
    private static readonly string MovieRoot = Path.Combine(Path.GetTempPath(), "media", "movies");

    private static string Expected(params string[] parts) => Path.Combine(parts);

    // TV episodes.
    // ==================================================================================================

    [TestMethod]
    [DataRow("The Office (US) (2005)", "Season 1", "The Office (US) (2005) - S01E01 - Pilot Bluray-1080p.mkv",
        "The Office (US) (2005)", "Season 01", "The Office (US) (2005) - s01e01 - Pilot.mkv", DisplayName = "Sonarr standard naming")]
    [DataRow("Breaking Bad", "Season 02", "Breaking.Bad.S02E03.Bit.by.a.Dead.Bee.1080p.BluRay.x265-GROUP.mkv",
        "Breaking Bad", "Season 02", "Breaking Bad - s02e03 - Bit by a Dead Bee.mkv", DisplayName = "Scene naming with dots")]
    [DataRow("Show", "Season 1", "Show - S01E01-E02 - Two Parter [HDTV-720p].mkv",
        "Show", "Season 01", "Show - s01e01-e02 - Two Parter.mkv", DisplayName = "Multi-episode file")]
    [DataRow("Show", "Season 3", "Show.S03E10.720p.HDTV.x264.mkv",
        "Show", "Season 03", "Show - s03e10.mkv", DisplayName = "No episode title")]
    [DataRow("Doctor Who (2005)", "Season 1", "Doctor Who 1x02 The End of the World DVD.mkv",
        "Doctor Who (2005)", "Season 01", "Doctor Who (2005) - s01e02 - The End of the World.mkv", DisplayName = "1x02 episode marker")]
    [DataRow("Show", "Specials", "Show - S00E01 - Christmas Special WEBDL-1080p.mkv",
        "Show", "Season 00", "Show - s00e01 - Christmas Special.mkv", DisplayName = "Specials")]
    [DataRow("Show", "Season 1", "Show - S01E05 - What? Part 1 [Bluray-1080p][x265].mkv",
        "Show", "Season 01", "Show - s01e05 - What Part 1.mkv", DisplayName = "Characters Windows doesn't allow")]
    [DataRow("Star Trek - Picard (2020)", "Season 1", "Star Trek: Picard - S01E01 - Remembrance WEBRip-2160p.mkv",
        "Star Trek - Picard (2020)", "Season 01", "Star Trek - Picard (2020) - s01e01 - Remembrance.mkv", DisplayName = "Show folder name wins")]
    public void TestTvEpisode(string showFolder, string seasonFolder, string fileName,
        string expectedShow, string expectedSeason, string expectedFile)
    {
        string source = Path.Combine(TvRoot, showFolder, seasonFolder, fileName);

        var result = PlexNaming.GetOutputName(TvRoot, source, "mkv");

        Assert.AreEqual(PlexMediaKind.TvEpisode, result.Kind);
        Assert.AreEqual(Expected(expectedShow, expectedSeason, expectedFile), result.RelativePath);
    }

    [TestMethod]
    public void TestTvEpisode_SourceRootIsTheShowFolder()
    {
        string root = Path.Combine(TvRoot, "Show A (2010)");
        string source = Path.Combine(root, "Season 1", "Show A - S01E02 - Two DVD.mkv");

        var result = PlexNaming.GetOutputName(root, source, "mkv");

        Assert.AreEqual(Expected("Show A (2010)", "Season 01", "Show A (2010) - s01e02 - Two.mkv"), result.RelativePath);
    }

    [TestMethod]
    public void TestTvEpisode_FileDirectlyInSourceRootUsesShowNameFromFile()
    {
        string source = Path.Combine(TvRoot, "Firefly.S01E01.Serenity.WEB-DL.mkv");

        var result = PlexNaming.GetOutputName(TvRoot, source, "mkv");

        Assert.AreEqual(Expected("Firefly", "Season 01", "Firefly - s01e01 - Serenity.mkv"), result.RelativePath);
    }

    [TestMethod]
    public void TestTvEpisode_UsesProfileContainerExtension()
    {
        string source = Path.Combine(TvRoot, "Show", "Season 1", "Show - S01E01 - Pilot.avi");

        var result = PlexNaming.GetOutputName(TvRoot, source, "MP4");

        Assert.AreEqual(Expected("Show", "Season 01", "Show - s01e01 - Pilot.mp4"), result.RelativePath);
    }

    // Movies.
    // ==================================================================================================

    [TestMethod]
    [DataRow("Disclosure Day (2026) Remux-1080p.mkv", "Disclosure Day (2026)", DisplayName = "Radarr standard naming")]
    [DataRow("The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP.mkv", "The Matrix (1999)", DisplayName = "Scene naming, year without brackets")]
    [DataRow("Blade Runner 2049 (2017) Bluray-2160p.mkv", "Blade Runner 2049 (2017)", DisplayName = "Number in the title")]
    [DataRow("2001 A Space Odyssey (1968) [Bluray-1080p].mkv", "2001 A Space Odyssey (1968)", DisplayName = "Title starting with a year")]
    [DataRow("1917 (2019) WEBDL-1080p.mkv", "1917 (2019)", DisplayName = "Title that is a year")]
    [DataRow("Charlotte's Web (1973) DVD.mkv", "Charlotte's Web (1973)", DisplayName = "Title containing a tag-like word")]
    [DataRow("Alien (1979) Directors Cut Bluray-1080p.mkv", "Alien (1979)", DisplayName = "Text after the year is dropped")]
    [DataRow("Some Home Movie Bluray-1080p.mkv", "Some Home Movie", DisplayName = "No year")]
    public void TestMovie(string fileName, string expectedName)
    {
        string source = Path.Combine(MovieRoot, "Any Folder", fileName);

        var result = PlexNaming.GetOutputName(MovieRoot, source, "mkv");

        Assert.AreEqual(PlexMediaKind.Movie, result.Kind);
        Assert.AreEqual(Expected(expectedName, expectedName + ".mkv"), result.RelativePath);
    }
}
