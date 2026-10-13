using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Entities;

namespace PDA.Media.Utils.Models;

/// <summary>
/// The fields of a media row that can be edited by hand (see MediaSearchService.CopyEditableFields): Description, Notes and Favourite for every row,
/// plus the MusicBrainz id (artist, album, track), the type (album, movie, TV show), the length (album, track) and the release date (episode).
/// It keeps the stored values to tell whether anything has changed.
/// </summary>
public partial class MediaItemEditor : ObservableObject
{
    private readonly IEntity _original;

    public MediaItemEditor(IEntity entity, IReadOnlyList<LookupOption> typeOptions)
    {
        _original = entity;
        TypeOptions = typeOptions;
        (string? description, string? notes, string? musicBrainzId, int? typeId, int? length, DateTime? releaseDate) = Read(entity);
        Description = description;
        Notes = notes;
        IsFavourite = MediaEntityInfo.IsFavourite(entity);
        MusicBrainzId = musicBrainzId;
        SelectedType = typeOptions.FirstOrDefault(o => o.Id == typeId);
        Length = length;
        ReleaseDate = releaseDate;
    }

    public IEntity Entity => _original;

    public bool HasMusicBrainzId => _original is Artist or Album or Track;
    public bool HasType => _original is Album or Movie or TVShow;
    public bool HasLength => _original is Album or Track;
    public bool HasReleaseDate => _original is TVShowEpisode;

    public string TypeLabel => _original switch { Album => "Album type", Movie => "Movie type", _ => "Show type" };
    public IReadOnlyList<LookupOption> TypeOptions { get; }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial string? Description { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial string? Notes { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial bool IsFavourite { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial string? MusicBrainzId { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial LookupOption? SelectedType { get; set; }

    /// <summary>Length as stored (a NumericUpDown value, so decimal).</summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial decimal? Length { get; set; }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasChanges))] public partial DateTime? ReleaseDate { get; set; }

    /// <summary>True when a field differs from the stored row (empty text counts as no text).</summary>
    public bool HasChanges
    {
        get
        {
            (string? description, string? notes, string? musicBrainzId, int? typeId, int? length, DateTime? releaseDate) = Read(_original);
            return Text(Description) != Text(description) || Text(Notes) != Text(notes) || IsFavourite != MediaEntityInfo.IsFavourite(_original)
                || (HasMusicBrainzId && Text(MusicBrainzId) != Text(musicBrainzId))
                || (HasType && SelectedType != null && SelectedType.Id != typeId)
                || (HasLength && (int)(Length ?? 0) != (length ?? 0))
                || (HasReleaseDate && ReleaseDate?.Date != releaseDate?.Date);
        }
    }

    /// <summary>Writes the edited fields onto <paramref name="target"/> (a row of the same type).</summary>
    public void ApplyTo(IEntity target)
    {
        string? description = Text(Description), notes = Text(Notes), musicBrainzId = Text(MusicBrainzId);
        int length = (int)(Length ?? 0);
        switch (target)
        {
            case Artist a:
                (a.Description, a.Notes, a.IsFavourite, a.MusicBrainzId) = (description, notes, IsFavourite, musicBrainzId);
                break;
            case Album a:
                (a.Description, a.Notes, a.IsFavourite, a.MusicBrainzId, a.Length) = (description, notes, IsFavourite, musicBrainzId, length);
                a.AlbumTypeId = SelectedType?.Id ?? ((Album)_original).AlbumTypeId;
                break;
            case Track t:
                (t.Description, t.Notes, t.IsFavourite, t.MusicBrainzId, t.Length) = (description, notes, IsFavourite, musicBrainzId, length);
                break;
            case Movie m:
                (m.Description, m.Notes, m.IsFavourite) = (description, notes, IsFavourite);
                m.MovieTypeId = SelectedType?.Id ?? ((Movie)_original).MovieTypeId;
                break;
            case TVShow s:
                (s.Description, s.Notes, s.IsFavourite) = (description, notes, IsFavourite);
                s.TVShowTypeId = SelectedType?.Id ?? ((TVShow)_original).TVShowTypeId;
                break;
            case TVShowEpisode e:
                (e.Description, e.Notes, e.IsFavourite, e.ReleaseDate) = (description, notes, IsFavourite, ReleaseDate?.Date);
                break;
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static (string? Description, string? Notes, string? MusicBrainzId, int? TypeId, int? Length, DateTime? ReleaseDate) Read(IEntity entity) => entity switch
    {
        Artist a => (a.Description, a.Notes, a.MusicBrainzId, null, null, null),
        Album a => (a.Description, a.Notes, a.MusicBrainzId, a.AlbumTypeId, a.Length, null),
        Track t => (t.Description, t.Notes, t.MusicBrainzId, null, t.Length, null),
        Movie m => (m.Description, m.Notes, null, m.MovieTypeId, null, null),
        TVShow s => (s.Description, s.Notes, null, s.TVShowTypeId, null, null),
        TVShowEpisode e => (e.Description, e.Notes, null, null, null, e.ReleaseDate),
        _ => (null, null, null, null, null, null),
    };
}
