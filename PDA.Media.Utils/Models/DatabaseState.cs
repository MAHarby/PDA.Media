namespace PDA.Media.Utils.Models;

/// <summary>Whether the media database can be reached, shown in the main window's status bar.</summary>
public enum DatabaseState
{
    /// <summary>No data layer (tests and the XAML previewer).</summary>
    NotConfigured,
    Checking,
    Online,
    Offline,
}
