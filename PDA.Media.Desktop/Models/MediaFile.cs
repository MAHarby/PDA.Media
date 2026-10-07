namespace PDA.Media.Desktop.Models;

public class MediaFile
{
    public int Id { get; set; } = 0;
    public string Name { get; set; } = string.Empty;    
    public string Path { get; set; } = string.Empty;   
    public string RootPathLabel { get; set; } = string.Empty;
    public MediaFileType FileType { get; set; } = MediaFileType.Other;   
}

public enum MediaFileType
{
    ALL=0,
    Folder=1,
    Music=2,
    Movie=3,
    TVShow=4,
    Photo=5,
    Other=6
}