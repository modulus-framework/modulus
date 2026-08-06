using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Shared discovery + <c>dotnet ef</c> invocation for the <c>modulus migrate</c>
/// sub-commands. Each module owns its own DbContext + migrations (in its
/// Infrastructure project), applied against the host (API) startup project.
/// </summary>
internal static class MigrateSupport
{
    internal sealed record ModuleProject(string Name, string InfrastructureCsproj);

    /// <summary>The host/startup project (<c>*.Api.csproj</c>) EF resolves config from.</summary>
    public static string? FindStartupProject(string root)
    {
        var apiDir = Path.Combine(root, "src", "API");
        var search = Directory.Exists(apiDir) ? apiDir : root;
        return Directory.EnumerateFiles(search, "*.Api.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>All module Infrastructure projects (each holds one module DbContext).</summary>
    public static IReadOnlyList<ModuleProject> FindModuleProjects(string root, string? moduleFilter)
    {
        var modulesDir = Path.Combine(root, "src", "Modules");
        var search = Directory.Exists(modulesDir) ? modulesDir : root;

        var projects = new List<ModuleProject>();
        foreach (var csproj in Directory.EnumerateFiles(
                     search, "*.Infrastructure.csproj", SearchOption.AllDirectories))
        {
            // "MyApp.Modules.Catalog.Infrastructure" → "Catalog"
            var stem = Path.GetFileNameWithoutExtension(csproj);
            var withoutSuffix = stem[..^".Infrastructure".Length];
            var name = withoutSuffix.Contains(".Modules.")
                ? withoutSuffix[(withoutSuffix.IndexOf(".Modules.", StringComparison.Ordinal) + ".Modules.".Length)..]
                : withoutSuffix.Split('.')[^1];

            if (moduleFilter is null
                || name.Equals(moduleFilter, StringComparison.OrdinalIgnoreCase))
                projects.Add(new ModuleProject(name, csproj));
        }
        return projects;
    }

    /// <summary>Runs <c>dotnet ef</c> with the given args from <paramref name="workingDir"/>.</summary>
    public static int RunDotnetEf(string workingDir, params string[] args)
    {
        var argString = "ef " + string.Join(' ', args.Select(a => Quote(a)));
        return Ux.RunProcess("dotnet", argString, workingDir, dryRunLabel: "");
    }

    private static string Quote(string arg)
    {
        // Shell-style: quote only if it contains whitespace and isn't already quoted.
        if (arg.Length > 0 && arg[0] == '"') return arg;
        return arg.Any(c => char.IsWhiteSpace(c)) ? $"\"{arg}\"" : arg;
    }

    /// <summary>Resolves the app root and startup project, printing errors on failure.</summary>
    public static bool TryResolve(
        string? output,
        out string root,
        out string startupProject,
        out string startupRelative)
    {
        root = Path.GetFullPath(output ?? "./");
        startupProject = FindStartupProject(root) ?? "";
        startupRelative = "";

        if (startupProject.Length == 0)
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] No [grey]*.Api.csproj[/] startup project found under {0}. " +
                "Run this from a Modulus app directory.", root);
            return false;
        }

        startupRelative = Path.GetRelativePath(root, startupProject);
        return true;
    }
}

/// <summary>
/// <c>modulus migrate add &lt;name&gt;</c> — scaffolds an EF Core migration in each
/// module's Infrastructure project (or a single module with <c>--module</c>).
/// </summary>
internal sealed class MigrateAddCommand : Command<MigrateAddCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Migration name (e.g. InitialCreate, AddOrderTotals)")]
        [CommandArgument(0, "<name>")]
        public required string Name { get; init; }

        [Description("Only add the migration to this module (default: every module)")]
        [CommandOption("-m|--module")]
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
            if (!MigrateSupport.TryResolve(s.Output, out var root, out _, out var startupRel))
                return 1;

            var modules = MigrateSupport.FindModuleProjects(root, s.Module);
            if (modules.Count == 0)
            {
                Ux.Error($"No module Infrastructure projects found" +
                    (s.Module is null ? "." : $" for module '{s.Module}'."));
                return 1;
            }

            Ux.Info($"Scaffolding migration [cyan]{s.Name}[/] in [grey]{modules.Count}[/] module(s)…");

            var failed = 0;
            foreach (var m in modules)
            {
                var projRel = Path.GetRelativePath(root, m.InfrastructureCsproj);
                if (Ux.DryRun)
                    Ux.DryRunNote($"would scaffold [cyan]{s.Name}[/] in {m.Name}");
                else
                    Ux.Info($"  [grey]›[/] {m.Name}");

                var code = MigrateSupport.RunDotnetEf(root,
                    "migrations", "add", s.Name,
                    "--project", projRel,
                    "--startup-project", startupRel,
                    "--output-dir", "Migrations");

                if (code != 0)
                {
                    Ux.Error($"Migration failed for {m.Name}");
                    failed++;
                }
            }

            if (failed == 0)
            {
                if (Ux.DryRun)
                    Ux.Success($"Would add migration to {modules.Count} module(s).",
                        "Apply with: modulus migrate update");
                else
                    Ux.Success($"Added migration to {modules.Count} module(s).",
                        "Apply with: modulus migrate update");
            }
            return failed == 0 ? 0 : 1;
        });
    }
}

/// <summary>
/// <c>modulus migrate update</c> — applies pending migrations to each module's
/// database (<c>dotnet ef database update</c>).
/// </summary>
internal sealed class MigrateUpdateCommand : Command<MigrateUpdateCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Only update this module (default: every module)")]
        [CommandOption("-m|--module")]
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
            if (!MigrateSupport.TryResolve(s.Output, out var root, out _, out var startupRel))
                return 1;

            var modules = MigrateSupport.FindModuleProjects(root, s.Module);
            if (modules.Count == 0)
            {
                Ux.Error($"No module Infrastructure projects found" +
                    (s.Module is null ? "." : $" for module '{s.Module}'."));
                return 1;
            }

            // Confirm before touching real databases (skipped by --force or CI).
            var target = s.Module is null ? $"{modules.Count} module database(s)" : $"the {s.Module} database";
            if (!Ux.Confirm($"Apply pending migrations to [cyan]{target}[/]?", nonInteractiveDefault: true))
            {
                Ux.Warning("Aborted (nothing was changed).");
                return 0;
            }

            var failed = 0;
            foreach (var m in modules)
            {
                var projRel = Path.GetRelativePath(root, m.InfrastructureCsproj);
                if (Ux.DryRun)
                    Ux.DryRunNote($"would update database for {m.Name}");
                else
                    Ux.Info($"  [grey]›[/] {m.Name}");

                var code = MigrateSupport.RunDotnetEf(root,
                    "database", "update",
                    "--project", projRel,
                    "--startup-project", startupRel);

                if (code != 0)
                {
                    Ux.Error($"Update failed for {m.Name}");
                    failed++;
                }
            }

            if (failed == 0)
            {
                if (Ux.DryRun)
                    Ux.Success($"Would apply migrations to {modules.Count} module(s).");
                else
                    Ux.Success($"Applied migrations to {modules.Count} module(s).");
            }
            return failed == 0 ? 0 : 1;
        });
    }
}
