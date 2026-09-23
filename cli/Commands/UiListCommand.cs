using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiListCommand : Command<UiListCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Show only UI modules already installed in this app")]
        [CommandOption("--installed")]
        [DefaultValue(false)]
        public bool InstalledOnly { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            var catalog = UiModuleCatalog.All;

            if (s.InstalledOnly)
            {
                var installed = GetInstalledUiModules(inventory);
                if (installed.Count == 0)
                {
                    Ux.Info("No UI modules installed.");
                    Ux.Info("Available modules: run 'modulus ui list' to see all.");
                    return 0;
                }

                catalog = catalog
                    .Where(m => installed.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[cyan]Available UI Modules[/]")
                .AddColumn("[cyan]ID[/]")
                .AddColumn("[grey]Package[/]")
                .AddColumn("[grey]Version[/]")
                .AddColumn("[grey]Dependencies[/]")
                .AddColumn("[grey]Features[/]");

            foreach (var m in catalog.OrderBy(m => m.Id))
            {
                var deps = m.Dependencies.Count == 0
                    ? "[grey dim]—[/]"
                    : string.Join(", ", m.Dependencies.Select(d => $"[cyan]{d}[/]"));
                var features = m.Features.Count == 0
                    ? "[grey dim]—[/]"
                    : string.Join(", ", m.Features);
                var status = s.InstalledOnly ? "" : (
                    GetInstalledUiModules(inventory).Contains(m.PackageId, StringComparer.OrdinalIgnoreCase)
                        ? "[green]installed[/]"
                        : "[grey dim]not installed[/]");

                table.AddRow(
                    $"[cyan]{m.Id}[/]",
                    $"[grey]{m.PackageId}[/]",
                    $"[grey]{m.Version}[/]",
                    deps,
                    features);
            }

            AnsiConsole.Write(table);

            if (!s.InstalledOnly)
            {
                var installed = GetInstalledUiModules(inventory);
                AnsiConsole.WriteLine();
                Ux.Info($"Installed: {installed.Count} / {catalog.Count} modules");
                Ux.Info("Run 'modulus ui info <module>' for details, or 'modulus ui add <module>' to install.");
            }

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
