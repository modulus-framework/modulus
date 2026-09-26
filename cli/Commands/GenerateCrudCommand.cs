using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Generates CRUD (Create, Read, Update, Delete) code for a domain entity
/// within an existing layered module, distributing the files across the
/// module's layer projects. Existing files are never overwritten — they are
/// reported as skipped instead.
/// </summary>
internal sealed class GenerateCrudCommand : Command<GenerateCrudCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Entity name (e.g. Product, Order). Omit to be prompted.")]
        [CommandArgument(0, "[entity]")]
        public string? Entity { get; init; }

        [Description("Module name or namespace (e.g. Catalog, MyApp.Modules.Catalog). Auto-detected if one module.")]
        [CommandOption("-m|--module")]
        public string? Module { get; init; }

        [Description("Also scaffold the HTMX admin page + sidebar entry in the host API project. A web app does this by default; an API-only app has no UI and refuses it.")]
        [CommandOption("--with-ui")]
        [DefaultValue(false)]
        public bool WithUi { get; init; }

        [Description("Web apps: scaffold only the API side (entity, handlers, endpoints), not the admin page.")]
        [CommandOption("--no-ui")]
        [DefaultValue(false)]
        public bool NoUi { get; init; }

        [Description("When scaffolding the UI: do not install the Tabler theme (keep Core's built-in layout or bring your own ITheme).")]
        [CommandOption("--no-theme")]
        [DefaultValue(false)]
        public bool NoTheme { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        var entity = !string.IsNullOrWhiteSpace(s.Entity)
            ? CodeGen.ValidateIdentifier(s.Entity, "Entity")
            : Ux.AskRequired("Entity name [grey](e.g. Product, Order)[/]:",
                ciHint: "Pass the entity name, e.g. `modulus generate-crud Product --module Catalog`.");

        var entityLower = CodeGen.ToCamelCase(entity);
        var plural = CodeGen.Pluralize(entity);
        var routeName = plural.ToLowerInvariant();

        var module = CodeGen.ResolveModule(s.Module);

        // A web app gets the admin page by default, an API-only app never does, and a host generated before app
        // kinds existed keeps the opt-in --with-ui. Decided up front so a refusal writes nothing.
        var kind = ModuleDiscovery.Inventory(Environment.CurrentDirectory)?.Kind;
        var withUi = AppKinds.ResolveCrudUi(kind, s.WithUi, s.NoUi);

        // Locate the layer project directories.
        var domainDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Domain");
        var appDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Application");
        var infraDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Infrastructure");
        var presDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Presentation");

        var model = new ModuleModel
        {
            RootNamespace = module.RootNamespace,
            ModuleNamespace = module.Namespace,
            ModuleName = module.Name,
            EntityName = entity,
            EntityNameLower = entityLower,
            RouteName = routeName,
            // For webapp+api kind, the UI pages live in the Web project; otherwise in the API project.
            UiNamespace = kind == AppKind.WebAppApi ? $"{module.RootNamespace}.Web" : $"{module.RootNamespace}.Api",
        };
        model.HasApiExtraFields = ExposesExtraFieldsInApi(kind, domainDir, appDir, presDir, entity, plural);

        // A host with the identity backend guards the API endpoints (and the admin page) with a permission the Admin role holds;
        // a host with no such role has nothing to grant it to, so its endpoints stay as open as the rest of that host.
        var host = ResolveHost(module);
        model.RequiredPermission = File.Exists(host.ProgramCs) && UiAccessGates.HasAdminRole(File.ReadAllText(host.ProgramCs))
            ? UiAccessGates.CrudPermission(module.Name, routeName)
            : null;

        var generated = new List<string>();
        var skipped = new List<string>();

        // ── Domain layer ──────────────────────────────────────────
        WriteIfMissing("module/Domain/Entity", model,
            Path.Combine(domainDir, $"{entity}.cs"), generated, skipped);
        WriteIfMissing("module/Domain/IRepository", model,
            Path.Combine(domainDir, $"I{entity}Repository.cs"), generated, skipped);

        // ── Application layer (DTOs + commands/handlers/queries) ────
        WriteIfMissing("module/Application/Dto", model,
            Path.Combine(appDir, "Dtos", $"{entity}Dto.cs"), generated, skipped);

        WriteIfMissing("module/Application/CreateCommand", model,
            Path.Combine(appDir, $"Create{entity}Command.cs"), generated, skipped);
        WriteIfMissing("module/Application/CreateHandler", model,
            Path.Combine(appDir, $"Create{entity}Handler.cs"), generated, skipped);

        WriteIfMissing("module/Application/GetAllQuery", model,
            Path.Combine(appDir, $"Get{plural}Query.cs"), generated, skipped);
        WriteIfMissing("module/Application/GetAllHandler", model,
            Path.Combine(appDir, $"Get{plural}Handler.cs"), generated, skipped);

        WriteIfMissing("module/Application/GetByIdQuery", model,
            Path.Combine(appDir, $"Get{entity}ByIdQuery.cs"), generated, skipped);
        WriteIfMissing("module/Application/GetByIdHandler", model,
            Path.Combine(appDir, $"Get{entity}ByIdHandler.cs"), generated, skipped);

        WriteIfMissing("module/Application/UpdateCommand", model,
            Path.Combine(appDir, $"Update{entity}Command.cs"), generated, skipped);
        WriteIfMissing("module/Application/UpdateHandler", model,
            Path.Combine(appDir, $"Update{entity}Handler.cs"), generated, skipped);

        WriteIfMissing("module/Application/DeleteCommand", model,
            Path.Combine(appDir, $"Delete{entity}Command.cs"), generated, skipped);
        WriteIfMissing("module/Application/DeleteHandler", model,
            Path.Combine(appDir, $"Delete{entity}Handler.cs"), generated, skipped);

        // ── Integration event (Application layer) ──────────────────
        WriteIfMissing("module/Application/IntegrationEvent", model,
            Path.Combine(appDir, "IntegrationEvents", $"{entity}CreatedIntegrationEvent.cs"),
            generated, skipped);

        // ── Infrastructure layer ──────────────────────────────────
        WriteIfMissing("module/Infrastructure/Repository", model,
            Path.Combine(infraDir, $"{entity}Repository.cs"), generated, skipped);

        // Wire repository + handler registration into the module class.
        var moduleFile = Path.Combine(infraDir, $"{module.Name}Module.cs");
        if (File.Exists(moduleFile))
        {
            var wired = EnsureModuleRegistrations(moduleFile, module.Namespace, entity);
            if (wired)
                generated.Add(CodeGen.Rel(infraDir, $"{module.Name}Module.cs (updated)"));
        }

        // Auto-wire the DbSet into the module's own DbContext.
        var dbContextFile = Path.Combine(infraDir, $"{module.Name}DbContext.cs");
        if (File.Exists(dbContextFile))
        {
            var wired = EnsureDbSetRegistration(dbContextFile, module.Namespace, entity, plural);
            if (wired)
                generated.Add(CodeGen.Rel(infraDir, $"{module.Name}DbContext.cs (updated)"));
        }

        // ── Presentation layer ────────────────────────────────────
        WriteIfMissing("module/Presentation/Endpoint", model,
            Path.Combine(presDir, $"{plural}Endpoint.cs"), generated, skipped);

        // The endpoints filter extension fields through the UI registry, which lives in Modulus.UI.Core.
        var presentationCsproj = Path.Combine(presDir, $"{module.Namespace}.Presentation.csproj");
        if (model.HasApiExtraFields && File.Exists(presentationCsproj)
            && ProjectFileService.EnsureCsprojPackageReference(
                presentationCsproj, "Cobytelabs.Modulus.UI.Core", model.FrameworkVersion, Ux.DryRun))
        {
            generated.Add(CodeGen.Rel(presDir, $"{module.Namespace}.Presentation.csproj (updated)"));
        }

        // ── Host UI companion (default for a web app, opt-in for an unmarked host) ──
        if (withUi)
            GenerateUiCompanion(module, model, host, kind, withTheme: !s.NoTheme, generated, skipped);
        else if (model.RequiredPermission is not null)
            EnsureApiPermission(module, model, host, generated);

        // ── Summary ───────────────────────────────────────────────
        AnsiConsole.MarkupLine("[green]✓[/] Generated CRUD for [cyan]{0}[/] in [grey]{1}[/]",
            entity, module.Name);
        foreach (var f in generated)
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", f);
        foreach (var f in skipped)
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", f);

        AnsiConsole.MarkupLine("[grey]  The DbSet + using were auto-wired into {0}DbContext.cs.[/]",
            module.Name);

        // dbsh modules: the EF model changed, but the schema is SQL-first —
        // remind the developer to author a migration for it.
        if (MigrateSupport.IsDbshModule(infraDir))
        {
            AnsiConsole.MarkupLine(
                "[yellow]![/] {0} uses dbsh (SQL-first schema) — author a migration for {1}:",
                module.Name, entity);
            AnsiConsole.MarkupLine(
                "[grey]    modulus migrate add Add{0} --module {1}  # then write the SQL under Database/Migrations/{1}/[/]",
                entity, module.Name);
        }

        return 0;
    }

    /// <summary>Rewrites an existing file through <paramref name="update"/> and lists it as updated when that changed it.</summary>
    private static void UpdateExisting(string path, string apiDir, List<string> generated, Func<string, string> update)
    {
        if (!File.Exists(path))
            return;

        var original = File.ReadAllText(path);
        var updated = update(original);
        if (updated == original)
            return;

        Ux.WriteFile(path, updated);
        generated.Add(CodeGen.Rel(apiDir, Path.GetRelativePath(apiDir, path).Replace(Path.DirectorySeparatorChar, '/') + " (updated)"));
    }

    /// <summary>The host project's files a CRUD set touches.</summary>
    private sealed record HostFiles(string ApiDir, string ApiCsproj, string ProgramCs);

    /// <summary>
    /// Prefers the discovered host paths (custom layouts); falls back to the generated-app convention when there is no
    /// <c>.slnx</c> (e.g. tests). For webapp+api kind, routes to the Web project; for others, routes to the API project.
    /// </summary>
    private static HostFiles ResolveHost(CodeGen.ModuleInfo module)
    {
        var inventory = ModuleDiscovery.Inventory(Environment.CurrentDirectory);
        var kind = inventory?.Kind;

        // For webapp+api, generate the UI in the Web project; for all other kinds (api, webapp, or unmarked), use the API project.
        var isWebProjectHost = kind == AppKind.WebAppApi;

        var hostDir = isWebProjectHost && inventory?.WebProjectPath is { Length: > 0 } wp1
                && File.Exists(wp1)
            ? Path.GetDirectoryName(wp1)!
            : isWebProjectHost && !string.IsNullOrEmpty(inventory?.WebProjectPath)
            ? Path.GetDirectoryName(inventory.WebProjectPath)!
            : inventory?.ApiProjectPath is { Length: > 0 } ap1
                && File.Exists(ap1)
            ? Path.GetDirectoryName(ap1)!
            : Path.Combine(Environment.CurrentDirectory, "src", isWebProjectHost ? "Web" : "API", $"{module.RootNamespace}.{(isWebProjectHost ? "Web" : "Api")}");

        var hostCsproj = isWebProjectHost && inventory?.WebProjectPath is { Length: > 0 } wp2
                && File.Exists(wp2)
            ? wp2
            : isWebProjectHost && !string.IsNullOrEmpty(inventory?.WebProjectPath)
            ? inventory.WebProjectPath!
            : inventory?.ApiProjectPath is { Length: > 0 } ap2
                && File.Exists(ap2)
            ? ap2
            : Path.Combine(hostDir, $"{module.RootNamespace}.{(isWebProjectHost ? "Web" : "Api")}.csproj");

        var programCs = isWebProjectHost && inventory?.WebProgramCsPath is { Length: > 0 } wp3
                && File.Exists(wp3)
            ? wp3
            : isWebProjectHost && !string.IsNullOrEmpty(inventory?.WebProgramCsPath)
            ? inventory.WebProgramCsPath!
            : inventory?.ProgramCsPath is { Length: > 0 } ap3
                && File.Exists(ap3)
            ? ap3
            : Path.Combine(hostDir, "Program.cs");

        return new HostFiles(hostDir, hostCsproj, programCs);
    }

    /// <summary>
    /// The <c>Program.cs</c> half of the API's permission when there is no admin page to carry it: the policy provider, the
    /// registry declaration and the Admin role's grant (all idempotent). The endpoints declare the permission themselves.
    /// </summary>
    private static void EnsureApiPermission(
        CodeGen.ModuleInfo module, ModuleModel model, HostFiles host, List<string> generated)
    {
        if (model.RequiredPermission is not { } permission || !File.Exists(host.ProgramCs))
            return;

        var original = File.ReadAllText(host.ProgramCs);
        var wired = UiCrudWiring.EnsurePagePermission(
            original, module.Name, permission, UiAccessGates.CrudPermissionDescription(model.EntityPlural ?? module.Name));
        if (wired == original)
            return;

        if (!Ux.DryRun)
            Ux.WriteFile(host.ProgramCs, wired);
        generated.Add(CodeGen.Rel(host.ApiDir, "Program.cs (updated)"));
    }

    /// <summary>
    /// Scaffolds the HTMX admin page + sidebar entry for the entity in the
    /// host project (Web SDK compiles <c>Pages/</c> with no csproj SDK
    /// changes; no new projects, so the <c>.slnx</c> is untouched):
    /// <list type="bullet">
    /// <item><c>Pages/{module}/{route}/Index.cshtml(.cs)</c> + table/form
    /// partials driving the module's mediator handlers (API project) or typed HTTP clients (Web project);</item>
    /// <item><c>Pages/_ViewImports.cshtml</c> + <c>_ViewStart.cshtml</c> shell
    /// chrome (once per host, mirroring the sidecar <c>Pages/</c> convention);</item>
    /// <item><c>Ui/{Module}UiModule.cs</c>, a <c>CustomUiModule</c> nav sidecar
    /// (the framework ships prebuilt UI modules only for its own areas —
    /// app-specific modules own theirs);</item>
    /// <item>the <c>Cobytelabs.Modulus.UI.Core</c> + <c>Platform</c> package
    /// references (localization services live in Platform) + host
    /// wiring (<c>UiHostWiring</c> shared bits, then the
    /// <c>AddUiModule&lt;&gt;</c> registration).</item>
    /// </list>
    /// Existing files are never overwritten (same <c>WriteIfMissing</c>
    /// semantics as the backend layers).
    /// </summary>
    private void GenerateUiCompanion(
        CodeGen.ModuleInfo module,
        ModuleModel model,
        HostFiles host,
        AppKind? kind,
        bool withTheme,
        List<string> generated,
        List<string> skipped)
    {
        var route = model.RouteName
            ?? throw new InvalidOperationException("RouteName must be set before UI generation.");

        var entityName = model.EntityName
            ?? throw new InvalidOperationException("EntityName must be set before UI generation.");
        var domainFile = Path.Combine(CodeGen.LayerDir(module.Directory, module.Namespace, "Domain"), $"{entityName}.cs");
        var appDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Application");
        model.HasExtraFields = SupportsExtraFields(domainFile, Path.Combine(appDir, $"Create{entityName}Command.cs"));
        // The edit form is a modal, and only a theme's layout hosts the modal container (Core's legacy shell has none).
        model.HasEditForm = withTheme
            && SupportsExtraFields(domainFile, Path.Combine(appDir, $"Update{entityName}Command.cs"));

        if (model.HasEditForm)
        {
            WriteIfMissing("module/Application/GetForEditQuery", model,
                Path.Combine(appDir, $"Get{entityName}ForEditQuery.cs"), generated, skipped);
            WriteIfMissing("module/Application/GetForEditHandler", model,
                Path.Combine(appDir, $"Get{entityName}ForEditHandler.cs"), generated, skipped);
        }

        var apiDir = host.ApiDir;
        var apiCsproj = host.ApiCsproj;
        var programCs = host.ProgramCs;

        if (!Directory.Exists(apiDir) || !File.Exists(apiCsproj))
            throw new InvalidOperationException(
                $"The admin UI needs the host API project at '{apiDir}'. " +
                "Run from the solution root of a generated app (or pass --no-ui).");

        // ── Razor Pages (route: /{module}/{route}) ────────────────
        var pageDir = Path.Combine(apiDir, "Pages", model.ModuleNameLower, route);
        var isWebProject = kind == AppKind.WebAppApi;
        var pageModelTemplate = isWebProject ? "ui/CrudIndexPageModel.Http" : "ui/CrudIndexPageModel";

        WriteIfMissing("ui/CrudIndexCshtml", model,
            Path.Combine(pageDir, "Index.cshtml"), generated, skipped);
        WriteIfMissing(pageModelTemplate, model,
            Path.Combine(pageDir, "Index.cshtml.cs"), generated, skipped);
        WriteIfMissing("ui/CrudFormPartial", model,
            Path.Combine(pageDir, "_CreateForm.cshtml"), generated, skipped);
        WriteIfMissing("ui/CrudTablePartial", model,
            Path.Combine(pageDir, "_Table.cshtml"), generated, skipped);
        if (model.HasEditForm)
            WriteIfMissing("ui/CrudEditFormPartial", model,
                Path.Combine(pageDir, "_EditForm.cshtml"), generated, skipped);

        // ── Shell chrome (once per host) ──────────────────────────
        WriteIfMissing("ui/ViewImports", model,
            Path.Combine(apiDir, "Pages", "_ViewImports.cshtml"), generated, skipped);
        WriteIfMissing("ui/ViewStart", model,
            Path.Combine(apiDir, "Pages", "_ViewStart.cshtml"), generated, skipped);

        // ── Nav sidecar (app-owned CustomUiModule) ────────────────
        var sidecar = Path.Combine(apiDir, "Ui", $"{module.Name}UiModule.cs");
        WriteIfMissing("ui/UiModule", model, sidecar, generated, skipped);

        // Files are never overwritten, so a set generated earlier needs its pieces brought in line: this entity's sidebar item (the
        // sidecar is written once per module, so a second entity would never reach the menu) and, where the host has a permission
        // to require, the guard on the page and its menu entry.
        UpdateExisting(sidecar, apiDir, generated,
            text => UiNavSidecar.EnsureItem(text, module.Name, model.EntityPlural ?? entityName, route, model.RequiredPermission));
        if (model.RequiredPermission is { } requiredPermission)
        {
            UpdateExisting(Path.Combine(pageDir, "Index.cshtml.cs"), apiDir, generated,
                text => UiAccessGates.EnsurePageGuard(text, requiredPermission));
        }

        // ── UI foundation references (offline csproj edits; the next
        // `dotnet build` restores them like every other scaffold step).
        // UI.Core brings the pages/nav contracts; Platform brings the
        // localization services the host wiring registers (every sidecar
        // lists Platform as a backend package for the same reason).
        var uiCoreAdded = ProjectFileService.EnsureCsprojPackageReference(
            apiCsproj, "Cobytelabs.Modulus.UI.Core", model.FrameworkVersion, Ux.DryRun);
        var platformAdded = ProjectFileService.EnsureCsprojPackageReference(
            apiCsproj, "Cobytelabs.Modulus.Platform", model.FrameworkVersion, Ux.DryRun);
        // The Tabler theme is the default look (opt out with --no-theme): generated pages already
        // resolve their layout through Context.GetThemeLayout(), so this only supplies the theme.
        var themeAdded = withTheme && ProjectFileService.EnsureCsprojPackageReference(
            apiCsproj, UiModuleCatalog.Find(UiCrudWiring.TablerThemeId).PackageId, model.FrameworkVersion, Ux.DryRun);
        if (uiCoreAdded || platformAdded || themeAdded)
            generated.Add(CodeGen.Rel(apiDir, $"{module.RootNamespace}.Api.csproj (updated)"));

        // ── webapp+api split: the Web project talks to the module over HTTP ──
        if (isWebProject)
        {
            EnsureWebApiClient(module, model, apiDir, apiCsproj, generated, skipped);
        }

        // ── Host wiring ───────────────────────────────────────────
        if (!File.Exists(programCs))
            throw new InvalidOperationException(
                $"The admin UI needs Program.cs at '{programCs}' to register the UI module.");

        var original = File.ReadAllText(programCs);
        // The namespace the wiring's usings/registrations anchor to is the UI
        // host's root namespace: the Web project for the split, the API host
        // otherwise.
        var wired = UiCrudWiring.EnsureHostWiring(
            original, model.UiNamespace, module.Name, withTheme, model.RequiredPermission,
            UiAccessGates.CrudPermissionDescription(model.EntityPlural ?? module.Name));

        if (wired != original)
        {
            if (!Ux.DryRun)
                Ux.WriteFile(programCs, wired);
            generated.Add(CodeGen.Rel(apiDir, "Program.cs (updated)"));
        }

        AnsiConsole.MarkupLine("[grey]  UI: /{0}/{1} + {2} sidebar entry (rebuild to restore new packages).[/]",
            model.ModuleNameLower, route, module.Name);
        AnsiConsole.MarkupLine(withTheme
            ? "[grey]  Theme: Tabler (AddTablerTheme). Pass --no-theme to keep Core's built-in layout.[/]"
            : "[grey]  Theme: none installed (--no-theme); pages use Core's built-in layout unless you register an ITheme.[/]");
    }

    /// <summary>
    /// webapp+api only: the Web project's typed client for this module's API endpoints
    /// (<c>ApiClients/{module_name}ApiClient.cs</c>), the module Application-project reference
    /// its page models compile against (they bind the module's DTOs), and the client's
    /// registration inside the Web project's <c>AddModuleApiClients</c>. Idempotent like
    /// everything else here: the client file is never overwritten and the other two steps
    /// no-op when already present.
    /// </summary>
    private void EnsureWebApiClient(
        CodeGen.ModuleInfo module,
        ModuleModel model,
        string webDir,
        string webCsproj,
        List<string> generated,
        List<string> skipped)
    {
        WriteIfMissing("ui/ModuleApiClient", model,
            Path.Combine(webDir, "ApiClients", $"{module.Name}ApiClient.cs"), generated, skipped);

        var applicationCsproj = Path.Combine(
            CodeGen.LayerDir(module.Directory, module.Namespace, "Application"),
            $"{module.Namespace}.Application.csproj");
        if (File.Exists(applicationCsproj) &&
            ProjectFileService.EnsureCsprojProjectReference(webCsproj, applicationCsproj, Ux.DryRun))
        {
            generated.Add(CodeGen.Rel(webDir, $"{Path.GetFileName(webCsproj)} (updated)"));
        }

        var apiClientExtensions = Path.Combine(webDir, "ApiClientExtensions.cs");
        if (!File.Exists(apiClientExtensions))
            return;

        UpdateExisting(apiClientExtensions, webDir, generated, text =>
        {
            if (text.Contains($"<{module.Name}ApiClient>", StringComparison.Ordinal))
                return text;

            var registration =
                $"        services.AddModulusHttpClient<{module.Name}ApiClient>()\n" +
                "            .AddHttpMessageHandler<TokenRelayHandler>();\n";
            var anchor = "        return services;";
            var index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0)
                return text;

            return text[..index] + registration + "\n" + text[index..];
        });
    }

    /// <summary>
    /// Whether the generated API exposes the entity's extension fields (through <c>EntityApiFields</c>, so callers only see and
    /// set what the registry lets them). Only a web app has the registry, and only a <i>fresh</i> set: files are never
    /// overwritten, so if any API file exists already the rest would not match a DTO or endpoint that predates this.
    /// </summary>
    internal static bool ExposesExtraFieldsInApi(
        AppKind? kind, string domainDir, string appDir, string presDir, string entity, string plural)
    {
        if (kind is not (AppKind.WebApp or AppKind.WebAppApi))
        {
            return false;
        }

        string[] apiFiles =
        [
            Path.Combine(appDir, "Dtos", $"{entity}Dto.cs"),
            Path.Combine(appDir, $"Get{plural}Handler.cs"),
            Path.Combine(appDir, $"Get{entity}ByIdHandler.cs"),
            Path.Combine(presDir, $"{plural}Endpoint.cs"),
        ];

        var entityFile = Path.Combine(domainDir, $"{entity}.cs");
        return apiFiles.All(f => !File.Exists(f))
            && SupportsExtraFields(entityFile, Path.Combine(appDir, $"Create{entity}Command.cs"))
            && SupportsExtraFields(entityFile, Path.Combine(appDir, $"Update{entity}Command.cs"));
    }

    /// <summary>
    /// Whether the entity and a create or update command on disk support extension fields. A file that was just
    /// generated always does; one that existed before (never overwritten) does only if it already mentions the
    /// marker, so a CRUD set generated earlier still gets a UI that compiles against it.
    /// </summary>
    internal static bool SupportsExtraFields(string entityFile, string commandFile)
        => Mentions(entityFile, "IHasExtraProperties") && Mentions(commandFile, "ExtraProperties");

    // A missing file is one this run generates (or, on a dry run, would generate) from the current template.
    private static bool Mentions(string file, string text)
        => !File.Exists(file) || File.ReadAllText(file).Contains(text, StringComparison.Ordinal);

    /// <summary>
    /// Renders <paramref name="templatePath"/> to <paramref name="outputPath"/>
    /// only when the file does not already exist; otherwise reports it as skipped.
    /// </summary>
    private void WriteIfMissing(
        string templatePath,
        ModuleModel model,
        string outputPath,
        List<string> generated,
        List<string> skipped)
    {
        if (File.Exists(outputPath))
        {
            skipped.Add(CodeGen.Rel(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath)));
            return;
        }

        _templates.RenderToFile(templatePath, model, outputPath);
        generated.Add(CodeGen.Rel(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath)));
    }

    /// <summary>
    /// Inserts the repository + handler registration into the module class's
    /// <c>ConfigureServices</c> body, idempotently.
    /// </summary>
    private static bool EnsureModuleRegistrations(string moduleFile, string moduleNs, string entity)
    {
        var content = File.ReadAllText(moduleFile);
        var original = content;

        var appNs = $"{moduleNs}.Application";
        var domainNs = $"{moduleNs}.Domain";

        // Detect the prevailing line ending style to avoid mixed \r\n / \n.
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";

        // Ensure required usings. Check without the newline suffix so the guard
        // works on both LF and CRLF files (Windows writes CRLF by default).
        foreach (var u in new[] { "using Modulus.Mediator.Extensions;", $"using {appNs};", $"using {domainNs};" })
        {
            if (!content.Contains(u, StringComparison.Ordinal))
            {
                var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
                if (nsIdx >= 0)
                    content = content.Insert(nsIdx, u + nl);
            }
        }

        var repoLine = $"        services.AddScoped<I{entity}Repository, {entity}Repository>();";
        var handlerLine = $"        services.AddMediatorHandlers(typeof(Create{entity}Handler).Assembly);";

        if (!content.Contains($"I{entity}Repository,", StringComparison.Ordinal))
            content = InsertInConfigureServices(content, repoLine);
        // Use generic check: if ANY AddMediatorHandlers call exists, don't insert another.
        // This prevents double-registration when single commands/queries are added later.
        if (!content.Contains("AddMediatorHandlers(typeof(", StringComparison.Ordinal))
            content = InsertInConfigureServices(content, handlerLine);

        if (content == original)
            return false;

        Ux.WriteFile(moduleFile, content);
        return true;
    }

    /// <summary>
    /// Inserts the <c>DbSet&lt;T&gt;</c> property + Domain using into the
    /// module's own <c>{Module}DbContext</c>, idempotently.
    /// </summary>
    private static bool EnsureDbSetRegistration(
        string dbContextFile, string moduleNs, string entity, string plural)
    {
        var content = File.ReadAllText(dbContextFile);
        var original = content;

        // Detect the prevailing line ending style to avoid mixed \r\n / \n.
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";

        // Ensure the Domain using is present (CRLF-safe: no "\n" suffix).
        var domainUsing = $"using {moduleNs}.Domain;";
        if (!content.Contains(domainUsing, StringComparison.Ordinal))
        {
            var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
            if (nsIdx >= 0)
                content = content.Insert(nsIdx, domainUsing + nl);
        }

        // Insert the DbSet property after the TablePrefix line if not present.
        if (!content.Contains($"DbSet<{entity}>", StringComparison.Ordinal))
        {
            var dbSetLine = $"{nl}    public DbSet<{entity}> {plural} => Set<{entity}>();{nl}";
            var tpIdx = content.IndexOf("TablePrefix", StringComparison.Ordinal);
            if (tpIdx >= 0)
            {
                var lineEnd = content.IndexOf('\n', tpIdx);
                if (lineEnd >= 0)
                    content = content.Insert(lineEnd + 1, dbSetLine);
            }
            else
            {
                // Fallback: insert before the last '}' that closes the class.
                // Find the class declaration first so we don't accidentally
                // land outside a block-namespace closing brace.
                var classIdx = content.IndexOf("class ", StringComparison.Ordinal);
                if (classIdx >= 0)
                {
                    var classBodyOpen = content.IndexOf('{', classIdx);
                    if (classBodyOpen >= 0)
                    {
                        // Walk backwards from the end to find the closing brace
                        // of the class body (the last '}' at depth 1 relative to classBodyOpen).
                        var depth = 0;
                        var insertAt = -1;
                        for (var i = classBodyOpen; i < content.Length; i++)
                        {
                            if (content[i] == '{') depth++;
                            else if (content[i] == '}')
                            {
                                depth--;
                                if (depth == 0) { insertAt = i; break; }
                            }
                        }
                        if (insertAt >= 0)
                            content = content.Insert(insertAt, dbSetLine);
                    }
                }
            }
        }

        if (content == original)
            return false;

        Ux.WriteFile(dbContextFile, content);
        return true;
    }

    /// <summary>
    /// Inserts a line right after the opening brace of the ConfigureServices
    /// method body.
    /// </summary>
    private static string InsertInConfigureServices(string content, string line)
    {
        var methodIdx = content.IndexOf("ConfigureServices(", StringComparison.Ordinal);
        if (methodIdx < 0) return content;

        // Find the opening '{' of the method body after the signature.
        var bodyOpen = content.IndexOf('{', methodIdx);
        if (bodyOpen < 0) return content;

        // Detect the prevailing line ending style to avoid mixed \r\n / \n.
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        return content.Insert(bodyOpen + 1, nl + line);
    }
}
