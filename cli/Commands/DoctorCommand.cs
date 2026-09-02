using System.ComponentModel;
using System.Diagnostics;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus doctor</c> — verifies the environment and the current app
/// are ready to build and migrate: checks the .NET SDK, the migration tools
/// each module's engine needs (dotnet-ef for EF Core modules, dbsh for dbsh
/// modules), that we're inside a Modulus app, that every module's
/// Infrastructure project + DbContext exist, and that a NuGet.config is
/// present. Reports each check as pass/warn/fail.
/// </summary>
internal sealed class DoctorCommand : Command<DoctorCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(async () =>
        {
            var start = Path.GetFullPath(s.Output ?? "./");

            AnsiConsole.Write(new Rule("[cyan]Modulus doctor[/]") { Border = BoxBorder.Rounded });
            AnsiConsole.WriteLine();

            var checks = new List<CheckResult>();

            checks.Add(CheckDotNetSdk());

            var inventory = ModuleDiscovery.Inventory(start);
            if (inventory is null)
            {
                checks.Add(CheckResult.Fail(
                    "Inside a Modulus app",
                    "No .slnx found in the current directory tree."));
                return Report(checks);
            }

            // Per-module migration engine (dbsh modules carry a
            // Database/Config/migration.json marker in their Infrastructure dir).
            var dbshModules = new List<string>();
            foreach (var m in inventory.Modules)
            {
                var infraDir = Path.Combine(m.Directory, m.Namespace + ".Infrastructure");
                if (MigrateSupport.IsDbshModule(infraDir))
                    dbshModules.Add(m.Name);
            }
            var efModules = inventory.Modules.Count - dbshModules.Count;

            // Each engine's tool is only required when at least one module uses it.
            if (efModules > 0 || inventory.Modules.Count == 0)
                checks.Add(CheckDotNetEf());
            if (dbshModules.Count > 0)
                checks.Add(CheckDbsh());

            checks.Add(CheckResult.Pass(
                "Inside a Modulus app",
                Path.GetFileName(inventory.SolutionPath)));

            checks.Add(CheckFile(
                "Host API project exists",
                inventory.ApiProjectPath,
                inventory.SolutionDir));

            checks.Add(CheckFile(
                "Program.cs exists",
                inventory.ProgramCsPath,
                inventory.SolutionDir));

            checks.Add(CheckFile(
                "NuGet.config present",
                Path.Combine(inventory.SolutionDir, "NuGet.config"),
                inventory.SolutionDir));

            checks.Add(CheckFile(
                ".gitignore present",
                Path.Combine(inventory.SolutionDir, ".gitignore"),
                inventory.SolutionDir));

            // Each module: Infrastructure project + DbContext + design-time factory
            // + migration engine marker consistency.
            foreach (var m in inventory.Modules)
            {
                var infraDir = Path.Combine(m.Directory, m.Namespace + ".Infrastructure");
                checks.Add(CheckFile(
                    $"{m.Name}: Infrastructure project",
                    Path.Combine(infraDir, m.Namespace + ".Infrastructure.csproj"),
                    inventory.SolutionDir));

                checks.Add(CheckFile(
                    $"{m.Name}: DbContext",
                    Path.Combine(infraDir, m.Name + "DbContext.cs"),
                    inventory.SolutionDir));

                checks.Add(CheckFile(
                    $"{m.Name}: design-time factory",
                    Path.Combine(infraDir, m.Name + "DbContextFactory.cs"),
                    inventory.SolutionDir));

                checks.Add(MigrateSupport.IsDbshModule(infraDir)
                    ? CheckResult.Pass(
                        $"{m.Name}: migration engine",
                        "dbsh (Database/Config/migration.json)")
                    : CheckResult.Pass(
                        $"{m.Name}: migration engine",
                        "efcore (Migrations/)"));
            }

            if (inventory.Modules.Count == 0)
            {
                checks.Add(CheckResult.Warn(
                    "Modules",
                    "None found under src/Modules/. Add one with: modulus add-module <Name>"));
            }

            // ── Version checks ─────────────────────────────────────────
            checks.Add(await CheckCliVersionAsync());
            checks.Add(await CheckFrameworkVersionAsync(inventory.SolutionDir));

            return Report(checks);
        });
    }

    private static int Report(List<CheckResult> checks)
    {
        var table = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("[grey]Check[/]")
            .AddColumn("[grey]Status[/]")
            .AddColumn("[grey]Detail[/]");

        var failures = 0;
        var warnings = 0;
        foreach (var c in checks)
        {
            var status = c.Kind switch
            {
                CheckKind.Pass => "[green]✓ ok[/]",
                CheckKind.Warn => "[yellow]! warn[/]",
                _ => "[red]✗ fail[/]",
            };
            table.AddRow($"[cyan]{c.Name}[/]", status, Markup.Escape(c.Detail));
            if (c.Kind == CheckKind.Fail) failures++;
            else if (c.Kind == CheckKind.Warn) warnings++;
        }
        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        if (failures == 0 && warnings == 0)
            Ux.Success("All checks passed.");
        else if (failures == 0)
            Ux.Warning($"{warnings} warning(s), 0 failures.");
        else
            Ux.Error($"{failures} failure(s), {warnings} warning(s).");

        return failures == 0 ? 0 : 1;
    }

    // ── Individual checks ─────────────────────────────────────────────

    private static CheckResult CheckDotNetSdk()
    {
        try
        {
            var (code, output) = Capture("dotnet", "--version");
            if (code != 0)
                return CheckResult.Fail(".NET SDK", "`dotnet --version` exited non-zero.");
            var version = output.Trim();
            return CheckResult.Pass(".NET SDK", $"v{version}");
        }
        catch (Exception ex)
        {
            return CheckResult.Fail(".NET SDK", $"dotnet not found on PATH ({ex.Message}).");
        }
    }

    private static CheckResult CheckDotNetEf()
    {
        try
        {
            var (code, output) = Capture("dotnet", "ef --version");
            if (code != 0)
                return CheckResult.Warn(
                    "dotnet-ef tool",
                    "Not installed. Run: dotnet tool install --global dotnet-ef");
            var line = output.Split('\n').FirstOrDefault(l => l.Contains("Tools", StringComparison.Ordinal))?.Trim()
                ?? output.Trim();
            return CheckResult.Pass("dotnet-ef tool", line);
        }
        catch (Exception ex)
        {
            return CheckResult.Warn("dotnet-ef tool", $"dotnet not found ({ex.Message}).");
        }
    }

    /// <summary>
    /// dbsh is only required when at least one module manages its schema with
    /// SQL migrations (dbsh engine) — checked against <c>dbsh --version</c>.
    /// </summary>
    private static CheckResult CheckDbsh()
    {
        try
        {
            var (code, output) = Capture("dbsh", "--version");
            if (code != 0)
                return CheckResult.Warn(
                    "dbsh tool",
                    "Not usable. Install: dotnet tool install --global dbsh");
            var line = output.Split('\n').FirstOrDefault(l => l.Contains("dbsh", StringComparison.OrdinalIgnoreCase))?.Trim()
                ?? output.Trim();
            return CheckResult.Pass("dbsh tool", line);
        }
        catch (Exception ex)
        {
            return CheckResult.Warn(
                "dbsh tool",
                $"dbsh not found on PATH ({ex.Message}). Install: dotnet tool install --global dbsh");
        }
    }

    private static CheckResult CheckFile(string name, string fullPath, string solutionDir)
    {
        if (File.Exists(fullPath))
        {
            var rel = Path.GetRelativePath(solutionDir, fullPath);
            return CheckResult.Pass(name, rel);
        }
        return CheckResult.Fail(name, "missing: " + (Directory.Exists(solutionDir)
            ? Path.GetRelativePath(solutionDir, fullPath)
            : fullPath));
    }

    // ── Version checks ────────────────────────────────────────────────

    private static async Task<CheckResult> CheckCliVersionAsync()
    {
        try
        {
            var result = await VersionCheckService.CheckCliUpdatesAsync();
            if (result.Error is not null)
                return CheckResult.Warn("CLI tool version", result.Error);

            if (result.HasUpdate)
                return CheckResult.Warn(
                    "CLI tool version",
                    $"v{result.InstalledVersion} installed, v{result.LatestVersion} available. Run: {result.UpdateCommand}");

            return CheckResult.Pass("CLI tool version", $"v{result.InstalledVersion} (latest)");
        }
        catch (Exception ex)
        {
            return CheckResult.Warn("CLI tool version", $"Could not check: {ex.Message}");
        }
    }

    private static async Task<CheckResult> CheckFrameworkVersionAsync(string solutionDir)
    {
        try
        {
            var currentVersion = ProjectFileService.DetectCurrentFrameworkVersion(solutionDir);
            if (currentVersion is null)
                return CheckResult.Warn(
                    "Framework version",
                    "Could not detect Cobytelabs.Modulus.* version in the project.");

            var latestVersion = await NuGetVersionService.GetLatestVersionAsync(
                ThirdPartyPackages.FrameworkPackagePrefix + "Core");

            if (latestVersion is null)
                return CheckResult.Pass("Framework version", $"v{currentVersion} (could not query NuGet)");

            if (NuGetVersionService.IsNewer(latestVersion, currentVersion))
                return CheckResult.Warn(
                    "Framework version",
                    $"v{currentVersion} installed, v{latestVersion} available. Run: modulus update");

            return CheckResult.Pass("Framework version", $"v{currentVersion} (latest)");
        }
        catch (Exception ex)
        {
            return CheckResult.Warn("Framework version", $"Could not check: {ex.Message}");
        }
    }

    /// <summary>Captures stdout of a process (no console output, short timeout).</summary>
    private static (int code, string output) Capture(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(5000);
        // Drain remaining buffers to avoid deadlock if process wrote more
        // than the pipe buffer (~256KB) before exiting.
        stdout.Wait();
        stderr.Wait();
        return (proc.ExitCode, stdout.Result);
    }

    private enum CheckKind { Pass, Warn, Fail }
    private sealed record CheckResult(CheckKind Kind, string Name, string Detail)
    {
        public static CheckResult Pass(string name, string detail) => new(CheckKind.Pass, name, detail);
        public static CheckResult Warn(string name, string detail) => new(CheckKind.Warn, name, detail);
        public static CheckResult Fail(string name, string detail) => new(CheckKind.Fail, name, detail);
    }
}
