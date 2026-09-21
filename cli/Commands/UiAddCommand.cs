using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddCommand : Command<UiAddCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI module ID, name, package ID, or namespace (e.g. Identity, Modulus.UI.Identity, Cobytelabs.Modulus.UI.Identity)")]
        [CommandArgument(0, "<module>")]
        public string Module { get; init; } = "";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("Skip dependency installation (install only the requested module)")]
        [CommandOption("--no-deps")]
        [DefaultValue(false)]
        public bool NoDeps { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(s.Module))
            {
                Ux.Error("Module identifier is required. Usage: modulus ui add <module>");
                return 1;
            }

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException(
                    $"This app is API-only ({AppKinds.Property}=api), so it has no UI to add modules to. " +
                    $"To make it a web app, set <{AppKinds.Property}>web</{AppKinds.Property}> in the host project, then run this again.");

            var target = UiModuleCatalog.Find(s.Module);
            var installOrder = s.NoDeps ? [target] : UiModuleCatalog.ResolveInstallOrder(s.Module);
            var installed = GetInstalledUiModules(inventory);

            var toInstall = installOrder
                .Where(m => !installed.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (toInstall.Length == 0)
            {
                Ux.Success($"All modules already installed: {string.Join(", ", installOrder.Select(m => m.Id))}");
                return 0;
            }

            Ux.Info($"Installing {toInstall.Length} UI module{(toInstall.Length == 1 ? "" : "s")}: {string.Join(", ", toInstall.Select(m => m.Id))}");

            foreach (var module in toInstall)
            {
                AddPackageReference(inventory, module);
                WireModule(inventory, module);
            }

            if (!Ux.DryRun)
            {
                RestorePackages(start);
            }

            Ux.Success("UI modules installed successfully.");
            Ux.Info("Run 'dotnet build' to verify the installation.");
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

    private static void AddPackageReference(ModuleDiscovery.AppInventory inventory, UiModuleDefinition module)
    {
        var apiProject = inventory.ApiProjectPath;
        if (apiProject.Length == 0 || !File.Exists(apiProject))
            throw new InvalidOperationException("API project not found. Cannot add package reference.");

        Ux.Status($"Adding package reference: {module.PackageId}@{module.Version}", () =>
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
                throw new InvalidOperationException($"Failed to add package {module.PackageId}: {stderr}");
            }
        });
    }

    private static void WireModule(ModuleDiscovery.AppInventory inventory, UiModuleDefinition module)
    {
        var programCs = inventory.ProgramCsPath;
        if (programCs.Length == 0 || !File.Exists(programCs))
            throw new InvalidOperationException("Program.cs not found. Cannot wire UI module.");

        var original = File.ReadAllText(programCs);
        var content = UiHostWiring.EnsureUiWiring(original, module);

        if (content != original)
        {
            Ux.WriteFile(programCs, content);
            Ux.Success($"Wired {module.Id} in Program.cs");
        }
        else
        {
            Ux.Info($"{module.Id} already wired in Program.cs");
        }

        if (UiAccessGates.WriteSettings(programCs, module) && module.Gate is { } gate)
        {
            Ux.Success($"Restricted {module.Name} to administrators", $"appsettings.json: {gate.Section}:RequirePermission = {gate.Permission}");
        }
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
