using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus outdated</c> — shows all packages that have a newer version
/// available on NuGet. Reports framework (<c>Cobytelabs.Modulus.*</c>) and
/// third-party packages with their current vs. latest version and the
/// severity of the version bump (patch / minor / major).
/// </summary>
internal sealed class OutdatedCommand : Command<OutdatedCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Only check Cobytelabs.Modulus.* framework packages")]
        [CommandOption("--framework-only")]
        public bool FrameworkOnly { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(async () =>
        {
            var start = Path.GetFullPath(s.Output ?? "./");

            // Verify we're inside a Modulus app.
            var inventory = ModuleDiscovery.Inventory(start);
            if (inventory is null)
            {
                Ux.Error("Not inside a Modulus application. No .slnx found.");
                return 1;
            }

            AnsiConsole.Write(new Rule("[cyan]Modulus outdated[/]") { Border = BoxBorder.Rounded });
            AnsiConsole.WriteLine();

            // 1. Find Directory.Packages.props and parse current versions.
            var packagesPropsPath = ProjectFileService.FindDirectoryPackagesProps(inventory.SolutionDir);
            Dictionary<string, string> currentVersions;

            if (packagesPropsPath is not null)
            {
                currentVersions = ProjectFileService.ParseDirectoryPackagesProps(packagesPropsPath);
            }
            else
            {
                currentVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            // If CPM has no versions (or CPM is not used), scan .csproj files.
            if (currentVersions.Count == 0)
            {
                var csprojFiles = ProjectFileService.FindAllProjectFiles(inventory.SolutionDir);
                foreach (var csproj in csprojFiles)
                {
                    var refs = ProjectFileService.ParseCsprojPackageReferences(csproj);
                    foreach (var (id, version) in refs)
                    {
                        // Only add if not already present (CPM takes precedence).
                        if (!currentVersions.ContainsKey(id))
                            currentVersions[id] = version;
                    }
                }
            }

            if (currentVersions.Count == 0)
            {
                Ux.Warning("No package versions found in the project.");
                return 0;
            }

            // Filter to only Cobytelabs.Modulus.* packages if framework-only mode.
            var packagesToCheck = s.FrameworkOnly
                ? currentVersions.Where(kvp => ThirdPartyPackages.IsFrameworkPackage(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : currentVersions;

            Ux.Info($"Scanning {packagesToCheck.Count} packages for updates...");

            // 2. Query NuGet for latest versions.
            IReadOnlyList<PackageUpdate> updates;
            try
            {
                updates = await Ux.StatusAsync("Checking NuGet for updates...", () =>
                    NuGetVersionService.CheckUpdatesAsync(packagesToCheck, s.FrameworkOnly));
            }
            catch (Exception ex)
            {
                Ux.Error($"Failed to check for updates: {ex.Message}");
                if (ex.InnerException is not null)
                    Ux.Error($"Inner: {ex.InnerException.Message}");
                return 1;
            }

            if (updates.Count == 0)
            {
                Ux.Success("All packages are up to date.");
                return 0;
            }

            // 3. Display results in a table.
            var table = new Table()
                .Border(TableBorder.Minimal)
                .AddColumn("[grey]Package[/]")
                .AddColumn("[grey]Current[/]")
                .AddColumn("[grey]Available[/]")
                .AddColumn("[grey]Type[/]")
                .AddColumn("[grey]Update[/]");

            var majorCount = 0;
            var minorCount = 0;
            var patchCount = 0;

            foreach (var update in updates.OrderByDescending(u => u.IsFrameworkPackage ? 0 : 1).ThenBy(u => u.PackageId))
            {
                var typeLabel = update.IsFrameworkPackage ? "[cyan]Framework[/]" : "Third-party";
                var (updateLabel, updateColor) = update.UpdateType switch
                {
                    UpdateType.Major => ("Major", "red"),
                    UpdateType.Minor => ("Minor", "yellow"),
                    _ => ("Patch", "green"),
                };

                table.AddRow(
                    update.PackageId,
                    $"[grey]{update.CurrentVersion}[/]",
                    $"[bold]{update.LatestVersion}[/]",
                    typeLabel,
                    $"[{updateColor}]{update.UpdateType}[/]");

                switch (update.UpdateType)
                {
                    case UpdateType.Major: majorCount++; break;
                    case UpdateType.Minor: minorCount++; break;
                    default: patchCount++; break;
                }
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // 4. Summary.
            var parts = new List<string>();
            if (majorCount > 0) parts.Add($"[red]{majorCount} major[/]");
            if (minorCount > 0) parts.Add($"[yellow]{minorCount} minor[/]");
            if (patchCount > 0) parts.Add($"[green]{patchCount} patch[/]");

            Ux.Info($"Summary: {updates.Count} outdated package(s) ({string.Join(", ", parts)})");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Run[/] modulus update [grey]to update packages.[/]");

            return 0;
        });
    }
}
