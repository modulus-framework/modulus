using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class NewAppCommand : Command<NewAppCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Name of the application (e.g. MyApp or MyCompany.MyApp). Omit to be prompted.")]
        [CommandArgument(0, "[name]")]
        public string? Name { get; init; }

        [Description("Output directory (default: current directory)")]
        [CommandOption("-o|--output")]
        [DefaultValue("./")]
        public string? Output { get; init; }

        [Description("Database provider: SQLite, SqlServer, PostgreSQL, MySQL. Omit to be prompted.")]
        [CommandOption("-d|--database")]
        public string? Database { get; init; }

        [Description("Auth provider: none, openiddict, auth0, authentik, azuread, duende, keycloak, okta. Omit to be prompted.")]
        [CommandOption("--auth")]
        public string? Auth { get; init; }

        [Description("Skip the example Catalog module. Omit to be prompted (CI default: include).")]
        [CommandOption("--no-example")]
        public bool? NoExample { get; init; }

        [Description("Message broker: none, rabbitmq, kafka. Omit to be prompted.")]
        [CommandOption("--message-broker")]
        public string? MessageBroker { get; init; }

        [Description("Caching provider: inmemory, redis. Omit to be prompted.")]
        [CommandOption("--caching")]
        public string? Caching { get; init; }

        [Description("Storage provider: local, s3, azureblobs. Omit to be prompted.")]
        [CommandOption("--storage")]
        public string? Storage { get; init; }

        [Description("SignalR backplane: none, redis, azure. Omit to be prompted.")]
        [CommandOption("--signalr")]
        public string? SignalR { get; init; }

        [Description("Enable API versioning (default: true).")]
        [CommandOption("--enable-api-versioning")]
        public bool? EnableApiVersioning { get; init; }

        [Description("Enable rate limiting (default: true).")]
        [CommandOption("--enable-rate-limiting")]
        public bool? EnableRateLimiting { get; init; }

        [Description("Enable health checks (default: true).")]
        [CommandOption("--enable-health-checks")]
        public bool? EnableHealthChecks { get; init; }

        [Description("Enable feature flags (default: false).")]
        [CommandOption("--enable-feature-flags")]
        public bool? EnableFeatureFlags { get; init; }

        [Description("Enable CORS (default: true).")]
        [CommandOption("--enable-cors")]
        public bool? EnableCors { get; init; }

        [Description("Enable security headers (default: true).")]
        [CommandOption("--enable-security-headers")]
        public bool? EnableSecurityHeaders { get; init; }

        [Description("Enable HTTP idempotency (default: false).")]
        [CommandOption("--enable-idempotency")]
        public bool? EnableIdempotency { get; init; }

        [Description("Enable request correlation (default: true).")]
        [CommandOption("--enable-correlation")]
        public bool? EnableCorrelation { get; init; }

        [Description("Enable secrets guard (default: true).")]
        [CommandOption("--enable-secrets-guard")]
        public bool? EnableSecretsGuard { get; init; }

        [Description("Enable personal data protection (default: false).")]
        [CommandOption("--enable-personal-data-protection")]
        public bool? EnablePersonalDataProtection { get; init; }
    }

    /// <summary>Valid provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownProviders = ["SQLite", "SqlServer", "PostgreSQL", "MySQL"];

    /// <summary>Valid message broker choices, in selection-menu order.</summary>
    internal static readonly string[] KnownMessageBrokers = ["none", "rabbitmq", "kafka"];

    /// <summary>Valid caching provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownCachingProviders = ["inmemory", "redis"];

    /// <summary>Valid storage provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownStorageProviders = ["local", "s3", "azureblobs"];

    /// <summary>Valid SignalR backplane choices, in selection-menu order.</summary>
    internal static readonly string[] KnownSignalRBackplanes = ["none", "redis", "azure"];

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        if (Ux.IsInteractive && !Ux.Quiet)
            AnsiConsole.Write(new Rule("[cyan]Modulus — create a new application[/]") { Border = BoxBorder.Rounded });

        // ── Resolve args (interactive when missing, TTY-aware) ──────────
        var name = s.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = Ux.AskRequired("App name [grey](e.g. MyApp or MyCompany.MyApp)[/]:",
                ciHint: "Pass the app name, e.g. `modulus app MyApp`.");
        var rootNs = ValidateAppName(name);

        var database = ResolveDatabase(s.Database);

        var auth = ResolveAuth(s.Auth);

        // Resolve infrastructure options
        var messageBroker = ResolveMessageBroker(s.MessageBroker);
        var cachingProvider = ResolveCachingProvider(s.Caching);
        var storageProvider = ResolveStorageProvider(s.Storage);
        var signalRBackplane = ResolveSignalRBackplane(s.SignalR);

        // Resolve production hardening features
        var enableApiVersioning = ResolveFeature(s.EnableApiVersioning, true, "API versioning");
        var enableRateLimiting = ResolveFeature(s.EnableRateLimiting, true, "rate limiting");
        var enableHealthChecks = ResolveFeature(s.EnableHealthChecks, true, "health checks");
        var enableFeatureFlags = ResolveFeature(s.EnableFeatureFlags, false, "feature flags");
        var enableCors = ResolveFeature(s.EnableCors, true, "CORS");
        var enableSecurityHeaders = ResolveFeature(s.EnableSecurityHeaders, true, "security headers");
        var enableIdempotency = ResolveFeature(s.EnableIdempotency, false, "HTTP idempotency");
        var enableCorrelation = ResolveFeature(s.EnableCorrelation, true, "request correlation");
        var enableSecretsGuard = ResolveFeature(s.EnableSecretsGuard, true, "secrets guard");
        var enablePersonalDataProtection = ResolveFeature(s.EnablePersonalDataProtection, false, "personal data protection");

        // NoExample is tri-state: null = unspecified → prompt (interactive)
        // or default to include (CI). True/False are explicit user choices.
        bool noExample;
        if (s.NoExample is { } noExampleFlag)
        {
            noExample = noExampleFlag;
        }
        else if (Ux.IsInteractive)
        {
            var include = Ux.Confirm("Include the example [cyan]Catalog[/] module?", nonInteractiveDefault: true);
            noExample = !include;
        }
        else
        {
            noExample = false;
        }

        var parts = rootNs.Split('.');
        var appName = parts[^1];
        var outputDir = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputDir, appName);

        // ── Target directory conflict ───────────────────────────────────
        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
        {
            if (!Ux.Confirm($"Directory [cyan]{projectDir}[/] is not empty. Continue and overwrite?", nonInteractiveDefault: false))
            {
                Ux.Error("Aborted.");
                return 1;
            }
            Ux.Status($"Clearing {appName}/", () => Ux.DeleteDirectory(projectDir));
        }

        var model = new AppModel
        {
            RootNamespace = rootNs,
            AppName = appName,
            DbProvider = database,
            NoExample = noExample,
            Auth = auth,
            MessageBroker = messageBroker,
            CachingProvider = cachingProvider,
            StorageProvider = storageProvider,
            SignalRBackplane = signalRBackplane,
            EnableApiVersioning = enableApiVersioning,
            EnableRateLimiting = enableRateLimiting,
            EnableHealthChecks = enableHealthChecks,
            EnableFeatureFlags = enableFeatureFlags,
            EnableCors = enableCors,
            EnableSecurityHeaders = enableSecurityHeaders,
            EnableIdempotency = enableIdempotency,
            EnableCorrelation = enableCorrelation,
            EnableSecretsGuard = enableSecretsGuard,
            EnablePersonalDataProtection = enablePersonalDataProtection,
        };

        Ux.Status($"Scaffolding {appName}...", () => GenerateAll(projectDir, model));

        // ── Summary ────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        Ux.Success($"Created [cyan]{appName}[/] at [grey]{projectDir}[/]");
        if (Ux.DryRun)
            Ux.Warning("Dry-run: nothing was actually written.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
        AnsiConsole.MarkupLine("  [grey]cd[/] {0}", appName);
        if (!Ux.DryRun)
        {
            AnsiConsole.MarkupLine("  [grey]dotnet restore[/]");
            AnsiConsole.MarkupLine("  [grey]dotnet run --project[/] src/API/{0}.Api", rootNs);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Then try:[/]");
        AnsiConsole.MarkupLine("  [grey]modulus add-module[/] Orders");
        AnsiConsole.MarkupLine("  [grey]modulus generate-crud[/] Order --module Orders");
        AnsiConsole.MarkupLine("  [grey]modulus list[/]  [grey]# see what's in this app[/]");

        return 0;
    }

    private void GenerateAll(string projectDir, AppModel model)
    {
        var rootNs = model.RootNamespace;
        var projects = new List<string>();

        // ── Host / API project ─────────────────────────────────────
        var apiDir = Path.Combine(projectDir, "src", "API", $"{rootNs}.Api");
        _templates.RenderToFile("app/api.csproj", model,
            Path.Combine(apiDir, $"{rootNs}.Api.csproj"));
        _templates.RenderToFile("app/Program", model,
            Path.Combine(apiDir, "Program.cs"));
        _templates.RenderToFile("app/HostModule", model,
            Path.Combine(apiDir, "Modules", $"{model.AppName}HostModule.cs"));
        _templates.RenderToFile("app/appsettings.json", model,
            Path.Combine(apiDir, "appsettings.json"));
        _templates.RenderToFile("app/appsettings.Development.json", model,
            Path.Combine(apiDir, "appsettings.Development.json"));
        // Without launchSettings.json, `dotnet run` defaults to the Production
        // environment, which switches the database initialisation to Migrate mode
        // (throws on an empty schema). The Development profile keeps the default
        // dev experience working out of the box.
        _templates.RenderToFile("app/launchSettings.json", model,
            Path.Combine(apiDir, "Properties", "launchSettings.json"));
        projects.Add($"src/API/{rootNs}.Api/{rootNs}.Api.csproj");

        // ── Shared kernel ─────────────────────────────────────────
        GenerateShared(Path.Combine(projectDir, "src", "Shared"), model, projects);

        // ── Example Catalog module ─────────────────────────────────
        if (!model.NoExample)
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
                EntityNameLower = CodeGen.ToCamelCase(model.ExampleEntity),
                RouteName = CodeGen.Pluralize(model.ExampleEntity).ToLowerInvariant(),
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
            Path.Combine(projectDir, $"{model.AppName}.slnx"),
            model.AppName, projects);

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

        // ── NuGet.config ──────────────────────────────────────────
        _templates.RenderToFile("app/NuGet.config", model,
            Path.Combine(projectDir, "NuGet.config"));

        // ── .gitignore ────────────────────────────────────────────
        _templates.RenderToFile("app/gitignore", model,
            Path.Combine(projectDir, ".gitignore"));
    }

    private static string ResolveDatabase(string? provided)
    {
        string database;
        if (string.IsNullOrWhiteSpace(provided))
        {
            database = Ux.SelectOrFallback(
                "Database provider?",
                KnownProviders,
                "SQLite");
        }
        else
        {
            database = provided;
        }

        if (!KnownProviders.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown database provider '{database}'. Valid: {string.Join(", ", KnownProviders)}.");

        // Normalise casing (templates index by exact string).
        return KnownProviders.First(p => string.Equals(p, database, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the auth provider: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveAuth(string? provided)
    {
        string auth;
        if (string.IsNullOrWhiteSpace(provided))
        {
            var choices = AuthProviders.DisplayChoices;
            var picked = Ux.SelectOrFallback(
                "Authentication provider?",
                choices,
                "none");
            // Map the display label back to the key.
            auth = AuthProviders.All.First(p => p.DisplayName == picked).Key;
        }
        else
        {
            auth = provided;
        }

        if (AuthProviders.Find(auth) is null)
            throw new ArgumentException(
                $"Unknown auth provider '{auth}'. Valid: {string.Join(", ", AuthProviders.Keys)}.");

        return AuthProviders.Find(auth)!.Key;
    }

    /// <summary>
    /// Resolves the message broker: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveMessageBroker(string? provided)
    {
        string broker;
        if (string.IsNullOrWhiteSpace(provided))
        {
            broker = Ux.SelectOrFallback(
                "Message broker?",
                KnownMessageBrokers,
                "none");
        }
        else
        {
            broker = provided;
        }

        if (!KnownMessageBrokers.Contains(broker, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown message broker '{broker}'. Valid: {string.Join(", ", KnownMessageBrokers)}.");

        return KnownMessageBrokers.First(b => string.Equals(b, broker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the caching provider: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveCachingProvider(string? provided)
    {
        string caching;
        if (string.IsNullOrWhiteSpace(provided))
        {
            caching = Ux.SelectOrFallback(
                "Caching provider?",
                KnownCachingProviders,
                "inmemory");
        }
        else
        {
            caching = provided;
        }

        if (!KnownCachingProviders.Contains(caching, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown caching provider '{caching}'. Valid: {string.Join(", ", KnownCachingProviders)}.");

        return KnownCachingProviders.First(c => string.Equals(c, caching, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the storage provider: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveStorageProvider(string? provided)
    {
        string storage;
        if (string.IsNullOrWhiteSpace(provided))
        {
            storage = Ux.SelectOrFallback(
                "Storage provider?",
                KnownStorageProviders,
                "local");
        }
        else
        {
            storage = provided;
        }

        if (!KnownStorageProviders.Contains(storage, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown storage provider '{storage}'. Valid: {string.Join(", ", KnownStorageProviders)}.");

        return KnownStorageProviders.First(s => string.Equals(s, storage, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the SignalR backplane: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveSignalRBackplane(string? provided)
    {
        string signalR;
        if (string.IsNullOrWhiteSpace(provided))
        {
            signalR = Ux.SelectOrFallback(
                "SignalR backplane?",
                KnownSignalRBackplanes,
                "none");
        }
        else
        {
            signalR = provided;
        }

        if (!KnownSignalRBackplanes.Contains(signalR, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown SignalR backplane '{signalR}'. Valid: {string.Join(", ", KnownSignalRBackplanes)}.");

        return KnownSignalRBackplanes.First(s => string.Equals(s, signalR, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a boolean feature flag with interactive prompt when not supplied.
    /// </summary>
    private static bool ResolveFeature(bool? provided, bool defaultValue, string featureName)
    {
        if (provided.HasValue)
            return provided.Value;

        if (Ux.IsInteractive)
            return Ux.Confirm($"Enable {featureName}?", nonInteractiveDefault: defaultValue);

        return defaultValue;
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
                Path.Combine(appDir, $"Get{m.EntityPlural}Query.cs"));
            _templates.RenderToFile("module/Application/GetAllHandler", m,
                Path.Combine(appDir, $"Get{m.EntityPlural}Handler.cs"));
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
                Path.Combine(presDir, $"{m.EntityPlural}Controller.cs"));
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

    private static string ValidateAppName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Application name cannot be empty.");

        var parts = name.Split('.');
        foreach (var part in parts)
            CodeGen.ValidateIdentifier(part, "Application");

        return name;
    }
}
