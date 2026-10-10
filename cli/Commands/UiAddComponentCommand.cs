using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddComponentCommand : Command<UiAddComponentCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Components to add, e.g. alert card data-table (see --list)")]
        [CommandArgument(0, "[component]")]
        public string[] Components { get; init; } = [];

        [Description("Add every component")]
        [CommandOption("--all")]
        [DefaultValue(false)]
        public bool All { get; init; }

        [Description("List the available components")]
        [CommandOption("--list")]
        [DefaultValue(false)]
        public bool List { get; init; }

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            if (s.List)
            {
                Ux.Result = new { components = UiComponents.All.Select(c => new { name = c.Key, category = c.Category, description = c.Description }) };
                if (Ux.Json) return 0;

                var table = new Table().Border(TableBorder.Minimal)
                    .AddColumn("[cyan]Component[/]").AddColumn("Category").AddColumn("Description");
                foreach (var c in UiComponents.All)
                    table.AddRow(c.Key, c.Category, Markup.Escape(c.Description));
                AnsiConsole.Write(table);
                return 0;
            }

            if (!s.All && s.Components.Length == 0)
                throw new ArgumentException("Name the components to add (modulus ui add-component alert card), or pass --all. See --list.");
            var selected = s.All ? UiComponents.All : [.. s.Components.Select(UiComponents.Find).DistinctBy(c => c.Key)];

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add components to.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);
            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var engineRenderer = new TemplateEngine();
            var added = new List<string>();
            var skipped = new List<string>();
            foreach (var component in selected)
            {
                var file = UiScaffold.TemplateFile(templates, engine, component.Key)
                    ?? throw new InvalidOperationException($"The package has no '{component.Key}' component for {engine}.");
                var target = UiComponents.OutputPath(engine, uiDir, component);
                if (File.Exists(target) && !Ux.Force)
                {
                    skipped.Add(component.Key);
                    continue;
                }
                engineRenderer.RenderToFile(file, new { ComponentName = component.PascalName }, target);
                added.Add(component.Key);
            }

            if (added.Count > 0)
                Ux.Success($"Added {added.Count} component{(added.Count == 1 ? "" : "s")}: {string.Join(", ", added)}");
            if (skipped.Count > 0)
                Ux.Warning($"Already present, left alone (use --force to overwrite): {string.Join(", ", skipped)}");
            return 0;
        });
    }
}
