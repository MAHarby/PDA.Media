using System.Diagnostics;
using FFMpegCore;
using PDA.Media.BatchConverter;

Console.Clear();
Console.WriteLine("PDA Media Batch Converter");
Console.WriteLine("===================================================================================");

// TODO: Bring these in on the command line.
// TODO: Make default for the output folder on the NAS drive when everything is done.
string inputFolder = @"\\pda-hp-z620\data\ARR-Stack\media\movies";
string outputFolder = @"\\pda-hp-z620\data\Media\Movies";

List<string> fileList = new List<string>();
fileList.AddRange(Directory.GetFiles(inputFolder, "*.mkv", SearchOption.AllDirectories));

Stopwatch sw = new Stopwatch();
sw.Start();

foreach (string mediaFile in fileList)
{
    sw.Reset();
    
    Console.WriteLine("");
    Console.WriteLine($"Processing media file : {mediaFile} ...");
    Console.WriteLine($"Start Time : {DateTime.Now.ToShortTimeString()}");
    
    // Check the validity of the input file
    Console.Write("- Checking input file ... ");
    if (CheckMediaFile(mediaFile))
    {
        Console.WriteLine("OK");
    }
    else
    {
        Console.WriteLine("FAILED");
        return;
    }
    
    // Break out and build the file and destination components.
    Console.Write("- Building file and destination path components ... ");
    
    string fileName = Path.GetFileName(mediaFile);
    string cleanFilename = CleanupFilename(fileName);
    string destinationFolder = BuildOutputFolder(mediaFile);
    string destinationFileAndFolder = Path.Combine(destinationFolder, cleanFilename);

    Console.WriteLine("Done");
    Console.WriteLine($"  - Filename: {fileName}");
    Console.WriteLine($"  - Cleaned filename: {cleanFilename}");
    Console.WriteLine($"  - Destination folder: {destinationFolder}");
    Console.WriteLine($"  - Destination file: {destinationFileAndFolder}");
    
    // Check to see if the destination file already exists.
    Console.Write("- Checking to see if destination file already exists ... ");
    if (File.Exists(destinationFileAndFolder))
    {
        Console.WriteLine("Found It, skipping this file.");
        continue;
    }

    // We didn't find the destination file, create the folders if they don't already exist.
    Console.WriteLine("Not Found, creating folders");
    Directory.CreateDirectory(destinationFolder);
    
    // Build the FFMPEG command.
    Console.WriteLine($"- Running FFMPEG command ...");
    
    GlobalFFOptions.Configure(new FFOptions() {BinaryFolder = "./bin"});
    await MovieEncoder.EncodeBlurayMovieAsync(inputPath: mediaFile, outputPath: destinationFileAndFolder, onPercent: p => Console.Write($"\rProgress: {p:0.0}% ..."));
    
    Console.WriteLine($"Time taken: {sw.Elapsed.TotalSeconds} minutes");
}

bool CheckMediaFile(string filename)
{
    // Need to check the file to make sure it is a valid media file
    return true;
}
string CleanupFilename(string filename)
{
    // The desired filename is {moviename} ({year}).mkv eg 'Disclosure Day (2026)'.
    // The source filename could be something like 'Disclosure Day (2026) Remux-1080p.mkv'.
    return MKVutils.CleanFilename(filename);
}

string BuildOutputFolder(string mediaFilename)
{
    // string? p1 = Path.GetFullPath(mediaFilename);
    // string? p2 = Path.GetDirectoryName(mediaFilename);
    // string? p3 = Path.GetPathRoot(mediaFilename);
    // string? p4 = Path.GetFileName(mediaFilename);
    // string? p5 = Path.GetFileName(Path.GetDirectoryName(mediaFilename));
    string? parentFolder = new FileInfo(mediaFilename).Directory?.Name;
    // string? p7 = mediaFilename.Replace(Path.GetFileName(mediaFilename), "");
    // string returnPath = fileName.Replace(Path.GetFileName(filename), "");

    return Path.Combine(outputFolder, parentFolder!);
}

