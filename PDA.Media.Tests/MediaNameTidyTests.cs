using PDA.Media.Data.Entities;
using PDA.Media.Utils.Services;

namespace PDA.Media.Tests;

/// <summary>Tidy-name suggestions (no disk or database needed).</summary>
[TestClass]
public sealed class MediaNameTidyTests
{
    [TestMethod]
    [DataRow("Heat.1995.1080p.BluRay.x265-GROUP", "Heat (1995)", DisplayName = "Release name")]
    [DataRow("Heat (1995)", "Heat (1995)", DisplayName = "Already tidy")]
    [DataRow("Star Trek: First Contact (1996)", "Star Trek - First Contact (1996)", DisplayName = "Colon")]
    [DataRow("Alien [Remux]", "Alien", DisplayName = "No year")]
    [DataRow("Mr. & Mrs. Smith (2005)", "Mr. & Mrs. Smith (2005)", DisplayName = "Dots in a title with spaces stay")]
    public void TestMovies(string name, string expected)
    {
        Assert.AreEqual(expected, MediaNameTidy.Suggest(new Movie { Name = name }));
    }

    [TestMethod]
    public void TestShowsAndEpisodes()
    {
        Assert.AreEqual("Firefly (2002)", MediaNameTidy.Suggest(new TVShow { Name = "Firefly", ReleaseYear = 2002 }));
        Assert.AreEqual("Firefly (2002)", MediaNameTidy.CurrentName(new TVShow { Name = "Firefly", ReleaseYear = 2002 }));
        Assert.AreEqual("Firefly", MediaNameTidy.Suggest(new TVShow { Name = "Firefly" }));
        Assert.AreEqual("Serenity", MediaNameTidy.Suggest(new TVShowEpisode { Name = "Serenity.1080p.WEB-DL" }));
        Assert.AreEqual("Our Mrs. Reynolds", MediaNameTidy.Suggest(new TVShowEpisode { Name = "Our Mrs. Reynolds" }), "a real dot stays");
    }

    [TestMethod]
    [DataRow("Gold [FLAC]", "Gold")]
    [DataRow("A Night at the Opera (2011 Remaster)", "A Night at the Opera")]
    [DataRow("Homogenic (24bit-96kHz) [WEB]", "Homogenic")]
    [DataRow("Greatest Hits (Deluxe Edition)", "Greatest Hits (Deluxe Edition)")]
    [DataRow("  ABBA  ", "ABBA")]
    [DataRow("AC/DC", "ACDC")]
    public void TestMusic(string name, string expected)
    {
        Assert.AreEqual(expected, MediaNameTidy.Suggest(new Album { Name = name }));
    }
}
