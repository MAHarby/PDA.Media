using System;
using System.IO;
using PDA.Media.Utils.Services;

namespace PDA.Media.Tests;

/// <summary>
/// Settings and profile services backed by files in a temporary folder, so tests never touch the
/// real %APPDATA%/PDA.Media files. Deleted on dispose.
/// </summary>
internal sealed class TempServices : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pda_vm_test_" + Guid.NewGuid().ToString("N"));

    public string SettingsFile => Path.Combine(_root, "settings.json");
    public string SourceFolder => Path.Combine(_root, "source");
    public AppSettingsService SettingsService { get; }
    public EncoderProfileService ProfileService { get; }

    public TempServices()
    {
        Directory.CreateDirectory(_root);
        SettingsService = new AppSettingsService(SettingsFile);
        ProfileService = new EncoderProfileService(Path.Combine(_root, "profiles.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
