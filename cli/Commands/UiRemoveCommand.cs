using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiRemoveCommand : Command<UiRemoveCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI module ID, name, package ID, or namespace (e.g. Notifications, Modulus.UI.Notifications, Cobytelabs.Modulus.UI.Notifications)")]
        [CommandArgument(0, "<module>")]
        public string Module { get; init; } = "";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Also remove dependent UI modules (e.g. removing Identity also removes Users)")]
        [CommandOption("--cascade")]
        [DefaultValue(false)]
        public bool Cascade { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(s.Module))
            {
                Ux.Error("Module identifier is required. Usage: modulus ui remove <module>");
                return 1;
            }

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            var target = UiModuleCatalog.Find(s.Module);
            var installed = GetInstalledUiModules(inventory);

            if (!installed.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                Ux.Warning($"Module {target.Id} is not installed.");
                return 0;
            }

            var toRemove = new List<UiModuleDefinition> { target };

            if (s.Cascade)
            {
                var dependents = UiModuleCatalog.All
                    .Where(m => m.Dependencies.Contains(target.Id, StringComparer.OrdinalIgnoreCase)
                        && installed.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                toRemove.AddRange(dependents);
            }

            if (toRemove.Count > 1 && !Ux.Confirm($"Remove {toRemove.Count} modules: {string.Join(", ", toRemove.Select(m => m.Id))}?"))
            {
                Ux.Info("Cancelled.");
                return 0;
            }

            foreach (var module in toRemove)
            {
                RemovePackageReference(inventory, module);
                UnwireModule(inventory, module);
            }

            if (!Ux.DryRun)
            {
                RestorePackages(start);
            }

            Ux.Success("UI modules removed successfully.");
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

    private static void RemovePackageReference(ModuleDiscovery.AppInventory inventory, UiModuleDefinition module)
    {
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length == 0 || !File.Exists(apiProject))
            throw new InvalidOperationException("API project not found. Cannot remove package reference.");

        Ux.Status($"Removing package reference: {module.PackageId}", () =>
        {
            var command = $"dotnet remove \"{apiProject}\" package \"{module.PackageId}\"";
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
                throw new InvalidOperationException($"Failed to remove package {module.PackageId}: {stderr}");
            }
        });
    }

    private static void UnwireModule(ModuleDiscovery.AppInventory inventory, UiModuleDefinition module)
    {
        var programCs = inventory.ProgramCsPath;
        if (programCs.Length == 0 || !File.Exists(programCs))
            throw new InvalidOperationException("Program.cs not found. Cannot unwire UI module.");

        var content = File.ReadAllText(programCs);
        var original = content;

        if (!module.IsCore)
        {
            var addCall = $"builder.Services.{module.AddMethod}(builder.Configuration);";
            content = RemoveLine(content, addCall);

            var mapMethod = module.AddMethod.Replace("Add", "Map").Replace("Ui", "Ui");
            var mapCall = $"app.{mapMethod}();";
            content = RemoveLine(content, mapCall);
        }

        // Check if any other non-core UI modules remain to decide whether to keep core wiring
        var remaining = GetRemainingUiModules(inventory, module);
        if (remaining.Count == 0)
        {
            content = RemoveLine(content, "builder.Services.AddModulusUi();");
            content = RemoveLine(content, "builder.Services.AddModulusLocalization();");
            content = RemoveLine(content, "app.UseStaticFiles();");
            content = RemoveLine(content, "app.MapRazorPages();");
            content = RemoveLine(content, "app.MapModulusUiMenu();");
        }

        if (content != original)
        {
            Ux.WriteFile(programCs, content);
            Ux.Success($"Unwired {module.Id} from Program.cs");
        }
        else
        {
            Ux.Info($"{module.Id} already unwired in Program.cs");
        }
    }

    private static HashSet<string> GetRemainingUiModules(ModuleDiscovery.AppInventory inventory, UiModuleDefinition currentModule)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length > 0 && File.Exists(apiProject))
        {
            var refs = ProjectFileService.ParseCsprojPackageReferences(apiProject);
            foreach (var kvp in refs)
            {
                if (kvp.Key.StartsWith("Cobytelabs.Modulus.UI.", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(kvp.Key, currentModule.PackageId, StringComparison.OrdinalIgnoreCase))
                    installed.Add(kvp.Key);
            }
        }
        return installed;
    }

    private static string RemoveLine(string content, string line)
    {
        var lines = content.Split('\n');
        var filtered = lines.Where(l => !l.Trim().Contains(line.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return string.Join("\n", filtered);
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
