namespace PDA.Media.Utils.Models;

/// <summary>A row of a lookup table (album, movie or TV show type) for a drop-down.</summary>
public sealed record LookupOption(int Id, string Name)
{
    public override string ToString() => Name;
}
