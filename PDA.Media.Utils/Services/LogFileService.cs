using System;
using System.IO;
using Microsoft.Extensions.Logging;
using PDA.Media.Utils.Logging;

namespace PDA.Media.Utils.Services;

/// <summary>
/// Gives access to the current run's log file: reading it for the log viewer and saving copies.
/// </summary>
public class LogFileService
{
    private readonly ILogger<LogFileService> _logger;

    public string LogFilePath { get; }

    public LogFileService(string logFilePath, ILogger<LogFileService> logger)
    {
        LogFilePath = logFilePath;
        _logger = logger;
    }

    /// <summary>
    /// Reads the whole log file. The file stays open for writing by Serilog, so it is opened with shared access.
    /// </summary>
    public string ReadLog()
    {
        if (!File.Exists(LogFilePath)) return string.Empty;

        using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Saves a copy of the log into <paramref name="directory"/>, named with the save time using the
    /// same format as the log files (e.g. <c>PDA.Media.Utils_20261007_153012.log</c>).
    /// </summary>
    /// <returns>The full path of the saved copy.</returns>
    public string SaveCopy(string directory)
    {
        Directory.CreateDirectory(directory);
        string destinationPath = LoggingSetup.GetLogFilePath(directory, DateTime.Now);

        File.WriteAllText(destinationPath, ReadLog());
        _logger.LogInformation("Saved a copy of the log to {LogCopyPath}", destinationPath);
        return destinationPath;
    }
}
