using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiInfoCommand : Command<UiInfoCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI module ID, name, package ID, or namespace (e.g. Identity, Modulus.UI.Identity, Cobytelabs.Modulus.UI.Identity)")]
        [CommandArgument(0, "<module>")]
        public string Module { get; init; } = "";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(s.Module))
            {
                Ux.Error("Module identifier is required. Usage: modulus ui info <module>");
                return 1;
            }

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            var module = UiModuleCatalog.Find(s.Module);
            var installed = GetInstalledUiModules(inventory);
            var isInstalled = installed.Contains(module.PackageId, StringComparer.OrdinalIgnoreCase);

            var panel = new Panel(new Rows(
                new Markup($"[cyan]{module.Id}[/] [grey]({module.Name})[/]"),
                new Markup($"[grey]Package     :[/] {module.PackageId}"),
                new Markup($"[grey]Version     :[/] {module.Version}"),
                new Markup($"[grey]Namespace   :[/] {module.Namespace}"),
                new Markup($"[grey]Add method  :[/] {module.AddMethod}"),
                new Markup($"[grey]Core module :[/] {(module.IsCore ? "[green]yes[/]" : "[grey]no[/]")}"),
                new Markup($"[grey]Installed   :[/] {(isInstalled ? "[green]yes[/]" : "[grey dim]no[/]")}")))
                .Border(BoxBorder.Rounded)
                .Header("[yellow]UI Module Info[/]");
            AnsiConsole.Write(panel);

            // Dependencies
            if (module.Dependencies.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]UI Module Dependencies[/]");
                foreach (var dep in module.Dependencies)
                {
                    var depModule = UiModuleCatalog.Find(dep);
                    var depInstalled = installed.Contains(depModule.PackageId, StringComparer.OrdinalIgnoreCase);
                    AnsiConsole.MarkupLine(
                        "  [cyan]{0}[/] [grey]({1})[/] {2}",
                        depModule.Id,
                        depModule.PackageId,
                        depInstalled ? "[green]✓ installed[/]" : "[grey dim]not installed[/]");
                }
            }

            // Backend packages
            if (module.BackendPackageIds.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Required Backend Packages[/]");
                var installedBackend = GetInstalledBackendPackages(inventory);
                foreach (var bp in module.BackendPackageIds)
                {
                    var bpInstalled = installedBackend.Contains(bp, StringComparer.OrdinalIgnoreCase);
                    AnsiConsole.MarkupLine(
                        "  [cyan]{0}[/] {1}",
                        bp,
                        bpInstalled ? "[green]✓ installed[/]" : "[grey dim]not installed[/]");
                }
            }

            // Features
            if (module.Features.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Features[/]");
                foreach (var f in module.Features)
                    AnsiConsole.MarkupLine("  [grey]•[/] {0}", f);
            }

            // Installation wiring
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Wiring Required in Program.cs[/]");
            AnsiConsole.MarkupLine("  [grey]builder.Services.AddModulusLocalization();[/]");
            AnsiConsole.MarkupLine("  [grey]builder.Services.AddModulusUi();[/]");
            if (!module.IsCore)
            {
                AnsiConsole.MarkupLine($"  [grey]builder.Services.{module.AddMethod}(builder.Configuration);[/]");
            }
            AnsiConsole.MarkupLine("  [grey]// ...[/]");
            AnsiConsole.MarkupLine("  [grey]app.UseStaticFiles();[/]");
            AnsiConsole.MarkupLine("  [grey]app.MapRazorPages();[/]");
            if (!module.IsCore && module.HasEndpoints)
            {
                var mapMethod = module.AddMethod.Replace("Add", "Map").Replace("Ui", "Ui");
                AnsiConsole.MarkupLine($"  [grey]app.{mapMethod}();[/]");
            }
            AnsiConsole.MarkupLine("  [grey]app.MapModulusUiMenu();[/]");

            if (!isInstalled)
            {
                AnsiConsole.WriteLine();
                Ux.Info($"Run 'modulus ui add {module.Id}' to install this module.");
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

    private static HashSet<string> GetInstalledBackendPackages(ModuleDiscovery.AppInventory inventory)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length > 0 && File.Exists(apiProject))
        {
            var refs = ProjectFileService.ParseCsprojPackageReferences(apiProject);
            foreach (var kvp in refs)
            {
                installed.Add(kvp.Key);
            }
        }
        return installed;
    }
}
