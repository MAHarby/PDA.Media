using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.Models;

/// <summary>
/// A group of the Search tab's categories ("Music", "Video"), with one tick box for all of them: ticked when they all are, unticked when none are,
/// and in between otherwise. Ticking it ticks every category in the group (also from in between); unticking it unticks them all.
/// </summary>
public partial class SearchCategoryGroup : ObservableObject
{
    public SearchCategoryGroup(string name, IReadOnlyList<SearchCategoryItem> categories)
    {
        Name = name;
        Categories = categories;
        foreach (SearchCategoryItem category in categories) category.PropertyChanged += Category_PropertyChanged;
    }

    public string Name { get; }
    public IReadOnlyList<SearchCategoryItem> Categories { get; }

    public bool? IsSelected
    {
        get => Categories.All(c => c.IsSelected) ? true : Categories.Any(c => c.IsSelected) ? null : false;
        set
        {
            // A two-state tick box clicked while in between asks for false: tick everything then, as clicking a group selects its contents.
            bool select = value ?? true;
            if (value == false && IsSelected == null) select = true;
            foreach (SearchCategoryItem category in Categories) category.IsSelected = select;
            OnPropertyChanged();
        }
    }

    private void Category_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchCategoryItem.IsSelected)) OnPropertyChanged(nameof(IsSelected));
    }
}
