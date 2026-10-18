using PDA.Media.CommandLine.Configuration;
using Spectre.Console;

namespace PDA.Media.CommandLine;

/// <summary>Where pda-media writes (results to Out, problems to Error) and where it finds its config file; tests replace these.</summary>
internal sealed class CliContext
{
    public required IAnsiConsole Out { get; init; }
    public required IAnsiConsole Error { get; init; }

    /// <summary>The writers System.CommandLine uses for --help and parse errors.</summary>
    public required TextWriter OutWriter { get; init; }

    public required TextWriter ErrorWriter { get; init; }
    public CliSettingsStore Store { get; init; } = new();

    public static CliContext ForConsole() => new()
    {
        Out = WithWidth(AnsiConsole.Console),
        Error = WithWidth(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) })),
        OutWriter = Console.Out,
        ErrorWriter = Console.Error,
    };

    /// <summary>
    /// Piped or redirected output has no window width (Spectre.Console would cut every line down to nothing): use $COLUMNS, else 200 characters.
    /// </summary>
    private static IAnsiConsole WithWidth(IAnsiConsole console)
    {
        if (console.Profile.Width < 40)
        {
            console.Profile.Width = int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out int columns) && columns >= 40 ? columns : 200;
        }
        return console;
    }
}
