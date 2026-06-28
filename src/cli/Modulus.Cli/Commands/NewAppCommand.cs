using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class NewAppCommand : Command<NewAppCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [Description("Name of the application (e.g. MyApp or MyCompany.MyApp)")]
        [CommandArgument(0, "<name>")]
        public required string Name { get; init; }

        [Description("Output directory (default: current directory)")]
        [CommandOption("-o|--output")]
        [DefaultValue("./")]
        public string? Output { get; init; }

        [Description("Database provider: SQLite (default), SqlServer, PostgreSQL, MySQL")]
        [CommandOption("-d|--database")]
        [DefaultValue("SQLite")]
        public string Database { get; init; } = "SQLite";

        [Description("Skip generating the example Products module")]
        [CommandOption("--no-example")]
        [DefaultValue(false)]
        public bool NoExample { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        var parts = s.Name.Split('.');
        var appName = parts[^1];
        var rootNs = s.Name;
        var outputDir = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputDir, appName);

        if (Directory.Exists(projectDir))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Directory already exists:[/] {0}", projectDir);
            return 1;
        }

        Directory.CreateDirectory(projectDir);

        var model = new AppModel
        {
            RootNamespace = rootNs,
            AppName = appName,
            DbProvider = s.Database,
            NoExample = s.NoExample,
        };

        var projects = new List<string>();

        // ── Host project ──────────────────────────────────────────
        var hostDir = Path.Combine(projectDir, "src", $"{rootNs}.Host");
        _templates.RenderToFile("app/host.csproj", model,
            Path.Combine(hostDir, $"{rootNs}.Host.csproj"));
        _templates.RenderToFile("app/Program", model,
            Path.Combine(hostDir, "Program.cs"));
        _templates.RenderToFile("app/HostModule", model,
            Path.Combine(hostDir, "Modules", $"{appName}HostModule.cs"));
        _templates.RenderToFile("app/AppDbContext", model,
            Path.Combine(hostDir, "Infrastructure", "AppDbContext.cs"));
        _templates.RenderToFile("app/appsettings.json", model,
            Path.Combine(hostDir, "appsettings.json"));
        _templates.RenderToFile("app/appsettings.Development.json", model,
            Path.Combine(hostDir, "appsettings.Development.json"));
        projects.Add($"src/{rootNs}.Host/{rootNs}.Host.csproj");

        // ── Example Products module ───────────────────────────────
        if (!s.NoExample)
        {
            var modDir = Path.Combine(projectDir, "src", $"{rootNs}.Modules.Products");
            var modModel = new ModuleModel
            {
                RootNamespace = rootNs,
                ModuleName = "Products",
                ModuleNamespace = $"{rootNs}.Modules.Products",
                ModuleProject = $"{rootNs}.Modules.Products.csproj",
            };
            GenerateModule(modDir, modModel, model);
            projects.Add($"src/{rootNs}.Modules.Products/{rootNs}.Modules.Products.csproj");
        }

        // ── Test project ──────────────────────────────────────────
        var testDir = Path.Combine(projectDir, "tests", $"{rootNs}.Tests");
        _templates.RenderToFile("app/test.csproj", model,
            Path.Combine(testDir, $"{rootNs}.Tests.csproj"));
        projects.Add($"tests/{rootNs}.Tests/{rootNs}.Tests.csproj");

        // ── Solution file ─────────────────────────────────────────
        SolutionHelper.Create(
            Path.Combine(projectDir, $"{appName}.slnx"),
            appName, projects);

        // ── Directory.Build.props ─────────────────────────────────
        _templates.RenderToFile("app/Directory.Build.props", model,
            Path.Combine(projectDir, "Directory.Build.props"));

        // ── Summary ───────────────────────────────────────────────
        AnsiConsole.MarkupLine("[green]✓[/] Created [cyan]{0}[/] at [grey]{1}[/]", appName, projectDir);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
        AnsiConsole.MarkupLine("  [grey]cd[/] {0}", appName);
        AnsiConsole.MarkupLine("  [grey]dotnet restore[/]");
        AnsiConsole.MarkupLine("  [grey]dotnet run --project[/] src/{0}.Host", rootNs);

        return 0;
    }

    /// <summary>
    /// Generates a module project with Domain/Application/Infrastructure layers.
    /// </summary>
    internal void GenerateModule(string modDir, ModuleModel modModel, AppModel appModel)
    {
        _templates.RenderToFile("module/module.csproj", modModel,
            Path.Combine(modDir, modModel.ModuleProject));

        // Module class
        _templates.RenderToFile("module/Module", modModel,
            Path.Combine(modDir, $"{modModel.ModuleName}Module.cs"));

        // Domain layer
        var domainDir = Path.Combine(modDir, "Domain");
        _templates.RenderToFile("module/Entity", new
        {
            modModel.RootNamespace,
            modModel.ModuleNamespace,
            modModel.ModuleName,
            EntityName = "Product",
            EntityNameLower = "product",
        }, Path.Combine(domainDir, "Product.cs"));
        _templates.RenderToFile("module/IRepository", new
        {
            modModel.RootNamespace,
            modModel.ModuleNamespace,
            modModel.ModuleName,
            EntityName = "Product",
            EntityNameLower = "product",
        }, Path.Combine(domainDir, "IProductRepository.cs"));

        // Application layer
        var appLayerDir = Path.Combine(modDir, "Application");
        _templates.RenderToFile("module/Dto", new
        {
            modModel.ModuleNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
        }, Path.Combine(appLayerDir, "Dtos", "ProductDto.cs"));
        _templates.RenderToFile("module/CreateCommand", new
        {
            modModel.ModuleNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
        }, Path.Combine(appLayerDir, "CreateProductCommand.cs"));
        _templates.RenderToFile("module/CreateHandler", new
        {
            modModel.ModuleNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
            InterfaceName = "IProductRepository",
            CreateCommandName = "CreateProductCommand",
        }, Path.Combine(appLayerDir, "CreateProductHandler.cs"));
        _templates.RenderToFile("module/GetAllQuery", new
        {
            modModel.ModuleNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
        }, Path.Combine(appLayerDir, "GetProductsQuery.cs"));
        _templates.RenderToFile("module/GetAllHandler", new
        {
            modModel.ModuleNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
            InterfaceName = "IProductRepository",
        }, Path.Combine(appLayerDir, "GetProductsHandler.cs"));

        // Infrastructure layer
        var infraDir = Path.Combine(modDir, "Infrastructure");
        _templates.RenderToFile("module/Repository", new
        {
            modModel.ModuleNamespace,
            modModel.RootNamespace,
            EntityName = "Product",
            EntityNameLower = "product",
            InterfaceName = "IProductRepository",
        }, Path.Combine(infraDir, "ProductRepository.cs"));
    }
}
