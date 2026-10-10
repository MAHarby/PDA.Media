using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data;

namespace PDA.Media.Utils.Services;

public class UserSettings
{
    public string GeneralProfile { get; set; } = "General";
    public string EncoderProfile { get; set; } = "Bluray TV";
    public string SourcePath { get; set; } = @"\\pda-hp-z620\data\ARR-Stack\media\tv";
    public string DestinationPath { get; set; } = @"\\Aubrey-NAS\Media\TV Series\ARRstack";

    /// <summary>SQL Server holding the media database (Windows authentication). Read when the app starts.</summary>
    public string DatabaseServer { get; set; } = DataConnection.DefaultServer;

    public string DatabaseName { get; set; } = DataConnection.DefaultDatabase;

    [JsonIgnore]
    public string ConnectionString => DataConnection.BuildConnectionString(DatabaseServer, DatabaseName);
}

public class AppSettingsService
{
    public static readonly string DefaultSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDA.Media");
    public static readonly string DefaultSettingsFilePath = Path.Combine(DefaultSettingsDirectory, "settings.json");

    private readonly string _filePath;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(string? customFilePath = null)
        : this(NullLogger<AppSettingsService>.Instance, customFilePath)
    {
    }

    public AppSettingsService(ILogger<AppSettingsService> logger, string? customFilePath = null)
    {
        _logger = logger;
        _filePath = customFilePath ?? DefaultSettingsFilePath;
    }

    public UserSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings != null)
                {
                    _logger.LogInformation("Loaded user settings from {SettingsFile}", _filePath);
                    return settings;
                }
            }
            else
            {
                _logger.LogInformation("No settings file at {SettingsFile}; using default settings", _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings from {SettingsFile}; using default settings", _filePath);
        }

        return new UserSettings();
    }

    public void SaveSettings(UserSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_filePath, json);
            _logger.LogInformation(
                "Saved user settings (General profile: {GeneralProfile}, Encoder profile: {EncoderProfile}, Source: {SourcePath}, Destination: {DestinationPath})",
                settings.GeneralProfile, settings.EncoderProfile, settings.SourcePath, settings.DestinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings to {SettingsFile}", _filePath);
        }
    }
}
