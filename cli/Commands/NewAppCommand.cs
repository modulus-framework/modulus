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

        [Description("Skip generating the example Catalog module")]
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

        // ── Host / API project ─────────────────────────────────────
        var apiDir = Path.Combine(projectDir, "src", "API", $"{rootNs}.Api");
        _templates.RenderToFile("app/api.csproj", model,
            Path.Combine(apiDir, $"{rootNs}.Api.csproj"));
        _templates.RenderToFile("app/Program", model,
            Path.Combine(apiDir, "Program.cs"));
        _templates.RenderToFile("app/HostModule", model,
            Path.Combine(apiDir, "Modules", $"{appName}HostModule.cs"));
        _templates.RenderToFile("app/appsettings.json", model,
            Path.Combine(apiDir, "appsettings.json"));
        _templates.RenderToFile("app/appsettings.Development.json", model,
            Path.Combine(apiDir, "appsettings.Development.json"));
        projects.Add($"src/API/{rootNs}.Api/{rootNs}.Api.csproj");

        // ── Shared kernel ─────────────────────────────────────────
        GenerateShared(Path.Combine(projectDir, "src", "Shared"), model, projects);

        // ── Example Catalog module ─────────────────────────────────
        if (!s.NoExample)
        {
            var modNs = $"{rootNs}.Modules.{model.ExampleModule}";
            var modDir = Path.Combine(projectDir, "src", "Modules", modNs);
            var modModel = new ModuleModel
            {
                RootNamespace = rootNs,
                ModuleName = model.ExampleModule,
                ModuleNamespace = modNs,
                DbProvider = model.DbProvider,
                EntityName = model.ExampleEntity,
                EntityNameLower = ToCamel(model.ExampleEntity),
                RouteName = ToPlural(model.ExampleEntity).ToLowerInvariant(),
            };
            GenerateModule(modDir, modModel);
            projects.AddRange(ModuleProjectPaths(rootNs, model.ExampleModule));
        }

        // ── Top-level test project ────────────────────────────────
        var testDir = Path.Combine(projectDir, "tests", $"{rootNs}.Tests");
        _templates.RenderToFile("app/tests.csproj", model,
            Path.Combine(testDir, $"{rootNs}.Tests.csproj"));
        _templates.RenderToFile("app/AppTests", model,
            Path.Combine(testDir, "ModulePipelineSmokeTest.cs"));
        projects.Add($"tests/{rootNs}.Tests/{rootNs}.Tests.csproj");

        // ── Solution file ─────────────────────────────────────────
        SolutionHelper.Create(
            Path.Combine(projectDir, $"{appName}.slnx"),
            appName, projects);

        // ── Directory.Build.props ─────────────────────────────────
        _templates.RenderToFile("app/Directory.Build.props", model,
            Path.Combine(projectDir, "Directory.Build.props"));

        // ── Directory.Packages.props ──────────────────────────────
        // Disables CPM in the generated app and prevents inheriting a
        // parent repo's Directory.Packages.props (the SDK walks up the
        // tree to find one).  Generated csproj files use explicit
        // Versions, so CPM must be off.
        _templates.RenderToFile("app/Directory.Packages.props", model,
            Path.Combine(projectDir, "Directory.Packages.props"));

        // ── .editorconfig ─────────────────────────────────────────
        _templates.RenderToFile("app/editorconfig", model,
            Path.Combine(projectDir, ".editorconfig"));

        // ── .gitignore ────────────────────────────────────────────
        // Keeps build output, user files, SQLite databases, and — importantly —
        // secrets (secrets.json / appsettings.*.json) out of source control.
        _templates.RenderToFile("app/gitignore", model,
            Path.Combine(projectDir, ".gitignore"));

        // ── Summary ───────────────────────────────────────────────
        AnsiConsole.MarkupLine("[green]✓[/] Created [cyan]{0}[/] at [grey]{1}[/]", appName, projectDir);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
        AnsiConsole.MarkupLine("  [grey]cd[/] {0}", appName);
        AnsiConsole.MarkupLine("  [grey]dotnet restore[/]");
        AnsiConsole.MarkupLine("  [grey]dotnet run --project[/] src/API/{0}.Api", rootNs);

        return 0;
    }

    /// <summary>
    /// Generates the four Shared.* kernel projects directly under
    /// <c>src/Shared/</c>.
    /// </summary>
    internal void GenerateShared(string sharedDir, AppModel model, List<string> projects)
    {
        var rootNs = model.RootNamespace;

        _templates.RenderToFile("shared/shared.domain.csproj", model,
            Path.Combine(sharedDir, $"{rootNs}.Shared.Domain", $"{rootNs}.Shared.Domain.csproj"));
        projects.Add($"src/Shared/{rootNs}.Shared.Domain/{rootNs}.Shared.Domain.csproj");

        _templates.RenderToFile("shared/shared.application.csproj", model,
            Path.Combine(sharedDir, $"{rootNs}.Shared.Application", $"{rootNs}.Shared.Application.csproj"));
        projects.Add($"src/Shared/{rootNs}.Shared.Application/{rootNs}.Shared.Application.csproj");

        _templates.RenderToFile("shared/shared.infrastructure.csproj", model,
            Path.Combine(sharedDir, $"{rootNs}.Shared.Infrastructure", $"{rootNs}.Shared.Infrastructure.csproj"));
        projects.Add($"src/Shared/{rootNs}.Shared.Infrastructure/{rootNs}.Shared.Infrastructure.csproj");

        _templates.RenderToFile("shared/shared.presentation.csproj", model,
            Path.Combine(sharedDir, $"{rootNs}.Shared.Presentation", $"{rootNs}.Shared.Presentation.csproj"));
        projects.Add($"src/Shared/{rootNs}.Shared.Presentation/{rootNs}.Shared.Presentation.csproj");
    }

    /// <summary>
    /// Generates a 4-layer module (Domain, Application, Infrastructure,
    /// Presentation) — each its own .csproj. DTOs live under
    /// <c>Application/Dtos</c> and integration events under
    /// <c>Application/IntegrationEvents</c>; there are no separate
    /// Contracts / IntegrationEvents / Tests projects. Pass a
    /// <see cref="ModuleModel"/> with a blank <see cref="ModuleModel.EntityName"/>
    /// to create an empty module skeleton.
    /// </summary>
    internal void GenerateModule(string modDir, ModuleModel m)
    {
        // Non-null local: for a blank module this is "" and hasEntity is false,
        // so none of the entity-specific blocks below run.
        var entityName = m.EntityName ?? "";
        var hasEntity = !string.IsNullOrWhiteSpace(entityName);

        // ── Domain layer ──────────────────────────────────────────
        var domainDir = Path.Combine(modDir, m.DomainProject);
        _templates.RenderToFile("module/domain.csproj", m,
            Path.Combine(domainDir, $"{m.DomainProject}.csproj"));
        if (hasEntity)
        {
            _templates.RenderToFile("module/Domain/Entity", m,
                Path.Combine(domainDir, $"{entityName}.cs"));
            _templates.RenderToFile("module/Domain/IRepository", m,
                Path.Combine(domainDir, $"I{entityName}Repository.cs"));
        }

        // ── Application layer (commands/handlers/queries/DTOs/events) ──
        var appDir = Path.Combine(modDir, m.ApplicationProject);
        _templates.RenderToFile("module/application.csproj", m,
            Path.Combine(appDir, $"{m.ApplicationProject}.csproj"));

        // The module's own IUnitOfWork (always present, even for a blank module).
        _templates.RenderToFile("module/Application/IUnitOfWork", m,
            Path.Combine(appDir, "IUnitOfWork.cs"));

        if (hasEntity)
        {
            _templates.RenderToFile("module/Application/Dto", m,
                Path.Combine(appDir, "Dtos", $"{entityName}Dto.cs"));

            _templates.RenderToFile("module/Application/CreateCommand", m,
                Path.Combine(appDir, $"Create{entityName}Command.cs"));
            _templates.RenderToFile("module/Application/CreateHandler", m,
                Path.Combine(appDir, $"Create{entityName}Handler.cs"));
            _templates.RenderToFile("module/Application/GetAllQuery", m,
                Path.Combine(appDir, $"Get{ToPlural(entityName)}Query.cs"));
            _templates.RenderToFile("module/Application/GetAllHandler", m,
                Path.Combine(appDir, $"Get{ToPlural(entityName)}Handler.cs"));
            _templates.RenderToFile("module/Application/GetByIdQuery", m,
                Path.Combine(appDir, $"Get{entityName}ByIdQuery.cs"));
            _templates.RenderToFile("module/Application/GetByIdHandler", m,
                Path.Combine(appDir, $"Get{entityName}ByIdHandler.cs"));
            _templates.RenderToFile("module/Application/UpdateCommand", m,
                Path.Combine(appDir, $"Update{entityName}Command.cs"));
            _templates.RenderToFile("module/Application/UpdateHandler", m,
                Path.Combine(appDir, $"Update{entityName}Handler.cs"));
            _templates.RenderToFile("module/Application/DeleteCommand", m,
                Path.Combine(appDir, $"Delete{entityName}Command.cs"));
            _templates.RenderToFile("module/Application/DeleteHandler", m,
                Path.Combine(appDir, $"Delete{entityName}Handler.cs"));

            _templates.RenderToFile("module/Application/IntegrationEvent", m,
                Path.Combine(appDir, "IntegrationEvents", $"{entityName}CreatedIntegrationEvent.cs"));
        }

        // ── Infrastructure layer (composition root) ───────────────
        var infraDir = Path.Combine(modDir, m.InfrastructureProject);
        _templates.RenderToFile("module/infrastructure.csproj", m,
            Path.Combine(infraDir, $"{m.InfrastructureProject}.csproj"));

        // The module's own DbContext (always present, even for a blank module).
        _templates.RenderToFile("module/Infrastructure/DbContext", m,
            Path.Combine(infraDir, $"{m.ModuleName}DbContext.cs"));

        // Design-time factory so `dotnet ef` / `modulus migrate` can construct the
        // context without the app's DI container (see DesignTimeContext stubs).
        _templates.RenderToFile("module/Infrastructure/DbContextFactory", m,
            Path.Combine(infraDir, $"{m.ModuleName}DbContextFactory.cs"));

        if (hasEntity)
        {
            _templates.RenderToFile("module/Infrastructure/Repository", m,
                Path.Combine(infraDir, $"{entityName}Repository.cs"));
        }
        _templates.RenderToFile("module/Infrastructure/Module", m,
            Path.Combine(infraDir, $"{m.ModuleName}Module.cs"));

        // ── Presentation layer ────────────────────────────────────
        var presDir = Path.Combine(modDir, m.PresentationProject);
        _templates.RenderToFile("module/presentation.csproj", m,
            Path.Combine(presDir, $"{m.PresentationProject}.csproj"));
        if (hasEntity)
        {
            _templates.RenderToFile("module/Presentation/Controller", m,
                Path.Combine(presDir, $"{entityName}sController.cs"));
        }
    }

    /// <summary>
    /// All four layer project paths (relative to solution root) for a module,
    /// used when registering projects in the .slnx file.
    /// </summary>
    internal static IEnumerable<string> ModuleProjectPaths(string rootNs, string moduleName)
    {
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        string[] layers = ["Domain", "Application", "Infrastructure", "Presentation"];
        foreach (var layer in layers)
        {
            var proj = $"{moduleNs}.{layer}";
            yield return $"src/Modules/{moduleNs}/{proj}/{proj}.csproj";
        }
    }

    private static string ToPlural(string s) => s.EndsWith('s') ? s + "es" : s + "s";

    private static string ToCamel(string s) =>
        char.ToLowerInvariant(s[0]) + s[1..];
}
