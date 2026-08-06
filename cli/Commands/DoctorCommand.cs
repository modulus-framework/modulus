using System.ComponentModel;
using System.Diagnostics;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus doctor</c> — verifies the environment and the current app
/// are ready to build and migrate: checks the .NET SDK, the dotnet-ef
/// global tool, that we're inside a Modulus app, that every module's
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
        return CommandRunner.Run(() =>
        {
            var start = Path.GetFullPath(s.Output ?? "./");

            AnsiConsole.Write(new Rule("[cyan]Modulus doctor[/]") { Border = BoxBorder.Rounded });
            AnsiConsole.WriteLine();

            var checks = new List<CheckResult>();

            checks.Add(CheckDotNetSdk());
            checks.Add(CheckDotNetEf());

            var inventory = ModuleDiscovery.Inventory(start);
            if (inventory is null)
            {
                checks.Add(CheckResult.Fail(
                    "Inside a Modulus app",
                    "No .slnx found in the current directory tree."));
                return Report(checks);
            }

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

            // Each module: Infrastructure project + DbContext + design-time factory.
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
            }

            if (inventory.Modules.Count == 0)
            {
                checks.Add(CheckResult.Warn(
                    "Modules",
                    "None found under src/Modules/. Add one with: modulus add-module <Name>"));
            }

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

    private static CheckResult CheckFile(string name, string fullPath, string solutionDir)
    {
        if (File.Exists(fullPath))
        {
            var rel = Path.GetRelativePath(solutionDir, fullPath);
            return CheckResult.Pass(name, rel);
        }
        return CheckResult.Fail(name, "missing: " + (File.Exists(solutionDir)
            ? Path.GetRelativePath(solutionDir, fullPath)
            : fullPath));
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
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return (proc.ExitCode, stdout);
    }

    private enum CheckKind { Pass, Warn, Fail }
    private sealed record CheckResult(CheckKind Kind, string Name, string Detail)
    {
        public static CheckResult Pass(string name, string detail) => new(CheckKind.Pass, name, detail);
        public static CheckResult Warn(string name, string detail) => new(CheckKind.Warn, name, detail);
        public static CheckResult Fail(string name, string detail) => new(CheckKind.Fail, name, detail);
    }
}
