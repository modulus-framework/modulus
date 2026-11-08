using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-tenant-lifecycle</c>: adds the tenant-data lifecycle to a multi-tenant API app: the store module
/// (<c>src/Modules/{App}.Modules.TenantLifecycle/{App}.Modules.TenantLifecycle.Infrastructure</c>: the host's lifecycle context, its
/// design-time factory and <c>TenantLifecycleModule</c>), the host wiring (the module, file storage when the host has none,
/// <c>AddTenantLifecycle</c>, the export endpoints, the Admin grant of <c>tenant-data:export</c>), the connection string and a test
/// class. Refused for an app without multi-tenancy: the lifecycle acts on companies. Idempotent; existing files are never overwritten.
/// </summary>
internal sealed class AddTenantLifecycleCommand : Command<AddTenantLifecycleCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddTenantLifecycleCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var host = ApiHostFiles.ReadAll(app.ProgramCsPath);
        if (!host.Contains("AddMultiTenancy(", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The tenant data lifecycle acts on companies, so the app needs multi-tenancy. Generate the app with modulus app --multi-tenancy (--kind api --auth openiddict).");

        var hasFileStorage = host.Contains("FileStorage(", StringComparison.Ordinal) || host.Contains("UseFileStorageProvider", StringComparison.Ordinal);
        var hasAdminRole = UiAccessGates.HasAdminRole(host);
        var grantsAdmin = hasAdminRole && host.Contains("AddModulusAuthorization(", StringComparison.Ordinal);
        var provider = app.Modules.Select(m => m.DatabaseProvider).FirstOrDefault(p => NewAppCommand.KnownProviders.Contains(p)) ?? "SQLite";
        var model = new LifecycleModel
        {
            RootNamespace = app.RootNamespace,
            DbProvider = provider,
            TargetFramework = AppFramework.Resolve(app.ProgramCsPath),
        };

        var written = new List<string>();
        var hints = new List<string>();

        // ── Store module ────────────────────────────────────────────
        var moduleDir = Path.Combine(app.SolutionDir, "src", "Modules", model.ModuleNamespace);
        var infraName = $"{model.ModuleNamespace}.Infrastructure";
        var infraDir = Path.Combine(moduleDir, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        Write("tenantlifecycle/infrastructure.csproj", model, infraCsproj, written, app);
        Write("tenantlifecycle/AppTenantLifecycleDbContext", model, Path.Combine(infraDir, "AppTenantLifecycleDbContext.cs"), written, app);
        Write("tenantlifecycle/AppTenantLifecycleDbContextFactory", model, Path.Combine(infraDir, "AppTenantLifecycleDbContextFactory.cs"), written, app);
        Write("tenantlifecycle/TenantLifecycleModule", model, Path.Combine(infraDir, "TenantLifecycleModule.cs"), written, app);
        if (!Ux.DryRun)
            SolutionHelper.AddProject(app.SolutionPath, Path.GetRelativePath(app.SolutionDir, infraCsproj));

        // ── API host ────────────────────────────────────────────────
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var reference = Path.GetRelativePath(apiDir, infraCsproj).Replace('\\', '/');
        Edit(app.ApiProjectPath, t => WebhooksWiring.EnsureApiProjectReference(t, reference), written, app);
        var changed = Edit(ApiHostFiles.ModulesFile(app.ProgramCsPath), t => TenantLifecycleWiring.EnsureApiModule(t, model.ModuleNamespace), written, app);
        changed |= Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), t => TenantLifecycleWiring.EnsureApiServices(t, !hasFileStorage, grantsAdmin ? UiAccessGates.AdminRole : null), written, app);
        changed |= Edit(ApiHostFiles.FeatureEndpointsFile(app.ProgramCsPath), TenantLifecycleWiring.EnsureApiEndpoints, written, app);
        if (!changed && !host.Contains("AddTenantLifecycle(", StringComparison.Ordinal))
            hints.Add("Add modules.AddModule<TenantLifecycleModule>() to the AddModulus callback, builder.Services.AddTenantLifecycle(builder.Configuration) to Configuration/FeatureServicesSetup.cs and app.MapModulusTenantLifecycle() to Endpoints/FeatureEndpoints.cs.");

        if (hasAdminRole && !grantsAdmin)
            hints.Add("Grant tenant-data:export to the administrators: builder.Services.AddModulusAuthorization() and AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"tenant-data:export\")).");
        else if (!hasAdminRole)
            hints.Add("No Admin role in this host: callers need a \"permission\" claim of tenant-data:export to request an export.");
        if (hasFileStorage)
            hints.Add("The host already registers file storage; exports are written there. Check that its MaxUploadBytes covers the largest export.");

        Edit(Path.Combine(apiDir, "appsettings.json"), t => TenantLifecycleWiring.EnsureSettings(t, model.ConnectionString), written, app);
        if (grantsAdmin)
        {
            // Grants belong to a company: the first-boot grant above is written in the host context, which no company sees, so the
            // administrators of each new company get the export permission from their starting grants.
            Edit(Path.Combine(apiDir, "appsettings.json"), t => TenantLifecycleWiring.EnsureCompanyGrant(t, UiAccessGates.AdminRole, "tenant-data:export"), written, app);
            hints.Add("Companies that already exist keep the grants they have: give their administrators tenant-data:export through the authorization API (POST grants). New companies get it from CompanyProvisioning:Grants.");
        }

        // ── Tenant data of the modules ──────────────────────────────
        foreach (var module in app.Modules.Where(m => m.Entities.Count > 0))
            WireModuleTenantData(app, module, written, hints);

        // ── Tests ───────────────────────────────────────────────────
        var testsDir = Path.Combine(app.SolutionDir, "tests", $"{app.RootNamespace}.Tests");
        if (Directory.Exists(testsDir))
            Write("tenantlifecycle/TenantLifecycleTests", model, Path.Combine(testsDir, "TenantLifecycleTests.cs"), written, app);
        // The export round trip signs in as the Admin role, which holds the permission only when the host grants permissions.
        if (Directory.Exists(testsDir) && grantsAdmin)
            Write("tenantlifecycle/TenantLifecycleExportTests", model, Path.Combine(testsDir, "TenantLifecycleExportTests.cs"), written, app);

        AnsiConsole.MarkupLine("[green]✓[/] Tenant data lifecycle added");
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        AnsiConsole.MarkupLine("  [grey]Set TenantLifecycle:ExportEncryptionKey (32 random bytes, base64) and TenantLifecycle:CertificateSigningKey (32+ bytes) in the environment or user secrets. Exports are refused until they are set.[/]");
        if (!app.Modules.Any(m => m.Entities.Count > 0))
            AnsiConsole.MarkupLine("  [grey]No module has entities yet, so the lifecycle has no tenant data to act on. Add entities with generate-crud, then run this command again.[/]");
        return 0;
    }

    // Each module with entities gets a handler over its own context (export, count, delete), registered in the host.
    private void WireModuleTenantData(ModuleDiscovery.AppInventory app, ModuleDiscovery.ModuleSummary module, List<string> written, List<string> hints)
    {
        var infraName = $"{module.Namespace}.Infrastructure";
        var infraDir = Path.Combine(module.Directory, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        if (!File.Exists(infraCsproj))
        {
            hints.Add($"{module.Name}: no Infrastructure project, so no tenant-data handler was generated for it.");
            return;
        }

        var handlerType = $"{module.Name}TenantDataHandler";
        var handlerNamespace = $"{module.Namespace}.Infrastructure.TenantData";
        var entities = module.Entities
            .Select(e => new TenantDataEntity
            {
                Plural = CodeGen.Pluralize(e),
                Dataset = $"{module.Name.ToLowerInvariant()}.{CodeGen.Pluralize(e).ToLowerInvariant()}",
            })
            .ToList();
        var handler = new TenantDataHandlerModel
        {
            ModuleName = module.Name,
            ModuleNamespace = module.Namespace,
            Entities = entities,
        };
        Write("tenantlifecycle/TenantDataHandler", handler, Path.Combine(infraDir, "TenantData", handlerType + ".cs"), written, app);

        var version = Services.FrameworkVersion.Current;
        Edit(infraCsproj, t => TenantLifecycleWiring.EnsurePackageReference(t, version), written, app);
        Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), t => TenantLifecycleWiring.EnsureHandlerRegistration(t, handlerType, handlerNamespace), written, app);

        var testsDir = Path.Combine(app.SolutionDir, "tests", $"{app.RootNamespace}.Tests");
        if (Directory.Exists(testsDir))
        {
            var first = entities[0];
            Write("tenantlifecycle/TenantDataHandlerTests", new TenantDataHandlerModel
            {
                ModuleName = module.Name,
                ModuleNamespace = module.Namespace,
                RootNamespace = app.RootNamespace,
                Entities = entities,
                FirstRoute = $"/api/{module.Name.ToLowerInvariant()}/{first.Plural.ToLowerInvariant()}",
            }, Path.Combine(testsDir, $"{module.Name}TenantDataHandlerTests.cs"), written, app);
        }
    }

    internal sealed class TenantDataHandlerModel
    {
        public string ModuleName { get; init; } = "";
        public string ModuleNamespace { get; init; } = "";
        public List<TenantDataEntity> Entities { get; init; } = [];
        public string RootNamespace { get; init; } = "";
        public string FirstRoute { get; init; } = "";
        public string HandlerType => ModuleName + "TenantDataHandler";
        public string ModuleNameLower => ModuleName.ToLowerInvariant();
    }

    internal sealed class TenantDataEntity
    {
        public string Plural { get; init; } = "";
        public string Dataset { get; init; } = "";
    }

    internal sealed class LifecycleModel
    {
        public string RootNamespace { get; init; } = "";
        public string DbProvider { get; init; } = "SQLite";
        public string TargetFramework { get; init; } = AppFramework.Default;
        public string ModuleNamespace => $"{RootNamespace}.Modules.TenantLifecycle";
        public string FrameworkVersion => Services.FrameworkVersion.Current;
        public string EfProviderPackage => DbProviderInfo.Package(DbProvider);
        public string EfProviderVersion => DbProviderInfo.Version(DbProvider, TargetFramework);
        public string UseDbMethod => DbProviderInfo.UseMethod(DbProvider);
        public string ConnectionString => DbProviderInfo.ConnectionString(DbProvider, "TenantLifecycle");
    }

    private void Write<T>(string template, T model, string path, List<string> written, ModuleDiscovery.AppInventory app)
        where T : class
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        _templates.RenderToFile(template, model, path);
        written.Add(display);
    }

    // Applies a pure text edit; true when the file changed (and was written, unless --dry-run).
    private static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return false;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return false;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
        return true;
    }
}
