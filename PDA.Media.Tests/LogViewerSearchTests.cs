using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;

namespace PDA.Media.Tests;

[TestClass]
public sealed class LogViewerSearchTests
{
    private string _logFile = string.Empty;

    [TestInitialize]
    public void CreateLogFile()
    {
        _logFile = Path.Combine(Path.GetTempPath(), "pda_logviewer_test_" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(_logFile,
            "[INF] Loaded 7 encoding profiles\r\n" +
            "[WRN] Source folder missing\r\n" +
            "[INF] Saved Encoding profile Bluray TV\r\n" +
            "[INF] Main window opened\r\n");
    }

    [TestCleanup]
    public void DeleteLogFile()
    {
        if (File.Exists(_logFile)) File.Delete(_logFile);
    }

    private LogViewerViewModel CreateViewModel() =>
        new(new LogFileService(_logFile, NullLogger<LogFileService>.Instance), NullLogger<LogViewerViewModel>.Instance);

    [TestMethod]
    public void TestSearch_FindsCaseInsensitiveMatchesAndSelectsFirst()
    {
        var vm = CreateViewModel();

        vm.SearchText = "ENCODING";

        Assert.AreEqual(2, vm.MatchCount);
        Assert.AreEqual("1 of 2", vm.MatchSummary);
        Assert.IsNotNull(vm.CurrentMatch);
        Assert.AreEqual("encoding", vm.LogText.Substring(vm.CurrentMatch.Value.Start, vm.CurrentMatch.Value.Length));
        Assert.IsTrue(vm.FindNextCommand.CanExecute(null));
    }

    [TestMethod]
    public void TestSearch_NextAndPreviousWrapAround()
    {
        var vm = CreateViewModel();
        vm.SearchText = "encoding";
        int first = vm.CurrentMatch!.Value.Start;

        vm.FindNextCommand.Execute(null);
        Assert.AreEqual("2 of 2", vm.MatchSummary);
        Assert.AreEqual("Encoding", vm.LogText.Substring(vm.CurrentMatch!.Value.Start, vm.CurrentMatch.Value.Length));

        vm.FindNextCommand.Execute(null);
        Assert.AreEqual("1 of 2", vm.MatchSummary, "Next from the last match wraps to the first");
        Assert.AreEqual(first, vm.CurrentMatch!.Value.Start);

        vm.FindPreviousCommand.Execute(null);
        Assert.AreEqual("2 of 2", vm.MatchSummary, "Previous from the first match wraps to the last");
    }

    [TestMethod]
    public void TestSearch_NoMatchesDisablesNavigation()
    {
        var vm = CreateViewModel();

        vm.SearchText = "ffmpeg";

        Assert.AreEqual(0, vm.MatchCount);
        Assert.AreEqual("No matches", vm.MatchSummary);
        Assert.IsNull(vm.CurrentMatch);
        Assert.IsFalse(vm.FindNextCommand.CanExecute(null));
        Assert.IsFalse(vm.FindPreviousCommand.CanExecute(null));
    }

    [TestMethod]
    public void TestSearch_ClearSearchResetsState()
    {
        var vm = CreateViewModel();
        vm.SearchText = "encoding";

        vm.ClearSearchCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.SearchText);
        Assert.AreEqual(string.Empty, vm.MatchSummary);
        Assert.IsNull(vm.CurrentMatch);
    }

    [TestMethod]
    public void TestRefresh_KeepsCurrentMatchNumberAndFindsNewMatches()
    {
        var vm = CreateViewModel();
        vm.SearchText = "encoding";
        vm.FindNextCommand.Execute(null);

        File.AppendAllText(_logFile, "[INF] Reset encoding profiles to 7 defaults\r\n");
        vm.RefreshCommand.Execute(null);

        Assert.AreEqual(3, vm.MatchCount);
        Assert.AreEqual("2 of 3", vm.MatchSummary);
    }

    [TestMethod]
    public void TestRefresh_NormalisesWindowsLineEndings()
    {
        var vm = CreateViewModel();

        Assert.DoesNotContain("\r", vm.LogText, "Match positions must line up with the TextBox text");
        Assert.Contains("4 lines", vm.StatusText);
    }
}
