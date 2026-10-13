namespace PDA.Media.Utils.Models;

/// <summary>A question the media scanner window asks before going on (shown over the window, with a confirm and a back button).</summary>
public sealed record ScanConfirmation(string Title, string Message, string ConfirmText, string BackText = "Back");
