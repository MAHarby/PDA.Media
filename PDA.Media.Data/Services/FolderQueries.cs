using System.Linq.Expressions;
using System.Reflection;

namespace PDA.Media.Data.Services;

/// <summary>Query helpers for the folder paths stored with media rows.</summary>
internal static class FolderQueries
{
    private static readonly MethodInfo StartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    /// <summary>
    /// <c>e => e.Folder == root || e.Folder.StartsWith(root + separator) || ...</c> for each root: the row is in one of those folders or under it.
    /// The roots are sent as query parameters.
    /// </summary>
    public static Expression<Func<T, bool>> UnderAny<T>(string folderProperty, IReadOnlyList<string> roots)
    {
        ParameterExpression e = Expression.Parameter(typeof(T), "e");
        MemberExpression folder = Expression.Property(e, folderProperty);
        Expression body = Expression.Constant(false);
        foreach (string root in roots)
        {
            string trimmed = root.TrimEnd('\\', '/');
            Expression equals = Expression.Equal(folder, AsParameter(trimmed));
            Expression startsWith = Expression.Call(folder, StartsWithMethod, AsParameter(trimmed + Path.DirectorySeparatorChar));
            body = Expression.OrElse(body, Expression.OrElse(equals, startsWith));
        }
        return Expression.Lambda<Func<T, bool>>(body, e);
    }

    /// <summary>
    /// <paramref name="folder"/> with the start moved from <paramref name="from"/> to <paramref name="to"/> when it is that folder or under it
    /// (ignoring case); null when it isn't.
    /// </summary>
    public static string? Move(string? folder, string from, string to)
    {
        if (folder == null) return null;
        from = from.TrimEnd('\\', '/');
        to = to.TrimEnd('\\', '/');
        if (folder.Equals(from, StringComparison.OrdinalIgnoreCase)) return to;
        foreach (char separator in new[] { '\\', '/' })
        {
            if (folder.StartsWith(from + separator, StringComparison.OrdinalIgnoreCase)) return to + folder[from.Length..];
        }
        return null;
    }

    /// <summary>An expression for <paramref name="value"/> that EF sends as a query parameter rather than a literal.</summary>
    public static Expression AsParameter(string value) => Expression.Property(Expression.Constant(new Box(value)), nameof(Box.Value));

    /// <summary>Holds a value so EF sends it as a query parameter rather than a literal.</summary>
    private sealed class Box(string value)
    {
        public string Value { get; } = value;
    }
}
