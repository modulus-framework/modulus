using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class NewModuleCommand : Command<NewModuleCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Name of the module (e.g. Catalog, Orders). Omit to be prompted.")]
        [CommandArgument(0, "[name]")]
        public string? Name { get; init; }

        [Description("Root namespace of the application (e.g. MyApp). Auto-detected when omitted.")]
        [CommandOption("--app")]
        public string? App { get; init; }

        [Description("Output directory")]
        [CommandOption("-o|--output")]
        [DefaultValue("./")]
        public string? Output { get; init; }

        [Description("Database provider: SQLite (default), SqlServer, PostgreSQL, MySQL")]
        [CommandOption("-d|--database")]
        public string? Database { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        var moduleName = !string.IsNullOrWhiteSpace(s.Name)
            ? CodeGen.ValidateIdentifier(s.Name, "Module")
            : Ux.AskRequired("Module name [grey](e.g. Catalog, Orders)[/]:",
                ciHint: "Pass the module name, e.g. `modulus module Catalog`.");

        var rootNs = s.App ?? DetectRootNamespace(s.Output ?? "./");
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        var outputBase = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputBase, "src", "Modules", moduleNs);

        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
        {
            if (!Ux.Confirm($"Module directory [cyan]{projectDir}[/] exists. Overwrite?", nonInteractiveDefault: false))
            {
                Ux.Error("Aborted.");
                return 1;
            }
            Ux.DeleteDirectory(projectDir);
        }

        var database = string.IsNullOrWhiteSpace(s.Database)
            ? Ux.SelectOrFallback("Database provider?", NewAppCommand.KnownProviders, "SQLite")
            : NewAppCommand.ValidateChoice(
                s.Database!, NewAppCommand.KnownProviders, "database provider");

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleName = moduleName,
            ModuleNamespace = moduleNs,
            DbProvider = database,
        };

        // Generate a blank layered module skeleton (no entity yet).
        var appCmd = new NewAppCommand();
        Ux.Status($"Scaffolding {moduleName} module...", () => appCmd.GenerateModule(projectDir, model));

        // Add all layer projects to the solution if one exists nearby.
        var slnx = SolutionHelper.FindSolution(outputBase);
        if (slnx is not null)
        {
            foreach (var rel in NewAppCommand.ModuleProjectPaths(rootNs, moduleName))
            {
                var absInSln = rel.Replace('\\', '/');
                SolutionHelper.AddProject(slnx, absInSln);
            }
            Ux.Success($"Added 4 projects to solution", Path.GetFileName(slnx));
        }

        AnsiConsole.WriteLine();
        Ux.Success($"Created layered module [cyan]{moduleName}[/] at [grey]{projectDir}[/]");
        if (Ux.DryRun) Ux.Warning("Dry-run: nothing was actually written.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Tip:[/] modulus generate-crud Item --module {0}", moduleName);

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
