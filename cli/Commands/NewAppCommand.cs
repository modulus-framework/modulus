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

        [Description("Target framework: net10.0, the only one Modulus supports (the default).")]
        [CommandOption("--framework")]
        public string? Framework { get; init; }

        [Description("Auth provider: none, openiddict, auth0, authentik, casdoor, azuread, duende, keycloak, okta. Omit to be prompted.")]
        [CommandOption("--auth")]
        public string? Auth { get; init; }

        [Description("Include the example Catalog module. Off by default: a new app starts with no sample code.")]
        [CommandOption("--with-example")]
        public bool? WithExample { get; init; }

        [Description("Deprecated: the example module is off by default; use --with-example to include it.")]
        [CommandOption("--no-example")]
        public bool? NoExample { get; init; }

        [Description("Message broker: none, rabbitmq, kafka. Omit to be prompted.")]
        [CommandOption("--message-broker")]
        public string? MessageBroker { get; init; }

        [Description("Caching provider: inmemory, redis. Omit to be prompted.")]
        [CommandOption("--caching")]
        public string? Caching { get; init; }

        [Description("With --storage s3: the S3-compatible service (aws, wasabi, b2, r2, minio). Default aws. The generated settings start with its endpoint and region.")]
        [CommandOption("--s3-provider")]
        public string? S3Provider { get; init; }

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

        [Description("Migration engine: efcore (default, EF Core migrations), dbsh (SQL-first migrations via the dbsh tool). Omit to be prompted.")]
        [CommandOption("--migration-engine")]
        public string? MigrationEngine { get; init; }

        [Description("Backends for Frontends to generate, one deployable per client: web, mobile, partner (comma-separated), or none. Each is src/Bff/{App}.Bff.{Client}, proxies /api to the API and works with every supported auth server.")]
        [CommandOption("--bff")]
        public string? Bff { get; init; }

        [Description("Microservices: upstream services the BFFs call besides the API host, named after their modules (catalog=http://localhost:5201,orders). A name without an address is resolved through service discovery (https+http://orders). Needs --bff.")]
        [CommandOption("--services")]
        public string? Services { get; init; }

        [Description("Company = tenant: tenant resolution with membership checks, a tenant store module, tenant-owned entities and isolation tests. Needs --auth openiddict.")]
        [CommandOption("--multi-tenancy")]
        [DefaultValue(false)]
        public bool MultiTenancy { get; init; }

        [Description("Path to a local NuGet feed containing the Modulus.* packages (written as an active 'modulus-local' source in NuGet.config). Omit to leave only nuget.org configured.")]
        [CommandOption("--package-source")]
        public string? PackageSource { get; init; }
    }

    /// <summary>Valid provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownProviders = ["SQLite", "SqlServer", "PostgreSQL", "MySQL"];

    /// <summary>Valid message broker choices, in selection-menu order.</summary>
    internal static readonly string[] KnownMessageBrokers = ["none", "rabbitmq", "kafka"];

    /// <summary>Valid caching provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownCachingProviders = ["inmemory", "redis"];

    /// <summary>Valid storage provider choices, in selection-menu order.</summary>
    internal static readonly string[] KnownStorageProviders = ["local", "s3", "azureblobs"];

    /// <summary>The S3-compatible services the generated settings know how to start with (with <c>--storage s3</c>).</summary>
    internal static readonly string[] KnownS3Providers = ["aws", "wasabi", "b2", "r2", "minio"];

    /// <summary>Valid SignalR backplane choices, in selection-menu order.</summary>
    internal static readonly string[] KnownSignalRBackplanes = ["none", "redis", "azure"];

    /// <summary>Valid migration engine choices, in selection-menu order.</summary>
    internal static readonly string[] KnownMigrationEngines = ["efcore", "dbsh"];

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        RefuseRetiredOptions(ctx?.Remaining.Parsed);

        if (Ux.IsInteractive && !Ux.Quiet)
            AnsiConsole.Write(new Rule("[cyan]Modulus â€” create a new application[/]") { Border = BoxBorder.Rounded });

        // â”€â”€ Resolve args (interactive when missing, TTY-aware) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var name = s.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = Ux.AskRequired("App name [grey](e.g. MyApp or MyCompany.MyApp)[/]:",
                ciHint: "Pass the app name, e.g. `modulus app MyApp`.");
        var rootNs = ValidateAppName(name);

        var database = ResolveDatabase(s.Database);
        var auth = ResolveAuth(s.Auth);
        var multiTenancy = ResolveMultiTenancy(s.MultiTenancy, auth);

        // Resolve infrastructure options
        var messageBroker = ResolveMessageBroker(s.MessageBroker);
        var cachingProvider = ResolveCachingProvider(s.Caching);
        var storageProvider = ResolveStorageProvider(s.Storage);
        if (s.S3Provider is not null && storageProvider != "s3")
            throw new ArgumentException("--s3-provider needs --storage s3.");
        var s3Provider = storageProvider == "s3"
            ? ValidateChoice(s.S3Provider ?? "aws", KnownS3Providers, "S3 provider")
            : "aws";
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

        var migrationEngine = ResolveMigrationEngine(s.MigrationEngine);

        var bff = BffClients.Parse(s.Bff);
        if (bff.Count > 0 && auth == "none")
            throw new ArgumentException("--bff needs an auth server (--auth openiddict, keycloak, auth0, okta, azuread, duende, authentik or casdoor): a BFF authenticates its clients.");
        var bffServices = BffClients.ParseServices(s.Services);
        if (bffServices.Count > 0 && bff.Count == 0)
            throw new ArgumentException("--services lists the BFFs' upstream services; add --bff web,mobile,partner.");

        // The example Catalog module is opt-in (2.0.0): --with-example includes it, --no-example is accepted for compatibility.
        // Interactive runs are asked (default: no); CI runs get no example.
        bool noExample;
        if (s.WithExample is { } withExample)
        {
            noExample = !withExample;
        }
        else if (s.NoExample is { } noExampleFlag)
        {
            noExample = noExampleFlag;
        }
        else if (Ux.IsInteractive)
        {
            var include = Ux.Confirm("Include the example [cyan]Catalog[/] module?", nonInteractiveDefault: false);
            noExample = !include;
        }
        else
        {
            noExample = true;
        }

        var parts = rootNs.Split('.');
        var appName = parts[^1];
        var outputDir = Path.GetFullPath(s.Output ?? "./");
        var projectDir = Path.Combine(outputDir, appName);

        // â”€â”€ Target directory conflict â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
            TargetFramework = string.IsNullOrWhiteSpace(s.Framework) ? AppFramework.Default : ValidateChoice(s.Framework!, AppFramework.Supported, "framework"),
            NoExample = noExample,
            Auth = auth,
            MessageBroker = messageBroker,
            CachingProvider = cachingProvider,
            StorageProvider = storageProvider,
            S3Provider = s3Provider,
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
            MigrationEngine = migrationEngine,
            LocalPackageSource = s.PackageSource,
            Bff = bff,
            BffServices = bffServices,
            MultiTenancy = multiTenancy,
        };

        Ux.Status($"Scaffolding {appName}...", () => GenerateAll(projectDir, model));

        // â”€â”€ Summary â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        AnsiConsole.WriteLine();
        Ux.Success($"Created [cyan]{appName}[/] at [grey]{projectDir}[/]");
        if (Ux.DryRun)
            Ux.Warning("Dry-run: nothing was actually written.");
        if (AuthNote(auth) is { } authNote)
        {
            if (auth == "none")
                Ux.Warning(authNote);
            else
                Ux.Info(authNote);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
        AnsiConsole.MarkupLine("  [grey]cd[/] {0}", appName);
        if (storageProvider == "s3")
        {
            AnsiConsole.MarkupLine("  [grey]S3 storage:[/] fill the placeholders in src/API/{0}.Api/appsettings.json (Storage:Endpoint, Storage:Region, Storage:BucketName)", rootNs);
            AnsiConsole.MarkupLine("  [grey]and set the keys as environment variables:[/] Storage__AccessKey and Storage__SecretKey (never in the settings file)");
        }
        if (!Ux.DryRun)
        {
            AnsiConsole.MarkupLine("  [grey]dotnet restore[/]");
            AnsiConsole.MarkupLine("  [grey]dotnet run --project[/] src/API/{0}.Api", rootNs);
            foreach (var client in model.BffClientModels)
            {
                AnsiConsole.MarkupLine("  [grey]# {0} BFF (http://localhost:{1}):[/] [grey]dotnet run --project[/] src/Bff/{2}.Bff.{3}",
                    client.Name, client.Port, rootNs, char.ToUpperInvariant(client.Name[0]) + client.Name[1..]);
            }
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Then try:[/]");
        AnsiConsole.MarkupLine("  [grey]modulus add-module[/] Orders");
        AnsiConsole.MarkupLine("  [grey]modulus generate-crud[/] Order --module Orders  [grey]# API endpoints[/]");
        AnsiConsole.MarkupLine("  [grey]modulus list[/]  [grey]# see what's in this app[/]");
        if (model.UseDbsh)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[yellow]dbsh:[/] modules use SQL-first migrations. Install the tool " +
                "[grey]dotnet tool install --global dbsh[/], then author + apply with:");
            AnsiConsole.MarkupLine("  [grey]modulus migrate add[/] InitialCreate");
            AnsiConsole.MarkupLine("  [grey]modulus migrate update[/]");
        }

        return 0;
    }

    internal void GenerateAll(string projectDir, AppModel model)
    {
        var rootNs = model.RootNamespace;
        var projects = new List<string>();

        // â”€â”€ Host / API project â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        GenerateApiHost(projectDir, model, projects);

        // Backends for Frontends (--bff): one deployable per client type.
        for (var i = 0; i < model.Bff.Count; i++)
        {
            GenerateBffHost(projectDir, BffHostModel.For(model, model.Bff[i], i), projects);
        }

        // â”€â”€ Shared kernel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        GenerateShared(Path.Combine(projectDir, "src", "Shared"), model, projects);

        // â”€â”€ Identity backend (local token server needs users) â”€â”€â”€â”€â”€â”€
        if (model.UseOpenIddict)
        {
            GenerateIdentityModule(Path.Combine(projectDir, "src", "Modules", model.IdentityNamespace), model);
            projects.Add(IdentityProjectPath(model));
        }

        // Authorization store (permission grants, organization units) on its own database, with its migrations: production
        // cannot create the schema without them. Generated whenever the API host registers the store (the example permission).
        if (model.ExamplePermission is not null)
        {
            GenerateAuthorizationModule(Path.Combine(projectDir, "src", "Modules", model.AuthorizationNamespace), model);
            projects.Add(AuthorizationProjectPath(model));
        }

        // Tenant store (companies and memberships) for --multi-tenancy.
        if (model.MultiTenancy)
        {
            GenerateTenancyModule(Path.Combine(projectDir, "src", "Modules", model.TenancyNamespace), model);
            projects.Add(TenancyProjectPath(model));
        }

        // â”€â”€ Example Catalog module â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                TargetFramework = model.TargetFramework,
                MigrationEngine = model.MigrationEngine,
                EntityName = model.ExampleEntity,
                EntityNameLower = CodeGen.ToCamelCase(model.ExampleEntity),
                RouteName = CodeGen.Pluralize(model.ExampleEntity).ToLowerInvariant(),
                // With the identity backend the example API needs the permission the Admin role holds (see Program.cs).
                RequiredPermission = model.ExamplePermission,
                MultiTenant = model.MultiTenancy,
                UseAuth = model.UseAuth,
            };
            GenerateModule(modDir, modModel);
            projects.AddRange(ModuleProjectPaths(rootNs, model.ExampleModule));
        }

        // â”€â”€ Top-level test project â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var testDir = Path.Combine(projectDir, "tests", $"{rootNs}.Tests");
        _templates.RenderToFile("app/tests.csproj", model,
            Path.Combine(testDir, $"{rootNs}.Tests.csproj"));
        _templates.RenderToFile("app/AppTests", model,
            Path.Combine(testDir, "ModulePipelineSmokeTest.cs"));
        if (model.UseS3Storage)
            _templates.RenderToFile("app/StorageTests", model, Path.Combine(testDir, "StorageProviderTests.cs"));
        projects.Add($"tests/{rootNs}.Tests/{rootNs}.Tests.csproj");

        // â”€â”€ Solution file â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        SolutionHelper.Create(
            Path.Combine(projectDir, $"{model.AppName}.slnx"),
            model.AppName, projects);

        // â”€â”€ Directory.Build.props â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _templates.RenderToFile("app/Directory.Build.props", model,
            Path.Combine(projectDir, "Directory.Build.props"));
        // The SDK the app builds with: the 10.0 band, so a newer SDK installed beside it cannot change the build.
        _templates.RenderToFile("app/global.json", new { sdk_version = AppFramework.SdkVersion },
            Path.Combine(projectDir, "global.json"));

        // â”€â”€ Directory.Packages.props â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Disables CPM in the generated app and prevents inheriting a
        // parent repo's Directory.Packages.props (the SDK walks up the
        // tree to find one).  Generated csproj files use explicit
        // Versions, so CPM must be off.
        _templates.RenderToFile("app/Directory.Packages.props", model,
            Path.Combine(projectDir, "Directory.Packages.props"));

        // â”€â”€ .editorconfig â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _templates.RenderToFile("app/editorconfig", model,
            Path.Combine(projectDir, ".editorconfig"));

        // â”€â”€ NuGet.config â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _templates.RenderToFile("app/NuGet.config", model,
            Path.Combine(projectDir, "NuGet.config"));

        // â”€â”€ .gitignore â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _templates.RenderToFile("app/gitignore", model,
            Path.Combine(projectDir, ".gitignore"));

    }

    /// <summary>
    /// The API host's setup files, one per concern (the layout of a modular-monolith API host). Program.cs composes them; the CLI's feature
    /// wiring adds to FeatureServicesSetup and FeatureEndpoints, and the module wiring to ModuleRegistration.
    /// </summary>
    private void RenderApiSetup(string apiDir, AppModel model)
    {
        _templates.RenderToFile("app/api/ModuleRegistration", model, Path.Combine(apiDir, "Composition", "ModuleRegistration.cs"));
        _templates.RenderToFile("app/api/CoreServicesSetup", model, Path.Combine(apiDir, "Configuration", "CoreServicesSetup.cs"));
        _templates.RenderToFile("app/api/FeatureServicesSetup", model, Path.Combine(apiDir, "Configuration", "FeatureServicesSetup.cs"));
        if (model.UseOpenIddict)
            _templates.RenderToFile("app/api/TokenServerSetup", model, Path.Combine(apiDir, "Security", "TokenServerSetup.cs"));
        _templates.RenderToFile("app/api/AuthenticationSetup", model, Path.Combine(apiDir, "Security", "AuthenticationSetup.cs"));
        _templates.RenderToFile("app/api/OpenApiSetup", model, Path.Combine(apiDir, "OpenApi", "OpenApiSetup.cs"));
        _templates.RenderToFile("app/api/DatabaseStartup", model, Path.Combine(apiDir, "Hosting", "DatabaseStartup.cs"));
        _templates.RenderToFile("app/api/PipelineSetup", model, Path.Combine(apiDir, "Middleware", "PipelineSetup.cs"));
        _templates.RenderToFile("app/api/ApiEndpoints", model, Path.Combine(apiDir, "Endpoints", "ApiEndpoints.cs"));
        _templates.RenderToFile("app/api/FeatureEndpoints", model, Path.Combine(apiDir, "Endpoints", "FeatureEndpoints.cs"));
    }

    /// <summary>Generates the API host project for this app.</summary>
    private void GenerateApiHost(string projectDir, AppModel model, List<string> projects)
    {
        var rootNs = model.RootNamespace;
        var apiDir = Path.Combine(projectDir, "src", "API", $"{rootNs}.Api");
        _templates.RenderToFile("app/api.csproj", model,
            Path.Combine(apiDir, $"{rootNs}.Api.csproj"));
        _templates.RenderToFile("app/Program", model,
            Path.Combine(apiDir, "Program.cs"));
        RenderApiSetup(apiDir, model);
        _templates.RenderToFile("app/appsettings.json", model,
            Path.Combine(apiDir, "appsettings.json"));
        _templates.RenderToFile("app/appsettings.Development.json", model,
            Path.Combine(apiDir, "appsettings.Development.json"));
        if (model.UseOpenIddict)
            _templates.RenderToFile("app/appsettings.Testing.json", model,
                Path.Combine(apiDir, "appsettings.Testing.json"));
        _templates.RenderToFile("app/launchSettings.json", model,
            Path.Combine(apiDir, "Properties", "launchSettings.json"));
        if (model.UseSecurityGuard)
            _templates.RenderToFile("app/loosening-allowlist.json", model,
                Path.Combine(apiDir, "Security", "loosening-allowlist.json"));
        projects.Add($"src/API/{rootNs}.Api/{rootNs}.Api.csproj");
    }

    /// <summary>
    /// Generates one BFF host (<c>src/Bff/{App}.Bff.{Client}</c>): the client's own edge with its authentication, edge rules,
    /// YARP passthrough of <c>/api</c> to the API and, with the example module, an aggregate <c>/home</c> endpoint.
    /// </summary>
    internal void GenerateBffHost(string projectDir, BffHostModel bff, List<string> projects)
    {
        var dir = Path.Combine(projectDir, "src", "Bff", bff.ProjectName);
        _templates.RenderToFile("bff/bff.csproj", bff, Path.Combine(dir, $"{bff.ProjectName}.csproj"));
        _templates.RenderToFile("bff/Program", bff, Path.Combine(dir, "Program.cs"));
        // Program.cs composes one setup file per concern (the layout of a BFF host).
        _templates.RenderToFile("bff/BffCoreSetup", bff, Path.Combine(dir, "Configuration", "BffCoreSetup.cs"));
        _templates.RenderToFile("bff/SessionAuthSetup", bff, Path.Combine(dir, "Security", "SessionAuthSetup.cs"));
        _templates.RenderToFile("bff/PipelineSetup", bff, Path.Combine(dir, "Middleware", "PipelineSetup.cs"));
        _templates.RenderToFile("bff/BffEndpoints", bff, Path.Combine(dir, "Endpoints", "BffEndpoints.cs"));
        _templates.RenderToFile("bff/HealthChecksSetup", bff, Path.Combine(dir, "Observability", "HealthChecksSetup.cs"));
        _templates.RenderToFile("bff/appsettings.json", bff, Path.Combine(dir, "appsettings.json"));
        _templates.RenderToFile("bff/appsettings.Development.json", bff, Path.Combine(dir, "appsettings.Development.json"));
        _templates.RenderToFile("bff/launchSettings.json", bff, Path.Combine(dir, "Properties", "launchSettings.json"));
        _templates.RenderToFile("bff/ApiClients", bff, Path.Combine(dir, "ApiClients", "ApiClientRegistration.cs"));
        foreach (var module in bff.Modules)
            Ux.WriteFile(Path.Combine(dir, "ApiClients", $"{module.ModuleName}Api.cs"), BffApiClients.RenderModule(_templates, module));
        if (bff.HasExample)
            _templates.RenderToFile("bff/HomeEndpoints", bff, Path.Combine(dir, "Endpoints", "HomeEndpoints.cs"));
        projects.Add(bff.SolutionPath);
    }

    /// <summary>The identity module's project in the <c>.slnx</c> (it has an Infrastructure project only).</summary>
    internal static string IdentityProjectPath(AppModel model)
        => $"src/Modules/{model.IdentityNamespace}/{model.IdentityNamespace}.Infrastructure/{model.IdentityNamespace}.Infrastructure.csproj";

    /// <summary>
    /// Generates the identity backend that <c>--auth openiddict</c> needs: a module with an Infrastructure project
    /// only (no Domain/Application/Presentation, nothing to put there), so <c>modulus migrate</c> finds its
    /// context like any module's, while <c>generate-crud</c> never mistakes it for a business module.
    /// </summary>
    internal static string TenancyProjectPath(AppModel model)
        => $"src/Modules/{model.TenancyNamespace}/{model.TenancyNamespace}.Infrastructure/{model.TenancyNamespace}.Infrastructure.csproj";

    internal static string AuthorizationProjectPath(AppModel model)
        => $"src/Modules/{model.AuthorizationNamespace}/{model.AuthorizationNamespace}.Infrastructure/{model.AuthorizationNamespace}.Infrastructure.csproj";

    /// <summary>The authorization store module: the application's context over the framework's organization-aware store, its design-time factory and its registration.</summary>
    internal void GenerateAuthorizationModule(string moduleDir, AppModel model)
    {
        var infraDir = Path.Combine(moduleDir, $"{model.AuthorizationNamespace}.Infrastructure");
        _templates.RenderToFile("authorization/infrastructure.csproj", model,
            Path.Combine(infraDir, $"{model.AuthorizationNamespace}.Infrastructure.csproj"));
        _templates.RenderToFile("authorization/AppAuthorizationDbContext", model, Path.Combine(infraDir, "AppAuthorizationDbContext.cs"));
        _templates.RenderToFile("authorization/AppAuthorizationDbContextFactory", model, Path.Combine(infraDir, "AppAuthorizationDbContextFactory.cs"));
        _templates.RenderToFile("authorization/AuthorizationStoreRegistration", model, Path.Combine(infraDir, "AuthorizationStoreRegistration.cs"));
    }

    /// <summary>The tenant store module of <c>--multi-tenancy</c>: companies and memberships, its design-time factory and seeding.</summary>
    internal void GenerateTenancyModule(string moduleDir, AppModel model)
    {
        var infraDir = Path.Combine(moduleDir, $"{model.TenancyNamespace}.Infrastructure");
        _templates.RenderToFile("tenancy/infrastructure.csproj", model,
            Path.Combine(infraDir, $"{model.TenancyNamespace}.Infrastructure.csproj"));
        _templates.RenderToFile("tenancy/TenancyModule", model, Path.Combine(infraDir, "TenancyModule.cs"));
        _templates.RenderToFile("tenancy/TenantStoreDbContextFactory", model, Path.Combine(infraDir, "TenantStoreDbContextFactory.cs"));
        _templates.RenderToFile("tenancy/TenancySeeding", model, Path.Combine(infraDir, "TenancySeeding.cs"));
    }

    /// <summary>
    /// <c>--multi-tenancy</c> is for an API host with the local token server: an external provider has no local users whose memberships could be seeded.
    /// </summary>
    internal static bool ResolveMultiTenancy(bool requested, string auth)
    {
        if (!requested)
            return false;
        if (auth != "openiddict")
            throw new ArgumentException("--multi-tenancy needs --auth openiddict: memberships are seeded for the local users (an external provider's users are not local).");
        return true;
    }

    internal void GenerateIdentityModule(string moduleDir, AppModel model)
    {
        var infraDir = Path.Combine(moduleDir, $"{model.IdentityNamespace}.Infrastructure");
        _templates.RenderToFile("identity/infrastructure.csproj", model,
            Path.Combine(infraDir, $"{model.IdentityNamespace}.Infrastructure.csproj"));
        _templates.RenderToFile("identity/AppIdentityDbContext", model,
            Path.Combine(infraDir, "AppIdentityDbContext.cs"));
        _templates.RenderToFile("identity/AppIdentityDbContextFactory", model,
            Path.Combine(infraDir, "AppIdentityDbContextFactory.cs"));
        _templates.RenderToFile("identity/IdentityModule", model,
            Path.Combine(infraDir, "IdentityModule.cs"));
        _templates.RenderToFile("identity/IdentitySeeding", model,
            Path.Combine(infraDir, "IdentitySeeding.cs"));
    }

    /// <summary>
    /// Validates + normalises a CLI-supplied choice against
    /// <paramref name="known"/> (case-insensitive). Templates switch on the
    /// canonical spelling; without this, e.g. <c>--database postgresql</c> or
    /// <c>--migration-engine Dbsh</c> silently fell through to the SQLite /
    /// efcore default.
    /// </summary>
    internal static string ValidateChoice(
        string provided, IEnumerable<string> known, string description)
    {
        if (!known.Contains(provided, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown {description} '{provided}'. Valid: {string.Join(", ", known)}.");

        return known.First(p =>
            string.Equals(p, provided, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveDatabase(string? provided)
    {
        var database = string.IsNullOrWhiteSpace(provided)
            ? Ux.SelectOrFallback(
                "Database provider?",
                KnownProviders,
                "SQLite")
            : provided;

        // Normalise casing (templates index by exact string).
        return ValidateChoice(database, KnownProviders, "database provider");
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
                AuthProviders.DisplayChoices[0]);
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
    /// Resolves the migration engine: interactive selection when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static string ResolveMigrationEngine(string? provided)
    {
        string engine;
        if (string.IsNullOrWhiteSpace(provided))
        {
            engine = Ux.SelectOrFallback(
                "Migration engine?",
                KnownMigrationEngines,
                "efcore");
        }
        else
        {
            engine = provided;
        }

        if (!KnownMigrationEngines.Contains(engine, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Unknown migration engine '{engine}'. Valid: {string.Join(", ", KnownMigrationEngines)}.");

        return KnownMigrationEngines.First(e => string.Equals(e, engine, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What to know about the chosen auth provider in a freshly generated app, or null when nothing needs saying.
    /// Endpoints require an authenticated user by default (the framework is secure by default), so an app with no
    /// authentication scheme answers every API call with a 500 (<c>none</c>: a warning). <c>openiddict</c> comes with
    /// an Identity module, so it works out of the box in Development; the note says what to do before production.
    /// A web app + API's API is also meant for external clients, which is why it matters most there.
    /// </summary>
    internal static string? AuthNote(string auth)
    {
        return auth switch
        {
            "none" =>
                "Auth is 'none': endpoints require an authenticated user and no authentication scheme is registered, so the API answers 500 " +
                "until you register one (AddAuthentication().AddJwtBearer(...)) or re-run with --auth openiddict or an external provider. " +
                "Call AllowAnonymous() in an endpoint's Configure() to open it.",
            "openiddict" =>
                " Auth is 'openiddict': an Identity module (users, roles, token store) was generated. In Development it creates an admin " +
                "(random password, logged once) and turns the password grant on, so a client can POST /connect/token and call the API with the " +
                "bearer token." +
                " Before production: modulus migrate add InitialCreate --module Identity, real signing certificates, " +
                "Identity:Seed:AdminEmail/AdminPassword from secrets, and Identity:AllowPasswordFlow only for trusted first-party clients.",
            _ => null,
        };
    }

    /// <summary>
    /// <c>--kind</c> and <c>--ui-engine</c> were removed with the UI scaffolding. The parser ignores unknown options, so a script that still
    /// asks for a web app would silently get an API; refuse it instead. <c>--kind api</c> stays harmless.
    /// </summary>
    internal static void RefuseRetiredOptions(ILookup<string, string?>? options)
    {
        if (options is null)
            return;
        foreach (var option in options)
        {
            var name = option.Key.TrimStart('-');
            var isKind = name.Equals("kind", StringComparison.OrdinalIgnoreCase);
            if (!isKind && !name.Equals("ui-engine", StringComparison.OrdinalIgnoreCase))
                continue;
            var harmless = isKind ? "api" : "none";
            if (option.Any(v => !string.Equals(v, harmless, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"--{name} is no longer supported: the CLI creates APIs only (a modular monolith, or microservices with --bff and --services).");
        }
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
    /// Generates a five-project module (Domain, Application, IntegrationEvents,
    /// Infrastructure, Presentation), each its own .csproj. DTOs live under
    /// <c>Application/Dtos</c>; integration events live in the IntegrationEvents
    /// project, which Application references. There is no separate Contracts or
    /// Tests project. Pass a
    /// <see cref="ModuleModel"/> with a blank <see cref="ModuleModel.EntityName"/>
    /// to create an empty module skeleton.
    /// </summary>
    internal void GenerateModule(string modDir, ModuleModel m)
    {
        // Non-null local: for a blank module this is "" and hasEntity is false,
        // so none of the entity-specific blocks below run.
        var entityName = m.EntityName ?? "";
        var hasEntity = !string.IsNullOrWhiteSpace(entityName);

        // â”€â”€ Domain layer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Application layer (commands/handlers/queries/DTOs/events) â”€â”€
        // The IntegrationEvents project is always generated (Application references it, even for a blank module).
        var eventsDir = Path.Combine(modDir, m.IntegrationEventsProject);
        _templates.RenderToFile("module/integrationevents.csproj", m,
            Path.Combine(eventsDir, $"{m.IntegrationEventsProject}.csproj"));

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
            if (m.CursorPaging)
            {
                _templates.RenderToFile("module/Application/GetCursorQuery", m,
                    Path.Combine(appDir, $"Get{m.EntityPlural}CursorQuery.cs"));
                _templates.RenderToFile("module/Application/GetCursorHandler", m,
                    Path.Combine(appDir, $"Get{m.EntityPlural}CursorHandler.cs"));
            }
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

            _templates.RenderToFile("module/Application/ModuleArea", m,
                Path.Combine(eventsDir, "IntegrationEvents", $"{m.ModuleName}Area.cs"));
            _templates.RenderToFile("module/Application/IntegrationEvent", m,
                Path.Combine(eventsDir, "IntegrationEvents", $"{entityName}CreatedIntegrationEvent.cs"));
            _templates.RenderToFile("module/Application/UpdatedIntegrationEvent", m,
                Path.Combine(eventsDir, "IntegrationEvents", $"{entityName}UpdatedIntegrationEvent.cs"));
            _templates.RenderToFile("module/Application/DeletedIntegrationEvent", m,
                Path.Combine(eventsDir, "IntegrationEvents", $"{entityName}DeletedIntegrationEvent.cs"));
        }

        // â”€â”€ Infrastructure layer (composition root) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var infraDir = Path.Combine(modDir, m.InfrastructureProject);
        _templates.RenderToFile("module/infrastructure.csproj", m,
            Path.Combine(infraDir, $"{m.InfrastructureProject}.csproj"));

        // The module's own DbContext (always present, even for a blank module).
        _templates.RenderToFile("module/Infrastructure/DbContext", m,
            Path.Combine(infraDir, $"{m.ModuleName}DbContext.cs"));

        // EF Core: design-time factory so `dotnet ef` / `modulus migrate` can
        // construct the context without the app's DI container. Rendered for
        // dbsh modules too â€” `dotnet ef migrations script` is the quickest way
        // to bootstrap the initial schema SQL to paste into a V001 migration.
        _templates.RenderToFile("module/Infrastructure/DbContextFactory", m,
            Path.Combine(infraDir, $"{m.ModuleName}DbContextFactory.cs"));

        if (m.UseDbsh)
        {
            // dbsh: per-module config + SQL migrations folder inside the
            // Infrastructure project (native dbsh layout, so running dbsh from
            // the module's Infrastructure directory discovers everything).
            _templates.RenderToFile("module/Infrastructure/dbsh.migration.json", m,
                Path.Combine(infraDir, "Database", "Config", "migration.json"));
            _templates.RenderToFile("module/Infrastructure/dbsh.local.json", m,
                Path.Combine(infraDir, "Database", "Config", "environments", "local.json"));
            Ux.WriteFile(Path.Combine(infraDir, "Database", "Migrations", ".gitkeep"), "");
        }

        if (hasEntity)
        {
            _templates.RenderToFile("module/Infrastructure/Repository", m,
                Path.Combine(infraDir, $"{entityName}Repository.cs"));
        }
        _templates.RenderToFile("module/Infrastructure/Module", m,
            Path.Combine(infraDir, $"{m.ModuleName}Module.cs"));

        // â”€â”€ Presentation layer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var presDir = Path.Combine(modDir, m.PresentationProject);
        _templates.RenderToFile("module/presentation.csproj", m,
            Path.Combine(presDir, $"{m.PresentationProject}.csproj"));
        if (hasEntity)
        {
            _templates.RenderToFile("module/Presentation/Endpoint", m,
                Path.Combine(presDir, $"{m.EntityPlural}Endpoint.cs"));
            if (m.CursorPaging)
                _templates.RenderToFile("module/Presentation/CursorEndpoint", m,
                    Path.Combine(presDir, $"{m.EntityPlural}CursorEndpoint.cs"));
        }
    }

    /// <summary>
    /// All five layer project paths (relative to solution root) for a module,
    /// used when registering projects in the .slnx file.
    /// </summary>
    internal static IEnumerable<string> ModuleProjectPaths(string rootNs, string moduleName)
    {
        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        string[] layers = ["Domain", "IntegrationEvents", "Application", "Infrastructure", "Presentation"];
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
