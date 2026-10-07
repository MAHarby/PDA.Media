using System.IO;
using System.Text.RegularExpressions;

namespace PDA.Media.BatchConverter
{
    public static partial class MKVutils
    {
        private const RegexOptions regexOptions = RegexOptions.CultureInvariant; // | RegexOptions.IgnoreCase | RegexOptions.Compiled;
        
        // Build [bracketed] tags.
        // [bracketed] tags
        [GeneratedRegex(@"\[[^\]]*\]", regexOptions)]
        private static partial Regex BracketedTags { get; }

        // Runs of dots or underscores.
        [GeneratedRegex(@"[._]+", regexOptions)] 
        private static partial Regex DotsAndUnderscores { get; }
        
        // first release tag (as a whole word) through to the end of the name
        [GeneratedRegex(@"[ (\-]+(?:2160p|1080[pi]|720p|576p|480p|4k|uhd|blu-?ray|b[dr]rip|web-?dl|webrip|hdtv|dvdrip|remux|x26[45]|h ?26[45]|hevc|avc|xvid|aac|ac3|dts|ddp?5 1|atmos|10bit|hdr|proper|repack|extended|unrated)(?:[ )\-].*)?$", regexOptions | RegexOptions.IgnoreCase)]
        private static partial Regex ReleaseTagToEnd { get; }

        [GeneratedRegex(@"\s+", regexOptions)]
        private static partial Regex Whitespace { get; }

        // trailing year, with or without brackets
        [GeneratedRegex(@" \(?((?:19|20)[0-9]{2})\)?$", regexOptions)]
        private static partial Regex TrailingYear { get; }

        /// <summary>
        /// Returns the cleaned name without an extension, e.g.
        /// "The_Matrix_1999_2160p_UHD_HEVC.MKV" -> "The Matrix (1999)".
        /// Can return an empty string if the whole name was tags.
        /// </summary>
        public static string CleanFilename(string fileName)
        {
            var extension = Path.GetExtension(fileName);        // save original extension
            var name = Path.GetFileNameWithoutExtension(fileName);    // drop the extension
            
            name = BracketedTags.Replace(name, "");                   // drop [bracketed] tags
            name = DotsAndUnderscores.Replace(name, " ");             // dots/underscores -> spaces
            name = ReleaseTagToEnd.Replace(name, "");                 // cut from first release tag
            name = Whitespace.Replace(name, " ").Trim(' ', '-');      // collapse and trim
            name = TrailingYear.Replace(name, " ($1)");               // trailing year -> (year)

            return $"{name}{extension}";
            // return Path.Combine(name, extension);
        }
        
    }
}