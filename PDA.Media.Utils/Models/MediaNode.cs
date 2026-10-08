using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDA.Media.Utils.Models;

/// <summary>
/// A folder or file in the source tree. Selecting a folder cascades to its children.
/// </summary>
public partial class MediaNode : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<MediaNode>? SubNodes { get; }

    public bool IsFolder => SubNodes != null;

    /// <summary>File: its size in bytes. Folder: the total size of every file under it.</summary>
    public long Size { get; }

    /// <summary>Folder: the number of files under it, at any depth. File: 0.</summary>
    public int FileCount { get; }

    /// <summary>e.g. "- 12.5 GB", shown after a file's name.</summary>
    public string SizeText => "- " + FileSize.Format(Size);

    /// <summary>e.g. "6 files", shown beside a folder.</summary>
    public string FileCountText => FileCount == 1 ? "1 file" : $"{FileCount} files";

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

    /// <summary>A file.</summary>
    public MediaNode(string name, string fullPath, long size = 0)
    {
        Name = name;
        FullPath = fullPath;
        Size = size;
    }

    /// <summary>A folder; its file count and size are totalled from <paramref name="subNodes"/>.</summary>
    public MediaNode(string name, string fullPath, ObservableCollection<MediaNode>? subNodes)
    {
        Name = name;
        FullPath = fullPath;
        SubNodes = subNodes;
        if (subNodes != null)
        {
            FileCount = subNodes.Sum(node => node.IsFolder ? node.FileCount : 1);
            Size = subNodes.Sum(node => node.Size);
        }
    }
}
