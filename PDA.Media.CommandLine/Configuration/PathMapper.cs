namespace PDA.Media.CommandLine.Configuration;

/// <summary>
/// Shows the database's paths (Windows / NAS paths such as <c>\\pda-hp-z620\data\Movies\…</c>) as they are on this computer, using the config file's
/// path mappings: the longest matching <see cref="PathMapping.From"/> (ignoring case and which slashes are used) is replaced by its
/// <see cref="PathMapping.To"/>, and when that is a Linux path the rest of the backslashes become slashes. A path no mapping matches is shown as stored.
/// </summary>
public sealed class PathMapper(IEnumerable<PathMapping> mappings)
{
    private readonly List<PathMapping> _mappings = mappings.Where(m => !string.IsNullOrEmpty(m.From)).OrderByDescending(m => m.From.Length).ToList();

    public string? Map(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string normalised = path.Replace('/', '\\');
        foreach (PathMapping mapping in _mappings)
        {
            string from = mapping.From.Replace('/', '\\').TrimEnd('\\');
            bool isFolder = normalised.Equals(from, StringComparison.OrdinalIgnoreCase);
            if (!isFolder && !normalised.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase)) continue;

            string rest = normalised[from.Length..];
            string to = mapping.To.TrimEnd('\\', '/');
            return to.Contains('/') ? to + rest.Replace('\\', '/') : to + rest;
        }
        return path;
    }
}
