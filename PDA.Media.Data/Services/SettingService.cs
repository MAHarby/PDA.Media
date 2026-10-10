using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

/// <summary>
/// Reads and writes the key / value rows in the Settings table (shared by everything that uses the database, unlike
/// the app's per-user settings.json). Keys match ignoring case (database collation). Each method creates its own
/// short-lived <see cref="DataContext"/>.
/// </summary>
public class SettingService
{
    private readonly IDbContextFactory<DataContext> _contextFactory;
    private readonly ILogger<SettingService> _logger;

    public SettingService(string connectionString) : this(new DataContextFactory(connectionString)) { }
    public SettingService(IDbContextFactory<DataContext> contextFactory, ILogger<SettingService>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<SettingService>.Instance;
    }

    /// <summary>The value for <paramref name="key"/>, or null if there is no such setting.</summary>
    public string? GetValue(string key)
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every setting, by key.</summary>
    public Dictionary<string, string> GetAllValues()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Settings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, string>> GetAllValuesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Settings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds the setting, or changes its value if it already exists.</summary>
    public void SetValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        using var context = _contextFactory.CreateDbContext();

        Setting? setting = context.Settings.FirstOrDefault(s => s.Key == key);
        if (setting == null) context.Settings.Add(new Setting(key, value));
        else setting.Value = value;
        context.SaveChanges();

        _logger.LogInformation("Saved setting {Key} = {Value}", key, value);
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Setting? setting = await context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (setting == null) context.Settings.Add(new Setting(key, value));
        else setting.Value = value;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved setting {Key} = {Value}", key, value);
    }

    /// <summary>Removes the setting. Returns false if there was no such setting.</summary>
    public bool DeleteValue(string key)
    {
        using var context = _contextFactory.CreateDbContext();
        bool deleted = context.Settings.Where(s => s.Key == key).ExecuteDelete() > 0;
        if (deleted) _logger.LogInformation("Deleted setting {Key}", key);
        return deleted;
    }

    public async Task<bool> DeleteValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool deleted = await context.Settings.Where(s => s.Key == key).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (deleted) _logger.LogInformation("Deleted setting {Key}", key);
        return deleted;
    }
}
