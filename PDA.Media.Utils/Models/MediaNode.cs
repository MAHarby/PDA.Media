using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.Models;

public partial class MediaNode : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<MediaNode>? SubNodes { get; }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                // Cascade selection to child nodes
                if (SubNodes != null)
                {
                    foreach (var childNode in SubNodes)
                    {
                        childNode.Selected = value;
                    }
                }
            }
        }
    }
    
    public MediaNode(string name, string fullPath) { Name = name; FullPath = fullPath; }
    public MediaNode(string name, string fullPath, ObservableCollection<MediaNode>? subNodes) { Name = name; FullPath = fullPath; SubNodes = subNodes; }
}
