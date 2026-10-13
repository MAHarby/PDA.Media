using System.Text.RegularExpressions;
using PDA.Media.Data.Entities;

namespace PDA.Media.Utils.Services;

/// <summary>
/// Suggests a tidy name for a media row (pure; used by Rename's Tidy button and the batch tidy):
/// movies <c>Title (Year)</c> and shows <c>Show (Year)</c> by <see cref="PlexNaming"/>, episode titles without release tags, and for music (which has no
/// Plex naming) only light cleanup: technical tags such as <c>[FLAC]</c>, <c>[24-96]</c> or <c>(Remastered 2011)</c> removed and spaces tidied.
/// Every suggestion is safe as a Windows file or folder name.
/// </summary>
public static partial class MediaNameTidy
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // [anything] or {anything}: in music folder names these are technical tags ("[FLAC]", "[24-96]", "{Web}").
    [GeneratedRegex(@"\s*(?:\[[^\]]*\]|\{[^}]*\})", Options)]
    private static partial Regex BracketedTags { get; }

    // (...) holding a technical word: "(Remastered 2011)", "(2011 Remaster)", "(FLAC)", "(24bit-96kHz)", "(Vinyl Rip)".
    [GeneratedRegex(@"\s*\((?=[^)]*\b(?:remaster(?:ed)?|flac|mp3|aac|alac|wav|\d{2}\s?-?bit|\d{2}(?:[.,]\d)?\s?khz|\d{2}-\d{2,3}|vinyl\s?rip|cd\s?rip|web)\b)[^)]*\)", Options)]
    private static partial Regex TechnicalParentheses { get; }

    [GeneratedRegex(@"\s+", Options)]
    private static partial Regex Whitespace { get; }

    /// <summary>The tidy name for <paramref name="entity"/>, or its current name when there is nothing to tidy (or no rule for it).</summary>
    public static string Suggest(IEntity entity) => entity switch
    {
        Movie m => Fallback(PlexNaming.SafeName(PlexNaming.CleanMovieName(m.Name, keepDots: m.Name.Contains(' '))), m.Name),
        TVShow s => Fallback(PlexNaming.SafeName(PlexNaming.CleanMovieName(CurrentName(s), keepDots: CurrentName(s).Contains(' '))), CurrentName(s)),
        TVShowEpisode e => Fallback(PlexNaming.SafeName(PlexNaming.CleanEpisodeTitle(e.Name)), e.Name),
        Artist or Album or Track => TidyMusic(entity.Name),
        _ => entity.Name,
    };

    /// <summary>The name a rename starts from: the folder-style name (<c>Show (Year)</c> for a show), otherwise the row's name.</summary>
    public static string CurrentName(IEntity entity) => entity is TVShow { ReleaseYear: > 0 } s ? $"{s.Name} ({s.ReleaseYear})" : entity.Name;

    /// <summary>Music cleanup: technical tags removed, spaces tidied, characters Windows doesn't allow removed.</summary>
    public static string TidyMusic(string name)
    {
        string tidy = TechnicalParentheses.Replace(BracketedTags.Replace(name, ""), "");
        tidy = Whitespace.Replace(tidy, " ").Trim(' ', '-', '_');
        return Fallback(PlexNaming.SafeName(tidy), name);
    }

    private static string Fallback(string suggestion, string current) => suggestion.Length > 0 ? suggestion : current;
}
