using System;
using System.IO;
using System.Text.Json;

namespace PDA.Media.Utils.Services;

public class UserSettings
{
    public string GeneralProfile { get; set; } = "General";
    public string EncoderProfile { get; set; } = "Bluray TV";
    public string SourcePath { get; set; } = @"\\pda-hp-z620\data\ARR-Stack\media\tv";
    public string DestinationPath { get; set; } = @"\\Aubrey-NAS\Media\TV Series\ARRstack";
}

public class AppSettingsService
{
    public static readonly string DefaultSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDA.Media");
    public static readonly string DefaultSettingsFilePath = Path.Combine(DefaultSettingsDirectory, "settings.json");

    private readonly string _filePath;
    public AppSettingsService(string? customFilePath = null)
    {
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
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings from '{_filePath}': {ex.Message}");
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings to '{_filePath}': {ex.Message}");
        }
    }
}
