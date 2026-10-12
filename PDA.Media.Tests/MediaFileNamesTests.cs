using PDA.Media.Scanner.Services;

namespace PDA.Media.Tests;

/// <summary>Reading media details from file and folder names (no disk or database needed).</summary>
[TestClass]
public sealed class MediaFileNamesTests
{
    [TestMethod]
    [DataRow("01 - Dancing Queen.flac", 1, "Dancing Queen", DisplayName = "NN - Title")]
    [DataRow("01. Dancing Queen.mp3", 1, "Dancing Queen", DisplayName = "NN. Title")]
    [DataRow("01 Dancing Queen.m4a", 1, "Dancing Queen", DisplayName = "NN Title")]
    [DataRow("12-Knowing Me, Knowing You.flac", 12, "Knowing Me, Knowing You", DisplayName = "NN-Title")]
    [DataRow("2-03 - Waterloo.flac", 203, "Waterloo", DisplayName = "Disc-track")]
    [DataRow("1.05 Mamma Mia.flac", 105, "Mamma Mia", DisplayName = "Disc.track")]
    [DataRow("2001 A Space Odyssey.flac", 0, "2001 A Space Odyssey", DisplayName = "Number that is part of the title")]
    [DataRow("Intro.flac", 0, "Intro", DisplayName = "No number")]
    [DataRow("05 - Jóga.flac", 5, "Jóga", DisplayName = "Unicode title")]
    public void TestParseTrackFileName(string fileName, int trackNo, string title)
    {
        Assert.AreEqual((trackNo, title), MediaFileNames.ParseTrackFileName(fileName));
    }

    [TestMethod]
    [DataRow("The Office (US) (2005)", "The Office (US)", 2005)]
    [DataRow("Alien (1979)", "Alien", 1979)]
    [DataRow("Firefly", "Firefly", 0)]
    [DataRow("1917 (2019)", "1917", 2019)]
    public void TestParseNameAndYear(string folderName, string name, int year)
    {
        Assert.AreEqual((name, year), MediaFileNames.ParseNameAndYear(folderName));
    }

    [TestMethod]
    [DataRow("The Office (US) (2005) - s01e02 - Diversity Day.mkv", "Season 01", 1, 2, "Diversity Day", DisplayName = "Plex naming")]
    [DataRow("Show - S02E10 - Title.mp4", "Season 2", 2, 10, "Title", DisplayName = "Upper case marker")]
    [DataRow("Show - s01e01-e02 - Two Parter.mkv", "Season 01", 1, 1, "Two Parter", DisplayName = "Multi-episode file")]
    [DataRow("Show - s00e01 - Christmas Special.mkv", "Specials", 0, 1, "Christmas Special", DisplayName = "Special")]
    [DataRow("Show - s03e04.mkv", "Season 03", 3, 4, "Episode 4", DisplayName = "No title")]
    [DataRow("Behind the scenes.mkv", "Season 02", 2, 0, "Behind the scenes", DisplayName = "No marker: season from the folder")]
    [DataRow("Behind the scenes.mkv", "Specials", 0, 0, "Behind the scenes", DisplayName = "No marker in Specials")]
    public void TestParseEpisodeFileName(string fileName, string seasonFolder, int season, int episode, string title)
    {
        Assert.AreEqual((season, episode, title), MediaFileNames.ParseEpisodeFileName(fileName, seasonFolder));
    }

    [TestMethod]
    [DataRow("@eaDir", true)]
    [DataRow("#recycle", true)]
    [DataRow(".hidden", true)]
    [DataRow("$RECYCLE.BIN", true)]
    [DataRow("ABBA", false)]
    [DataRow("Season 01", false)]
    public void TestIgnoredFolders(string name, bool ignored)
    {
        Assert.AreEqual(ignored, MediaFileNames.IsIgnoredFolder(Path.Combine("nas", name)));
    }

    [TestMethod]
    public void TestFindCoverArt()
    {
        Assert.AreEqual("Cover.JPG", MediaFileNames.FindCoverArt(["01 - Track.flac", "Cover.JPG", "folder.jpg"]));
        Assert.AreEqual("poster.png", MediaFileNames.FindCoverArt(["movie.mkv", "poster.png"]));
        Assert.IsNull(MediaFileNames.FindCoverArt(["01 - Track.flac", "booklet.pdf"]));
    }

    [TestMethod]
    [DataRow("Music", MediaKind.Music)]
    [DataRow("movies", MediaKind.Movie)]
    [DataRow("Movie", MediaKind.Movie)]
    [DataRow("TV Show", MediaKind.TV)]
    [DataRow("TV-Shows", MediaKind.TV)]
    [DataRow("Unknown", MediaKind.Unknown)]
    [DataRow(null, MediaKind.Unknown)]
    public void TestMediaKindFromMediaClass(string? mediaClass, MediaKind kind)
    {
        Assert.AreEqual(kind, MediaKinds.FromMediaClass(mediaClass));
    }
}
