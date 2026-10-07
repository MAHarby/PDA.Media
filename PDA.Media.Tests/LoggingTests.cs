using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.Logging;
using PDA.Media.Utils.Services;
using PDA.Media.Utils.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PDA.Media.Tests;

[TestClass]
public sealed class LoggingTests
{
    [TestMethod]
    public void TestAuditLogEntry_FormatsMessageSourceAndLevel()
    {
        var capture = new CapturingSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(capture).CreateLogger())
        {
            logger.ForContext("SourceContext", "PDA.Media.Utils.ViewModels.MainViewModel")
                .Warning("Source folder {SourcePath} does not exist", @"C:\Media\TV");
        }

        var entry = AuditLogEntry.FromLogEvent(capture.Events.Single());

        Assert.AreEqual(@"Source folder C:\Media\TV does not exist", entry.Message, "String values should not be quoted");
        Assert.AreEqual("MainViewModel", entry.Source);
        Assert.AreEqual("WRN", entry.LevelText);
        Assert.IsTrue(entry.IsWarning);
        Assert.IsFalse(entry.IsError);
        Assert.EndsWith(@"[WRN] MainViewModel: Source folder C:\Media\TV does not exist", entry.DisplayText);
    }

    [TestMethod]
    public void TestAuditLogEntry_AppendsExceptionSummary()
    {
        var capture = new CapturingSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(capture).CreateLogger())
        {
            logger.Error(new IOException("Network path not found"), "Failed to scan source folder");
        }

        var entry = AuditLogEntry.FromLogEvent(capture.Events.Single());

        Assert.IsTrue(entry.IsError);
        Assert.AreEqual("App", entry.Source);
        Assert.AreEqual("Failed to scan source folder (IOException: Network path not found)", entry.Message);
    }

    [TestMethod]
    public void TestLoggingSetup_WritesToNewTimestampedFileEachRun()
    {
        string logDirectory = Path.Combine(Path.GetTempPath(), "pda_logs_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = LoggingSetup.CreateLogger(new AuditLogSink(), out string logFilePath, logDirectory);
            logger.Information("Hello from {Test}", "the logging test");
            (logger as IDisposable)?.Dispose();

            Assert.AreEqual(logDirectory, Path.GetDirectoryName(logFilePath));
            StringAssert.Matches(Path.GetFileName(logFilePath), new System.Text.RegularExpressions.Regex(@"^PDA\.Media\.Utils_\d{8}_\d{6}\.log$"));
            string contents = File.ReadAllText(logFilePath);
            Assert.Contains("[INF]", contents);
            Assert.Contains("Hello from the logging test", contents);
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, true);
        }
    }

    [TestMethod]
    public void TestLoggingSetup_DeleteOldLogFiles_KeepsNewest()
    {
        string logDirectory = Path.Combine(Path.GetTempPath(), "pda_logs_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
        try
        {
            var start = new DateTime(2026, 1, 1, 9, 0, 0);
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(LoggingSetup.GetLogFilePath(logDirectory, start.AddDays(i)), "log");
            }
            File.WriteAllText(Path.Combine(logDirectory, "unrelated.txt"), "keep me");

            LoggingSetup.DeleteOldLogFiles(logDirectory, 2);

            var remaining = Directory.GetFiles(logDirectory).Select(Path.GetFileName).OrderBy(n => n).ToList();
            CollectionAssert.AreEqual(new[]
            {
                "PDA.Media.Utils_20260104_090000.log",
                "PDA.Media.Utils_20260105_090000.log",
                "unrelated.txt"
            }, remaining);
        }
        finally
        {
            Directory.Delete(logDirectory, true);
        }
    }

    [TestMethod]
    public void TestMainViewModel_LogsWarningForMissingSourceFolder()
    {
        string tempSettingsFile = Path.Combine(Path.GetTempPath(), "pda_settings_test_" + Guid.NewGuid().ToString("N") + ".json");
        string tempProfilesFile = Path.Combine(Path.GetTempPath(), "pda_profiles_test_" + Guid.NewGuid().ToString("N") + ".json");
        string missingFolder = Path.Combine(Path.GetTempPath(), "pda_missing_" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new AppSettingsService(tempSettingsFile);
            settingsService.SaveSettings(new UserSettings { SourcePath = missingFolder, GeneralProfile = "" });

            var logger = new ListLogger<MainViewModel>();
            _ = new MainViewModel(settingsService, new EncoderProfileService(tempProfilesFile), new AuditLogSink(), logger);

            Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("Restored last session")));
            Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(missingFolder)),
                "A missing source folder should be logged as a warning");
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
            if (File.Exists(tempProfilesFile)) File.Delete(tempProfilesFile);
        }
    }

    [TestMethod]
    public void TestLogFileService_ReadsOpenLogAndSavesTimestampedCopy()
    {
        string root = Path.Combine(Path.GetTempPath(), "pda_logs_test_" + Guid.NewGuid().ToString("N"));
        string downloads = Path.Combine(root, "Downloads");
        try
        {
            var logger = LoggingSetup.CreateLogger(new AuditLogSink(), out string logFilePath, root);
            try
            {
                logger.Information("Line written while the file is open");
                var service = new LogFileService(logFilePath, new ListLogger<LogFileService>());

                // Serilog still has the file open for writing here.
                Assert.Contains("Line written while the file is open", service.ReadLog());

                string copyPath = service.SaveCopy(downloads);
                Assert.AreEqual(downloads, Path.GetDirectoryName(copyPath));
                StringAssert.Matches(Path.GetFileName(copyPath), new System.Text.RegularExpressions.Regex(@"^PDA\.Media\.Utils_\d{8}_\d{6}\.log$"));
                Assert.Contains("Line written while the file is open", File.ReadAllText(copyPath));
            }
            finally
            {
                (logger as IDisposable)?.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void TestAuditLogSink_ClearRemovesEntries()
    {
        var sink = new AuditLogSink();
        sink.Entries.Add(new AuditLogEntry(DateTimeOffset.Now, LogEventLevel.Information, "Test", "Message"));

        sink.Clear();

        Assert.IsEmpty(sink.Entries);
    }

    [TestMethod]
    public void TestMainViewModel_StartupScansSourceAndLoadsProfileOnce()
    {
        using var temp = new TempServices();
        Directory.CreateDirectory(Path.Combine(temp.SourceFolder, "Season 1"));
        temp.SettingsService.SaveSettings(new UserSettings { GeneralProfile = "TV Show", SourcePath = temp.SourceFolder });

        var logger = new ListLogger<MainViewModel>();
        _ = new MainViewModel(temp.SettingsService, temp.ProfileService, new AuditLogSink(), logger);

        Assert.AreEqual(1, logger.Entries.Count(e => e.Message.StartsWith("Scanning source folder")));
        Assert.AreEqual(1, logger.Entries.Count(e => e.Message.StartsWith("Active encoding profile")));
    }

    [TestMethod]
    public void TestMainViewModel_GeneralProfileChange_SavesAndScansOnce()
    {
        using var temp = new TempServices();
        var logger = new ListLogger<MainViewModel>();
        var settingsLogger = new ListLogger<AppSettingsService>();
        var vm = new MainViewModel(new AppSettingsService(settingsLogger, temp.SettingsFile), temp.ProfileService, new AuditLogSink(), logger);
        logger.Entries.Clear();
        settingsLogger.Entries.Clear();

        vm.SelectedGeneralProfile = "Movie";

        Assert.AreEqual(1, settingsLogger.Entries.Count(e => e.Message.StartsWith("Saved user settings")), "Settings should be saved once");
        Assert.AreEqual(0, logger.Entries.Count(e => e.Message.StartsWith("No source path set")), "Source should not be cleared first");
        Assert.AreEqual(1, logger.Entries.Count(e => e.Message.StartsWith("Source path changed")));
    }

    [TestMethod]
    public void TestMainViewModel_RefreshProfiles_UnchangedSelectionDoesNotSaveSettings()
    {
        using var temp = new TempServices();
        var settingsLogger = new ListLogger<AppSettingsService>();
        var vm = new MainViewModel(new AppSettingsService(settingsLogger, temp.SettingsFile), temp.ProfileService,
            new AuditLogSink(), new ListLogger<MainViewModel>());
        string? selected = vm.SelectedEncoderProfile;
        settingsLogger.Entries.Clear();

        vm.RefreshProfiles();

        Assert.AreEqual(selected, vm.SelectedEncoderProfile);
        Assert.IsNotNull(vm.CurrentEncodeProfile);
        Assert.AreEqual(0, settingsLogger.Entries.Count(e => e.Message.StartsWith("Saved user settings")));
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
