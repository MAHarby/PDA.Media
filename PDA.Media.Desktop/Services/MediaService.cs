using System.IO;
using PDA.Media.Desktop.Models;

namespace PDA.Media.Desktop.Services;

public class MediaService
{
    private readonly string _rootPath = @"\\Aubrey-NAS\Media";
    private readonly string[] _rootFolders = ["Movies", "Music", "TV Series", "RIP"];
    private readonly string[] _movieExtraSuffixes = ["-behindthescenes","-deleted","-featurette","-interview","-scene","-short","-trailer","-other"];
    private readonly string[] _tvShowExtraSuffixes = ["-Behind The Scenes","-Deleted Scenes","-Featurettes","-Interviews","-Scenes","-Shorts","-Trailers","-Other"];

    private readonly MediaFileType _mediaFileType = MediaFileType.ALL;
    private readonly bool _removeExtras = false;

    public MediaService(string? rootPath)
    {
        if (!string.IsNullOrEmpty(rootPath)) _rootPath = rootPath;
        _mediaFileType = MediaFileType.ALL;
        _removeExtras = false;
    }
    public MediaService(string? rootPath, MediaFileType mediaFileType, bool removeExtras)
    {
        if (!string.IsNullOrEmpty(rootPath)) _rootPath = rootPath;
        _mediaFileType = mediaFileType;
        _removeExtras = removeExtras;
    }
    public MediaService(string? rootPath, string rootFolder, bool removeExtras)
    {
        if (!string.IsNullOrEmpty(rootPath)) _rootPath = rootPath;
        _mediaFileType = MediaFileType.ALL;
        _rootFolders = [rootFolder];
        _removeExtras = removeExtras;
    }

    public string RootPath => _rootPath;
    public string[] RootFolders => _rootFolders;
    
    public async Task<List<MediaFile>> GetRawMediaFilesAsync()
    {
        int id = 0;
        List<MediaFile> mediaFiles = new List<MediaFile>();
        foreach (string rootFolder in _rootFolders)
        {
            string rootFolderPath = Path.Combine(_rootPath, rootFolder);
            if (Path.Exists(rootFolderPath))
            {
                // Log the top-level folder.
                Console.WriteLine($"Processing folder: {rootFolderPath}");

                id++;
                mediaFiles.Add(new MediaFile() { Id = id, Name = rootFolder, Path = rootFolderPath, RootPathLabel = rootFolder, FileType = MediaFileType.Folder });
                
                // Enumerate all sub-folders off the root.
                var subFolders = Directory.EnumerateDirectories(rootFolderPath, "*", SearchOption.AllDirectories);
                foreach (string subFolder in subFolders)
                {
                    id++;
                    mediaFiles.Add(new MediaFile() { Id = id, Name = Path.GetFileNameWithoutExtension(subFolder), Path = subFolder, RootPathLabel = rootFolder, FileType = MediaFileType.Folder });
                    
                    // Now enumerate the sub-folders to find the media files.
                    var mediaFilesInSubFolder = Directory.EnumerateFiles(subFolder, "*", SearchOption.AllDirectories);
                    foreach (string mediaFile in mediaFilesInSubFolder)
                    {
                        id++;
                        MediaFile mediaFileToAdd = new MediaFile()
                        {
                            Id = id,
                            Name = Path.GetFileNameWithoutExtension(mediaFile),
                            Path = mediaFile,
                            RootPathLabel = rootFolder,
                            FileType = CalculateMediaFileType(Path.GetExtension(mediaFile))
                        };
                        mediaFiles.Add(mediaFileToAdd);
                    }
                }
            }
            else
            {
                Console.WriteLine($"Folder not found: {rootFolderPath}");
            }
        }
        return mediaFiles;
    }

    private MediaFileType CalculateMediaFileType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaFileType.Other;
        }

        string ext = extension.Trim().TrimStart('.').ToLowerInvariant();

        return ext switch
        {
            // Music / Audio
            "mp3" or "wav" or "wma" or "aac" or "flac" or "m4a" or "ogg" or "oga" or "opus"
                or "aiff" or "aif" or "aifc" or "alac" or "ape" or "mid" or "midi" or "amr"
                or "ac3" or "dts" or "mka" or "ra" or "voc" or "au" or "pcm" or "caf" => MediaFileType.Music,

            // Video / Movies
            "mp4" or "m4v" or "mkv" or "avi" or "wmv" or "mov" or "flv" or "webm" or "vob"
                or "ogv" or "mpeg" or "mpg" or "m2v" or "m4p" or "3gp" or "3g2" or "f4v"
                or "f4p" or "f4a" or "f4b" or "rm" or "rmvb" or "asf" or "ts" or "m2ts"
                or "mts" or "divx" or "xvid" or "hevc" or "h264" or "h265" => MediaFileType.Movie,

            // Photo / Image
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "tiff" or "tif" or "svg"
                or "ico" or "psd" or "raw" or "cr2" or "nef" or "arw" or "dng" or "heic"
                or "heif" or "avif" or "jfif" or "pjpeg" or "pjp" => MediaFileType.Photo,

            _ => MediaFileType.Other
        };
    }
}