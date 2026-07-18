using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class NewModuleCommand : Command<NewModuleCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [Description("Name of the module (e.g. Catalog, Orders)")]
        [CommandArgument(0, "<name>")]
        public required string Name { get; init; }

        [Description("Root namespace of the application (e.g. MyApp)")]
        [CommandOption("--app")]
        public string? App { get; init; }

        [Description("Output directory")]
        [CommandOption("-o|--output")]
        [DefaultValue("./")]
        public string? Output { get; init; }

        [Description("Database provider: SQLite (default), SqlServer, PostgreSQL, MySQL")]
        [CommandOption("-d|--database")]
        [DefaultValue("SQLite")]
        public string Database { get; init; } = "SQLite";
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        var moduleName = s.Name;
        var rootNs = s.App ?? DetectRootNamespace(s.Output ?? "./");
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        var outputBase = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputBase, "src", "Modules", moduleNs);

        if (Directory.Exists(projectDir))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Module directory already exists: {0}", projectDir);
            return 1;
        }

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleName = moduleName,
            ModuleNamespace = moduleNs,
            DbProvider = s.Database,
        };

        // Generate a blank layered module skeleton (no entity yet).
        var appCmd = new NewAppCommand();
        appCmd.GenerateModule(projectDir, model);

        // Add all layer projects to the solution if one exists nearby.
        var slnx = SolutionHelper.FindSolution(outputBase);
        if (slnx is not null)
        {
            foreach (var rel in NewAppCommand.ModuleProjectPaths(rootNs, moduleName))
            {
                var absInSln = rel.Replace('\\', '/');
                SolutionHelper.AddProject(slnx, absInSln);
            }
            AnsiConsole.MarkupLine("[green]✓[/] Added 4 projects to solution: [grey]{0}[/]", Path.GetFileName(slnx));
        }

        AnsiConsole.MarkupLine("[green]✓[/] Created layered module [cyan]{0}[/] at [grey]{1}[/]",
            moduleName, projectDir);

        AnsiConsole.MarkupLine("[grey]Tip: Run[/] modulus generate-crud {0} --module {1} [grey]to scaffold CRUD.[/]",
            "Item", moduleName);

        return 0;
    }

    private static string DetectRootNamespace(string dir)
    {
        var slnx = SolutionHelper.FindSolution(Path.GetFullPath(dir));
        if (slnx is not null)
        {
            var solutionDir = Path.GetDirectoryName(slnx)!;
            var srcDir = Path.Combine(solutionDir, "src");
            if (Directory.Exists(srcDir))
            {
                // Prefer the *.Api directory name — it carries the full
                // root namespace even when the solution directory name has
                // been shortened (e.g. modulus app Contoso.Shop lives in
                // a directory called Shop/).
                var apiDirs = Directory.GetDirectories(srcDir, "*.Api", SearchOption.AllDirectories);
                if (apiDirs.Length == 1)
                    return Path.GetFileName(apiDirs[0])[..^".Api".Length];
            }
            return Path.GetFileNameWithoutExtension(slnx);
        }
        return new DirectoryInfo(Path.GetFullPath(dir)).Name;
    }
}
