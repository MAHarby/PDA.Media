using System.CommandLine;
using PDA.Media.CommandLine.Commands;

namespace PDA.Media.CommandLine;

/// <summary>
/// The pda-media command: <c>pda-media search &lt;text&gt; [options]</c> and <c>pda-media config [--init | --test]</c>, parsed by System.CommandLine
/// (<c>--help</c> on any command lists its options). Exit codes: 0 found / done, 1 nothing found, 2 an error.
/// </summary>
internal static class PdaMediaCli
{
    public const int Found = 0;
    public const int NothingFound = 1;
    public const int Failed = 2;

    public static async Task<int> RunAsync(string[] args, CliContext context, CancellationToken cancellationToken = default)
    {
        var configOption = new Option<string?>("--config")
        {
            Description = "The config file to use (default: ~/.config/pda-media/config.json on Linux, %APPDATA%\\PDA.Media\\pda-media.json on Windows)",
            Recursive = true,
        };
        var root = new RootCommand("pda-media: search the PDA.Media library (the media database the PDA.Media app scans into).");
        root.Options.Add(configOption);
        root.Subcommands.Add(SearchCommand.Create(context, configOption));
        root.Subcommands.Add(ConfigCommand.Create(context, configOption));

        ParseResult parsed = root.Parse(args);
        return await parsed.InvokeAsync(new InvocationConfiguration { Output = context.OutWriter, Error = context.ErrorWriter }, cancellationToken);
    }
}
