using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus update</c> — updates NuGet package versions in the current
/// Modulus application. Checks for outdated packages, applies updates to
/// <c>Directory.Packages.props</c> (and optionally <c>.csproj</c> files),
/// and runs <c>dotnet restore</c> to verify the changes compile.
/// </summary>
internal sealed class UpdateCommand : Command<UpdateCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Only update Cobytelabs.Modulus.* framework packages")]
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

            AnsiConsole.Write(new Rule("[cyan]Modulus update[/]") { Border = BoxBorder.Rounded });
            AnsiConsole.WriteLine();

            if (Ux.DryRun)
                Ux.Warning("DRY-RUN mode — no files will be modified.");

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

            // Filter to only Cobytelabs.Modulus.* packages if framework-only mode.
            var packagesToCheck = s.FrameworkOnly
                ? currentVersions.Where(kvp => ThirdPartyPackages.IsFrameworkPackage(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : currentVersions;

            if (packagesToCheck.Count == 0)
            {
                Ux.Warning("No package versions found in the project.");
                return 0;
            }

            // 2. Check for updates.
            Ux.Info($"Scanning {packagesToCheck.Count} packages for updates...");
            var updates = await Ux.StatusAsync("Checking NuGet for updates...", () =>
                NuGetVersionService.CheckUpdatesAsync(packagesToCheck, s.FrameworkOnly));

            if (updates.Count == 0)
            {
                Ux.Success("All packages are already up to date.");
                return 0;
            }

            // 3. Display proposed changes.
            var table = new Table()
                .Border(TableBorder.Minimal)
                .AddColumn("[grey]Package[/]")
                .AddColumn("[grey]Current[/]")
                .AddColumn("[grey]Target[/]")
                .AddColumn("[grey]Update[/]");

            foreach (var update in updates)
            {
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
                    $"[{updateColor}]{update.UpdateType}[/]");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // 4. Check for major version bumps and warn.
            var majorUpdates = updates.Where(u => u.UpdateType == UpdateType.Major).ToList();
            if (majorUpdates.Count > 0)
            {
                Ux.Warning($"WARNING: {majorUpdates.Count} package(s) have MAJOR version bumps:");
                foreach (var u in majorUpdates)
                    AnsiConsole.MarkupLine($"  [red]•[/] {u.PackageId}: {u.CurrentVersion} → {u.LatestVersion}");
                AnsiConsole.MarkupLine("[grey]  Major updates may contain breaking changes. Review the release notes before upgrading.[/]");
                AnsiConsole.WriteLine();
            }

            // 5. Confirm (unless --force or --dry-run).
            if (!Ux.DryRun && !Ux.Force)
            {
                if (!Ux.Confirm("Apply these updates?", nonInteractiveDefault: false))
                {
                    Ux.Info("Update cancelled.");
                    return 0;
                }
            }

            // 6. Build the update map.
            var updateMap = updates.ToDictionary(u => u.PackageId, u => u.LatestVersion);

            // 7. Backup and update packages.
            var changes = new List<PackageChange>();

            if (packagesPropsPath is not null)
            {
                // CPM mode: update Directory.Packages.props
                Ux.Info($"Updating {Path.GetFileName(packagesPropsPath)}...");

                if (!Ux.DryRun)
                    ProjectFileService.BackupFile(packagesPropsPath);

                changes = ProjectFileService.UpdateDirectoryPackagesProps(
                    packagesPropsPath, updateMap, dryRun: Ux.DryRun).ToList();
            }
            else
            {
                // Non-CPM mode: update each .csproj file
                var csprojFiles = ProjectFileService.FindAllProjectFiles(inventory.SolutionDir);
                Ux.Info($"Updating {csprojFiles.Count} project file(s)...");

                foreach (var csproj in csprojFiles)
                {
                    if (!Ux.DryRun)
                        ProjectFileService.BackupFile(csproj);

                    var csprojChanges = ProjectFileService.UpdateCsprojPackageReferences(
                        csproj, updateMap, dryRun: Ux.DryRun);
                    changes.AddRange(csprojChanges);
                }
            }

            foreach (var change in changes)
                Ux.Success($"{change.PackageId}: {change.OldVersion} → {change.NewVersion}");

            if (changes.Count == 0)
                Ux.Info("No changes applied.");

            // 8. Run dotnet restore.
            if (!Ux.DryRun && changes.Count > 0)
            {
                Ux.Info("Running dotnet restore...");
                var exitCode = Ux.RunProcess("dotnet", "restore", inventory.SolutionDir, "would run dotnet restore");
                if (exitCode != 0)
                {
                    Ux.Error("dotnet restore failed. Rolling back changes...");

                    // Rollback
                    if (packagesPropsPath is not null)
                    {
                        ProjectFileService.RestoreFromBackup(packagesPropsPath);
                    }
                    else
                    {
                        var csprojFiles = ProjectFileService.FindAllProjectFiles(inventory.SolutionDir);
                        foreach (var csproj in csprojFiles)
                            ProjectFileService.RestoreFromBackup(csproj);
                    }
                    return 1;
                }

                // Clean up backups on success.
                if (packagesPropsPath is not null)
                {
                    ProjectFileService.DeleteBackup(packagesPropsPath);
                }
                else
                {
                    var csprojFiles = ProjectFileService.FindAllProjectFiles(inventory.SolutionDir);
                    foreach (var csproj in csprojFiles)
                        ProjectFileService.DeleteBackup(csproj);
                }
            }

            // 9. Summary.
            AnsiConsole.WriteLine();
            if (Ux.DryRun)
            {
                Ux.Info($"DRY-RUN complete. {changes.Count} package(s) would be updated.");
                AnsiConsole.MarkupLine("[grey]Run without[/] --dry-run [grey]to apply changes.[/]");
            }
            else
            {
                Ux.Success($"Updated {changes.Count} package(s) successfully.");
                if (changes.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[grey]Run[/] dotnet build [grey]to verify the project compiles.[/]");
                }
            }

            return 0;
        });
    }
}
