using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-analytics [--no-compose]</c>: adds the analytics layer to an app. It adds the ClickHouse sink and the OpenLineage emitter packages to the
/// API host, wires them in the feature services (<c>AddModulusAnalytics</c>, <c>AddModulusOpenLineage</c>), writes the <c>Analytics</c> settings
/// (ClickHouse, OpenLineage off) and, unless <c>--no-compose</c>, <c>docker-compose.analytics.yml</c> for a local ClickHouse and RabbitMQ.
/// Existing files are never overwritten and every edit is idempotent.
/// </summary>
internal sealed class AddAnalyticsCommand : Command<AddAnalyticsCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("Do not write docker-compose.analytics.yml (the local ClickHouse and RabbitMQ).")]
        [CommandOption("--no-compose")]
        public bool NoCompose { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddAnalyticsCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var written = new List<string>();
        var hints = new List<string>();
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;

        if (!s.NoCompose)
            Write("analytics/docker_compose.analytics.yml", Path.Combine(app.SolutionDir, AnalyticsWiring.ComposeFile), written, app);

        foreach (var package in new[] { AnalyticsWiring.ClickHousePackageId, AnalyticsWiring.OpenLineagePackageId })
        {
            if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, package, FrameworkVersion.Current, Ux.DryRun))
                written.Add(Path.GetRelativePath(app.SolutionDir, app.ApiProjectPath).Replace('\\', '/') + $" ({package})");
        }

        Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), AnalyticsWiring.EnsureApiServices, written, app);
        Edit(Path.Combine(apiDir, "appsettings.json"), t => AnalyticsWiring.EnsureSettings(t, development: false), written, app);
        Edit(Path.Combine(apiDir, "appsettings.Development.json"), t => AnalyticsWiring.EnsureSettings(t, development: true), written, app);

        var servicesFile = ApiHostFiles.FeatureServicesFile(app.ProgramCsPath);
        if (!File.Exists(servicesFile) || !File.ReadAllText(servicesFile).Contains("AddModulusAnalytics(", StringComparison.Ordinal))
            hints.Add("Add builder.Services.AddModulusAnalytics(analytics => analytics.AddClickHouse(builder.Configuration)) and AddModulusOpenLineage(builder.Configuration) to Configuration/FeatureServicesSetup.cs.");
        hints.Add("Declare the datasets in the AddModulusAnalytics callback (a.AddDataset(...), a.MapEvent<TEvent>(...)). Until then nothing is delivered.");
        hints.Add("Delivery is inline until the durable queue is wired (AddModulusAnalyticsStore<TContext>, with the module's DbContext).");
        hints.Add("Production: set Analytics:ClickHouse:ConnectionString from a secret store (the file has the placeholder only).");

        AnsiConsole.MarkupLine("[green]✓[/] Analytics (ClickHouse sink, OpenLineage off)");
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        if (!s.NoCompose)
            AnsiConsole.MarkupLine("  [grey]Development: docker compose -f {0} up -d (ClickHouse on 8123, RabbitMQ on 5672).[/]", Markup.Escape(AnalyticsWiring.ComposeFile));
        return 0;
    }

    private void Write(string template, string path, List<string> written, ModuleDiscovery.AppInventory app)
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        _templates.RenderToFile(template, new { }, path);
        written.Add(display);
    }

    private static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return false;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return false;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
        return true;
    }
}
