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

        [Description("Migration engine: efcore (default, EF Core migrations), dbsh (SQL-first migrations via the dbsh tool). Omit to be prompted.")]
        [CommandOption("--migration-engine")]
        public string? MigrationEngine { get; init; }

        [Description("App kind: api (an API host, no UI is created) or web (a web app + the same API for external clients such as mobile or desktop apps). Omit to be prompted; implied web by --ui-modules, otherwise api when not interactive.")]
        [CommandOption("--kind")]
        public string? Kind { get; init; }

        [Description("Web apps only: UI modules to include: none, identity, permissions, tenancy, users, settings, auditlogging, notifications, files, or 'full' for a complete admin dashboard. Comma-separated or omit to be prompted.")]
        [CommandOption("--ui-modules")]
        public string? UiModules { get; init; }

        [Description("Web apps only: do not install the Tabler theme (keep Core's built-in layout or bring your own ITheme).")]
        [CommandOption("--no-theme")]
        [DefaultValue(false)]
        public bool NoTheme { get; init; }

        [Description("Path to a local NuGet feed containing the Cobytelabs.Modulus.* packages (written as an active 'modulus-local' source in NuGet.config). Omit to leave only nuget.org configured.")]
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

    /// <summary>Valid SignalR backplane choices, in selection-menu order.</summary>
    internal static readonly string[] KnownSignalRBackplanes = ["none", "redis", "azure"];

    /// <summary>Valid migration engine choices, in selection-menu order.</summary>
    internal static readonly string[] KnownMigrationEngines = ["efcore", "dbsh"];

    internal static readonly string[] KnownUiModules = [
        "identity", "permissions", "tenancy", "users",
        "settings", "auditlogging", "notifications", "files"
    ];

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

        var kind = ResolveKind(s.Kind, s.UiModules);

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

        var migrationEngine = ResolveMigrationEngine(s.MigrationEngine);

        // UI modules only exist in a web app; an API host is never asked.
        var uiModules = kind == AppKind.Web ? ResolveUiModules(s.UiModules) : [];
        if (WithSignInPage(kind, auth, uiModules) is { } withSignIn && withSignIn.Count != uiModules.Count)
        {
            AnsiConsole.MarkupLine("[grey]  A web app with the local token server also gets the Identity UI: it is the sign-in page.[/]");
            uiModules = withSignIn;
        }

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
            MigrationEngine = migrationEngine,
            Kind = kind,
            UiModules = uiModules,
            UseTablerTheme = kind == AppKind.Web && !s.NoTheme,
            LocalPackageSource = s.PackageSource,
        };

        Ux.Status($"Scaffolding {appName}...", () => GenerateAll(projectDir, model));

        // ── Summary ────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        Ux.Success($"Created [cyan]{appName}[/] ({kind.Label()}) at [grey]{projectDir}[/]");
        if (Ux.DryRun)
            Ux.Warning("Dry-run: nothing was actually written.");
        if (AuthNote(auth, kind) is { } authNote)
        {
            if (auth == "none")
                Ux.Warning(authNote);
            else
                Ux.Info(authNote);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
        AnsiConsole.MarkupLine("  [grey]cd[/] {0}", appName);
        if (!Ux.DryRun)
        {
            AnsiConsole.MarkupLine("  [grey]dotnet restore[/]");
            AnsiConsole.MarkupLine("  [grey]dotnet run --project[/] src/API/{0}.Api", rootNs);
            if (string.IsNullOrWhiteSpace(s.PackageSource))
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Note:[/] Cobytelabs.Modulus.* packages are not on nuget.org yet — " +
                    "wire a local feed in NuGet.config or re-run with [grey]--package-source[/].");
            }
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Then try:[/]");
        AnsiConsole.MarkupLine("  [grey]modulus add-module[/] Orders");
        AnsiConsole.MarkupLine(kind == AppKind.Web
            ? "  [grey]modulus generate-crud[/] Order --module Orders  [grey]# API endpoints + an admin page (--no-ui: API only)[/]"
            : "  [grey]modulus generate-crud[/] Order --module Orders  [grey]# API endpoints; this app has no UI[/]");
        if (kind == AppKind.Web && !noExample)
            AnsiConsole.MarkupLine("  [grey]modulus generate-crud[/] {0} --module {1}  [grey]# add the example module's admin page[/]",
                model.ExampleEntity, model.ExampleModule);
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
        _templates.RenderToFile("app/appsettings.json", model,
            Path.Combine(apiDir, "appsettings.json"));
        _templates.RenderToFile("app/appsettings.Development.json", model,
            Path.Combine(apiDir, "appsettings.Development.json"));
        // The integration tests boot the host in the Testing environment, where the token server needs the same throwaway
        // certificates Development uses (its base settings register none, on purpose).
        if (model.UseOpenIddict)
            _templates.RenderToFile("app/appsettings.Testing.json", model,
                Path.Combine(apiDir, "appsettings.Testing.json"));
        // Without launchSettings.json, `dotnet run` defaults to the Production
        // environment, which switches the database initialisation to Migrate mode
        // (throws on an empty schema). The Development profile keeps the default
        // dev experience working out of the box.
        _templates.RenderToFile("app/launchSettings.json", model,
            Path.Combine(apiDir, "Properties", "launchSettings.json"));
        projects.Add($"src/API/{rootNs}.Api/{rootNs}.Api.csproj");

        // ── Shared kernel ─────────────────────────────────────────
        GenerateShared(Path.Combine(projectDir, "src", "Shared"), model, projects);

        // ── Identity backend (local token server needs users) ──────
        if (model.UseOpenIddict)
        {
            GenerateIdentityModule(Path.Combine(projectDir, "src", "Modules", model.IdentityNamespace), model);
            projects.Add(IdentityProjectPath(model));
        }

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
                MigrationEngine = model.MigrationEngine,
                EntityName = model.ExampleEntity,
                EntityNameLower = CodeGen.ToCamelCase(model.ExampleEntity),
                RouteName = CodeGen.Pluralize(model.ExampleEntity).ToLowerInvariant(),
                // A web app's API exposes the entity's extension fields, filtered through the UI registry.
                HasApiExtraFields = model.UseUi,
                // With the identity backend the example API needs the permission the Admin role holds (see Program.cs).
                RequiredPermission = model.ExamplePermission,
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

        // ── UI Modules ───────────────────────────────────────────────
        if (model.UseUi)
        {
            WireUiModules(projectDir, model);
        }
    }

    /// <summary>The identity module's project in the <c>.slnx</c> (it has an Infrastructure project only).</summary>
    internal static string IdentityProjectPath(AppModel model)
        => $"src/Modules/{model.IdentityNamespace}/{model.IdentityNamespace}.Infrastructure/{model.IdentityNamespace}.Infrastructure.csproj";

    /// <summary>
    /// Generates the identity backend that <c>--auth openiddict</c> needs: a module with an Infrastructure project
    /// only (no Domain/Application/Presentation, nothing to put there), so <c>modulus migrate</c> finds its
    /// context like any module's, while <c>generate-crud</c> never mistakes it for a business module.
    /// </summary>
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

    private void WireUiModules(string projectDir, AppModel model)
    {
        var apiProject = Path.Combine(projectDir, "src", "API", $"{model.RootNamespace}.Api", $"{model.RootNamespace}.Api.csproj");
        var programCs = Path.Combine(projectDir, "src", "API", $"{model.RootNamespace}.Api", "Program.cs");

        if (!File.Exists(apiProject) || !File.Exists(programCs))
            return;

        // The localization services the UI foundation registers live in Platform (feature UI packages bring it
        // themselves, but a web app with no feature module has only UI.Core).
        ProjectFileService.EnsureCsprojPackageReference(
            apiProject, "Cobytelabs.Modulus.Platform", model.FrameworkVersion, Ux.DryRun);

        foreach (var module in ResolveWebInstall(model.UiModules, model.UseTablerTheme))
        {
            // Add package reference
            var command = $"dotnet add \"{apiProject}\" package \"{module.PackageId}\" --version {module.Version}";
            if (!Ux.DryRun)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet", command)
                {
                    WorkingDirectory = Path.GetDirectoryName(apiProject),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = new System.Diagnostics.Process { StartInfo = psi };
                proc.Start();
                proc.WaitForExit();
            }

            // Wire in Program.cs (shared idempotent surgery; also adds the
            // Razor Pages services the mapped endpoints require).
            var content = UiHostWiring.EnsureUiWiring(File.ReadAllText(programCs), module);

            if (!Ux.DryRun)
            {
                File.WriteAllText(programCs, content);

                // Admin UIs (Users, Settings, ...) require their permission, which the Admin role is granted.
                UiAccessGates.WriteSettings(programCs, module);
            }
        }

        if (!Ux.DryRun)
        {
            // Restore packages
            var restorePsi = new System.Diagnostics.ProcessStartInfo("dotnet", "restore")
            {
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var restoreProc = new System.Diagnostics.Process { StartInfo = restorePsi };
            restoreProc.Start();
            restoreProc.WaitForExit();
        }
    }

    /// <summary>
    /// What a web app installs and wires, in order: the UI foundation (<c>Modulus.UI.Core</c>, so a web app with no
    /// prebuilt module still has its Razor Pages, menu and layout), the chosen UI modules, then the Tabler theme when
    /// requested. Resolves through <see cref="UiModuleCatalog.Find"/> (ids like <c>identity</c> match the module name);
    /// the previous inline lookup compared against the catalog id (<c>Modulus.Identity</c>), never matched, and
    /// silently wired nothing.
    /// </summary>
    internal static IReadOnlyList<UiModuleDefinition> ResolveWebInstall(IEnumerable<string> uiModuleIds, bool withTheme)
    {
        var modules = uiModuleIds.Select(UiModuleCatalog.Find).ToList();
        modules.Insert(0, UiModuleCatalog.Find("Modulus.UI.Core"));
        if (withTheme)
            modules.Add(UiModuleCatalog.Find(UiCrudWiring.TablerThemeId));
        return modules;
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
    /// A web app's API is also meant for external clients, which is why it matters most there.
    /// </summary>
    internal static string? AuthNote(string auth, AppKind kind)
    {
        var clients = kind == AppKind.Web ? "The API is also for external clients (mobile, desktop), which need a way to sign in. " : "";
        return auth switch
        {
            "none" => clients +
                "Auth is 'none': endpoints require an authenticated user and no authentication scheme is registered, so the API answers 500 " +
                "until you register one (AddAuthentication().AddJwtBearer(...)) or re-run with --auth openiddict or an external provider. " +
                "Call AllowAnonymous() in an endpoint's Configure() to open it.",
            "openiddict" =>
                "Auth is 'openiddict': an Identity module (users, roles, token store) was generated. In Development it creates an admin " +
                "(random password, logged once) and turns the password grant on, so a client can POST /connect/token and call the API with the " +
                "bearer token." + (kind == AppKind.Web
                    ? " The web app also serves the authorization-code + PKCE flow (/connect/authorize, signing in through the Identity UI) " +
                      "for mobile, desktop and single-page clients; the redirect URIs they may use are Identity:Seed:RedirectUris."
                    : string.Empty) +
                " Before production: modulus migrate add InitialCreate --module Identity, real signing certificates, " +
                "Identity:Seed:AdminEmail/AdminPassword from secrets, and Identity:AllowPasswordFlow only for trusted first-party clients.",
            _ => null,
        };
    }

    private const string ApiChoice = "API: an API host, no UI";
    private const string WebChoice = "Web app + API: a UI, plus the API for external clients (mobile, desktop, other systems)";

    /// <summary>
    /// Resolves the app kind. An explicit <c>--kind</c> wins (and cannot be <c>api</c> together with UI modules);
    /// <c>--ui-modules</c> alone implies a web app, so existing command lines keep working; otherwise the user is asked,
    /// and a non-interactive run defaults to <c>api</c>.
    /// </summary>
    internal static AppKind ResolveKind(string? kind, string? uiModules)
    {
        var wantsUi = !string.IsNullOrWhiteSpace(uiModules)
            && !string.Equals(uiModules.Trim(), "none", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var parsed = AppKinds.Parse(kind);
            if (parsed == AppKind.Api && wantsUi)
                throw new ArgumentException(
                    "--ui-modules needs a web app: an API host creates no UI. Use --kind web, or drop --ui-modules.");
            return parsed;
        }

        if (wantsUi)
            return AppKind.Web;

        return Ux.SelectOrFallback("Application type?", [ApiChoice, WebChoice], ApiChoice) == WebChoice
            ? AppKind.Web
            : AppKind.Api;
    }

    /// <summary>
    /// A web app that signs users in with the local token server needs somewhere to do it: every page is behind the sign-in
    /// (<c>AddModulusPageAuthorization</c>) and the authorization-code flow sends users to the same page, so the Identity UI is
    /// part of the app. Returns <paramref name="uiModules"/> with <c>identity</c> added when it is missing.
    /// </summary>
    internal static IReadOnlyList<string> WithSignInPage(AppKind kind, string auth, IReadOnlyList<string> uiModules)
    {
        ArgumentNullException.ThrowIfNull(uiModules);
        if (kind != AppKind.Web
            || !string.Equals(auth, "openiddict", StringComparison.OrdinalIgnoreCase)
            || uiModules.Contains("identity", StringComparer.OrdinalIgnoreCase))
        {
            return uiModules;
        }

        return ["identity", .. uiModules];
    }

    /// <summary>
    /// Resolves the UI modules to include: interactive multi-select when not supplied,
    /// validation + normalisation when passed on the command line.
    /// </summary>
    private static IReadOnlyList<string> ResolveUiModules(string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            var parts = provided.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var normalized = new List<string>();
            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (lower == "none")
                {
                    continue;
                }
                if (lower == "full")
                {
                    return KnownUiModules;
                }
                if (KnownUiModules.Contains(lower, StringComparer.OrdinalIgnoreCase))
                {
                    normalized.Add(KnownUiModules.First(k => string.Equals(k, lower, StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    throw new ArgumentException($"Unknown UI module '{p}'. Valid: {string.Join(", ", KnownUiModules)} or 'full'.");
                }
            }
            return normalized;
        }

        if (Ux.IsInteractive)
        {
            var choices = KnownUiModules.Select(m => char.ToUpper(m[0]) + m[1..]).ToArray();
            var picked = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Prebuilt UI modules to include (space to select, enter to confirm; none is fine):")
                    .NotRequired()
                    .PageSize(10)
                    .AddChoices(choices)
                    .InstructionsText("[grey](Press [blue]space[/] to toggle, [blue]enter[/] to confirm)[/]"));
            return picked.Select(m => m.ToLowerInvariant()).ToArray();
        }

        return [];
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

        // EF Core: design-time factory so `dotnet ef` / `modulus migrate` can
        // construct the context without the app's DI container. Rendered for
        // dbsh modules too — `dotnet ef migrations script` is the quickest way
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

        // ── Presentation layer ────────────────────────────────────
        var presDir = Path.Combine(modDir, m.PresentationProject);
        _templates.RenderToFile("module/presentation.csproj", m,
            Path.Combine(presDir, $"{m.PresentationProject}.csproj"));
        if (hasEntity)
        {
            _templates.RenderToFile("module/Presentation/Endpoint", m,
                Path.Combine(presDir, $"{m.EntityPlural}Endpoint.cs"));
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
