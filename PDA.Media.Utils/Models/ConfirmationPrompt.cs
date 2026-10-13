namespace PDA.Media.Utils.Models;

/// <summary>A question a window asks before going on (shown over it, with a confirm and a back button).</summary>
public sealed record ConfirmationPrompt(string Title, string Message, string ConfirmText, string BackText = "Back");
