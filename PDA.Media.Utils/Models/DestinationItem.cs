using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.Models;

public partial class DestinationItem : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool Selected { get; set; } = true;

    public DestinationItem(string name, string fullPath) { Name = name; FullPath = fullPath; }
}
