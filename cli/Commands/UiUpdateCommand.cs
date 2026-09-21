using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiUpdateCommand : Command<UiUpdateCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Optional UI module to update (default: update all installed UI modules)")]
        [CommandArgument(0, "[module]")]
        public string? Module { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
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

            var installed = GetInstalledUiModules(inventory);

            if (installed.Count == 0)
            {
                Ux.Info("No UI modules installed.");
                return 0;
            }

            var toUpdate = new List<(UiModuleDefinition Module, string CurrentVersion)>();

            if (!string.IsNullOrWhiteSpace(s.Module))
            {
                var target = UiModuleCatalog.Find(s.Module);
                if (!installed.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase))
                {
                    Ux.Warning($"Module {target.Id} is not installed.");
                    return 0;
                }
                var currentVersion = GetCurrentVersion(inventory, target.PackageId);
                toUpdate.Add((target, currentVersion));
            }
            else
            {
                foreach (var pkgId in installed)
                {
                    var module = UiModuleCatalog.All.FirstOrDefault(m => string.Equals(m.PackageId, pkgId, StringComparison.OrdinalIgnoreCase));
                    if (module is not null)
                    {
                        var currentVersion = GetCurrentVersion(inventory, pkgId);
                        toUpdate.Add((module, currentVersion));
                    }
                }
            }

            var updates = toUpdate
                .Where(t => string.Compare(t.Module.Version, t.CurrentVersion, StringComparison.Ordinal) > 0)
                .ToArray();

            if (updates.Length == 0)
            {
                Ux.Success("All UI modules are up to date.");
                return 0;
            }

            Ux.Info($"Updating {updates.Length} UI module{(updates.Length == 1 ? "" : "s")}:");

            foreach (var (module, current) in updates)
            {
                Ux.Detail($"{module.Id}: {current} -> {module.Version}");
            }

            if (Ux.DryRun)
            {
                Ux.Info("Dry run complete. No changes made.");
                return 0;
            }

            if (!Ux.Confirm("Proceed with update?"))
            {
                Ux.Info("Cancelled.");
                return 0;
            }

            foreach (var (module, _) in updates)
            {
                UpdatePackageReference(inventory, module);
            }

            RestorePackages(start);
            Ux.Success("UI modules updated successfully.");
            return 0;
        });
    }

    private static HashSet<string> GetInstalledUiModules(ModuleDiscovery.AppInventory inventory)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length > 0 && File.Exists(apiProject))
        {
            var refs = ProjectFileService.ParseCsprojPackageReferences(apiProject);
            foreach (var kvp in refs)
            {
                if (kvp.Key.StartsWith("Cobytelabs.Modulus.UI.", StringComparison.OrdinalIgnoreCase))
                    installed.Add(kvp.Key);
            }
        }
        return installed;
    }

    private static string GetCurrentVersion(ModuleDiscovery.AppInventory inventory, string packageId)
    {
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length > 0 && File.Exists(apiProject))
        {
            var refs = ProjectFileService.ParseCsprojPackageReferences(apiProject);
            if (refs.TryGetValue(packageId, out var version))
                return version;
        }
        return "0.0.0";
    }

    private static void UpdatePackageReference(ModuleDiscovery.AppInventory inventory, UiModuleDefinition module)
    {
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length == 0 || !File.Exists(apiProject))
            throw new InvalidOperationException("API project not found. Cannot update package reference.");

        Ux.Status($"Updating {module.PackageId} to {module.Version}", () =>
        {
            var command = $"dotnet add \"{apiProject}\" package \"{module.PackageId}\" --version {module.Version}";
            if (Ux.DryRun)
            {
                Ux.DryRunNote($"would run: {command}");
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", command)
            {
                WorkingDirectory = Path.GetDirectoryName(apiProject),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                var stderr = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"Failed to update package {module.PackageId}: {stderr}");
            }
        });
    }

    private static void RestorePackages(string appRoot)
    {
        Ux.Status("Restoring packages", () =>
        {
            var command = "dotnet restore";
            if (Ux.DryRun)
            {
                Ux.DryRunNote($"would run: {command} in {appRoot}");
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "restore")
            {
                WorkingDirectory = appRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                var stderr = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"Package restore failed: {stderr}");
            }
        });
    }
}
