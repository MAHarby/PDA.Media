using System.Text.Json;

namespace PDA.Media.CommandLine.Configuration;

/// <summary>
/// Finds, reads and creates pda-media's config file:
/// <list type="bullet">
/// <item>Linux / macOS: <c>$XDG_CONFIG_HOME/pda-media/config.json</c>, normally <c>~/.config/pda-media/config.json</c>.</item>
/// <item>Windows: <c>%APPDATA%\PDA.Media\pda-media.json</c> (next to the app's settings.json).</item>
/// </list>
/// <c>--config &lt;file&gt;</c> or the <c>PDA_MEDIA_CONFIG</c> environment variable use another file. The file can hold a password, so on Linux it is
/// created readable by its owner only (chmod 600).
/// </summary>
public sealed class CliSettingsStore
{
    public const string ConfigVariable = "PDA_MEDIA_CONFIG";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly Func<string, string?> _environment;

    public CliSettingsStore(Func<string, string?>? environment = null)
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>The config file to use: <paramref name="explicitPath"/> (--config), else PDA_MEDIA_CONFIG, else the default for this OS.</summary>
    public string ResolvePath(string? explicitPath) =>
        !string.IsNullOrWhiteSpace(explicitPath) ? Path.GetFullPath(explicitPath)
        : _environment(ConfigVariable) is { Length: > 0 } fromVariable ? Path.GetFullPath(fromVariable)
        : DefaultPath();

    /// <summary>The default config file for this OS (see the class summary).</summary>
    public string DefaultPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDA.Media", "pda-media.json");
        }
        string configHome = _environment("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg
            : Path.Combine(_environment("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "pda-media", "config.json");
    }

    /// <summary>The settings in <paramref name="path"/>, or the defaults when there is no such file. Throws InvalidDataException for a file that can't be read.</summary>
    public CliSettings Load(string path)
    {
        if (!File.Exists(path)) return new CliSettings();
        try
        {
            return JsonSerializer.Deserialize<CliSettings>(File.ReadAllText(path), JsonOptions) ?? new CliSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The config file {path} isn't valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Writes a starting config file (readable by its owner only on Linux / macOS).</summary>
    public void WriteTemplate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var template = new CliSettings
        {
            UserId = OperatingSystem.IsWindows() ? null : "pda_media",
            Password = OperatingSystem.IsWindows() ? null : "change-me",
            PathMappings = OperatingSystem.IsWindows() ? [] : [new PathMapping(@"\\pda-hp-z620\data", "/mnt/nas/data")],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>True when others can read the file (Linux / macOS), which matters when it holds a password.</summary>
    public static bool IsReadableByOthers(string path) =>
        !OperatingSystem.IsWindows() && File.Exists(path)
        && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0;
}
