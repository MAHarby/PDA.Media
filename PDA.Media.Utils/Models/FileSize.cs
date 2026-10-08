namespace PDA.Media.Utils.Models;

public static class FileSize
{
    /// <summary>One decimal place for GB, whole numbers below that: "12.5 GB", "850 MB", "12 KB".</summary>
    public static string Format(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
        return bytes switch
        {
            >= (long)gb => $"{bytes / gb:0.0} GB",
            >= (long)mb => $"{bytes / mb:0} MB",
            >= (long)kb => $"{bytes / kb:0} KB",
            _ => $"{bytes} B"
        };
    }
}
