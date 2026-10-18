using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using PDA.Media.CommandLine.Configuration;
using PDA.Media.Data.Contexts;
using Spectre.Console;

namespace PDA.Media.CommandLine.Commands;

/// <summary><c>pda-media config</c>: shows the config file in use and its settings (never the password); <c>--init</c> creates it; <c>--test</c> connects.</summary>
internal static class ConfigCommand
{
    public static Command Create(CliContext context, Option<string?> configOption)
    {
        var init = new Option<bool>("--init") { Description = "Create the config file (if there isn't one) to fill in" };
        var test = new Option<bool>("--test") { Description = "Try to connect to the database" };
        var command = new Command("config", "Show pda-media's config file and settings, create it (--init) or test the database connection (--test).") { init, test };
        command.SetAction((parse, cancellationToken) => RunAsync(context, parse.GetValue(configOption), parse.GetValue(init), parse.GetValue(test), cancellationToken));
        return command;
    }

    public static async Task<int> RunAsync(CliContext context, string? configPath, bool init, bool test, CancellationToken cancellationToken)
    {
        string path = context.Store.ResolvePath(configPath);
        if (init)
        {
            if (File.Exists(path))
            {
                context.Error.MarkupLine($"[yellow]{Markup.Escape(path)} already exists; it was left as it is.[/]");
                return PdaMediaCli.Failed;
            }
            context.Store.WriteTemplate(path);
            context.Out.MarkupLine($"Created [bold]{Markup.Escape(path)}[/]: fill in the database login and path mappings, then run [bold]pda-media config --test[/].");
            return PdaMediaCli.Found;
        }

        CliSettings settings;
        try
        {
            settings = context.Store.Load(path);
        }
        catch (InvalidDataException ex)
        {
            context.Error.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return PdaMediaCli.Failed;
        }

        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn();
        grid.AddRow("[grey]Config file[/]", Markup.Escape(path) + (File.Exists(path) ? "" : " [yellow](not created yet: pda-media config --init)[/]"));
        grid.AddRow("[grey]Database[/]", Markup.Escape(settings.Describe()));
        grid.AddRow("[grey]Path mappings[/]", settings.PathMappings.Count == 0 ? "none (paths are shown as stored)"
            : string.Join("\n", settings.PathMappings.Select(m => $"{Markup.Escape(m.From)} → {Markup.Escape(m.To)}")));
        context.Out.Write(grid);
        if (CliSettingsStore.IsReadableByOthers(path) && !string.IsNullOrEmpty(settings.Password))
        {
            context.Error.MarkupLine($"[yellow]The config file holds a password and others can read it: chmod 600 {Markup.Escape(path)}[/]");
        }

        if (!test) return PdaMediaCli.Found;
        try
        {
            await using DataContext db = new DataContextFactory(settings.BuildConnectionString()).CreateDbContext();
            bool connected = await db.Database.CanConnectAsync(cancellationToken);
            if (connected)
            {
                context.Out.MarkupLine($"[green]Connected to {Markup.Escape(settings.Describe())}[/]");
                return PdaMediaCli.Found;
            }
            context.Error.MarkupLine($"[red]Couldn't connect to {Markup.Escape(settings.Describe())}[/]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Error.MarkupLine($"[red]Couldn't connect to {Markup.Escape(settings.Describe())}: {Markup.Escape(ex.GetBaseException().Message)}[/]");
        }
        return PdaMediaCli.Failed;
    }
}
