using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Utils.Models;

/// <summary>A media category offered to the search (rows belong to it by folder: anything under its root folder).</summary>
public partial class SearchCategoryItem : ObservableObject
{
    public SearchCategoryItem(string name, MediaKind kind, string rootFolder, bool isSelected)
    {
        Name = name;
        Kind = kind;
        RootFolder = rootFolder;
        IsSelected = isSelected;
    }

    public string Name { get; }
    public MediaKind Kind { get; }
    public string RootFolder { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
