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
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        var moduleName = s.Name;
        var rootNs = s.App ?? DetectRootNamespace(s.Output ?? "./");
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        var outputBase = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputBase, "src", moduleNs);

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
            ModuleProject = $"{moduleNs}.csproj",
        };

        var appCmd = new NewAppCommand();
        appCmd.GenerateModule(projectDir, model, new AppModel
        {
            RootNamespace = rootNs,
            AppName = rootNs.Split('.')[^1],
        });

        // Add to solution if one exists nearby
        var slnx = SolutionHelper.FindSolution(outputBase);
        if (slnx is not null)
        {
            var relPath = Path.GetRelativePath(
                Path.GetDirectoryName(slnx)!,
                Path.Combine(projectDir, model.ModuleProject));
            SolutionHelper.AddProject(slnx, relPath);
            AnsiConsole.MarkupLine("[green]✓[/] Added to solution: [grey]{0}[/]", Path.GetFileName(slnx));
        }

        AnsiConsole.MarkupLine("[green]✓[/] Created module [cyan]{0}[/] at [grey]{1}[/]",
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
            var name = Path.GetFileNameWithoutExtension(slnx);
            return name;
        }
        return new DirectoryInfo(Path.GetFullPath(dir)).Name;
    }
}
