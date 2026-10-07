using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
    
namespace PDA.Media.BatchConverter
{
    public static class MovieEncoder
    {
        public static async Task EncodeBlurayMovieAsync(string inputPath, string outputPath, int crf=22, string preset="fast", Action<double>? onPercent = null, CancellationToken cancellationToken = default)
        {
            // Look inside of the media file.
            IMediaAnalysis info = await FFProbe.AnalyseAsync(inputPath, cancellationToken: cancellationToken);
            
            // Encode to a temporary filename so we can easily clean-up if something goes wrong.
            string tempOutputPath = Path.ChangeExtension(outputPath, ".partial.mkv");
            // Directory.CreateDirectory(Path.GetFullPath(outputPath));
            
            var job = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(tempOutputPath, overwrite: true, o => o
                    .WithCustomArgument("-map 0")                  // keep every stream from the source
                    .WithCustomArgument("-c copy")                 // copy them all untouched...
                    .WithCustomArgument("-c:v:0 libx265")          // ...except the main video, which becomes HEVC
                    .WithCustomArgument($"-preset {preset}")
                    .WithCustomArgument($"-crf {crf}")
                    .WithCustomArgument("-pix_fmt yuv420p10le"))   // 10-bit output: less banding, slightly smaller
                .NotifyOnProgress(p => onPercent?.Invoke(p), info.Duration)
                .CancellableThrough(cancellationToken);
            
            Console.WriteLine($"    - FFMPEG {job.Arguments}");
            
            await job.ProcessAsynchronously();
            File.Move(tempOutputPath, outputPath, true);
        }
    }
}