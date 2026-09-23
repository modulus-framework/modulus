using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiSearchCommand : Command<UiSearchCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Search term (name, feature, package, or namespace)")]
        [CommandArgument(0, "<query>")]
        public string Query { get; init; } = "";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(s.Query))
            {
                Ux.Error("Search query is required. Usage: modulus ui search <term>");
                return 1;
            }

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            var results = UiModuleCatalog.Search(s.Query);

            if (results.Count == 0)
            {
                Ux.Warning($"No UI modules found matching '{s.Query}'.");
                Ux.Info("Run 'modulus ui list' to see all available modules.");
                return 0;
            }

            var installed = GetInstalledUiModules(inventory);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[cyan]Search results for '{Markup.Escape(s.Query)}'[/]")
                .AddColumn("[cyan]ID[/]")
                .AddColumn("[grey]Package[/]")
                .AddColumn("[grey]Version[/]")
                .AddColumn("[grey]Features[/]")
                .AddColumn("[grey]Status[/]");

            foreach (var m in results.OrderBy(m => m.Id))
            {
                var features = m.Features.Count == 0
                    ? "[grey dim]—[/]"
                    : string.Join(", ", m.Features);
                var status = installed.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase)
                    ? "[green]installed[/]"
                    : "[grey dim]not installed[/]";

                table.AddRow(
                    $"[cyan]{m.Id}[/]",
                    $"[grey]{m.PackageId}[/]",
                    $"[grey]{m.Version}[/]",
                    features,
                    status);
            }

            AnsiConsole.Write(table);
            return 0;
        });
    }

    private static HashSet<string> GetInstalledUiModules(ModuleDiscovery.AppInventory inventory)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uiProject = inventory.UiProjectPath;
        if (uiProject.Length > 0 && File.Exists(uiProject))
        {
            var refs = ProjectFileService.ParseCsprojPackageReferences(uiProject);
            foreach (var kvp in refs)
            {
                if (kvp.Key.StartsWith("Cobytelabs.Modulus.UI.", StringComparison.OrdinalIgnoreCase))
                    installed.Add(kvp.Key);
            }
        }
        return installed;
    }
}
