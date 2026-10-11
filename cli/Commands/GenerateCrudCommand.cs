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

        [CommandOption("--ai")]
        [DefaultValue(false)]
        [Description("Expose the entity to the AI platform: [AiIndexed]/[AiQueryable] on the entity, [AiCapability] on the list query, [AiResource] on the lookup query, the indexing grant and a test class. Re-run on an existing entity to mark it. Needs modulus add-ai.")]
        public bool Ai { get; init; }

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
        };

        // A host with the identity backend guards the API endpoints with a permission the Admin role holds;
        // a host with no such role has nothing to grant it to, so its endpoints stay as open as the rest of that host.
        var host = ResolveHost(module);
        model.RequiredPermission = File.Exists(host.ProgramCs) && UiAccessGates.HasAdminRole(File.ReadAllText(host.ProgramCs))
            ? UiAccessGates.CrudPermission(module.Name, routeName)
            : null;

        // A multi-tenant host (modulus app --multi-tenancy) keeps companies apart: the entity is tenant-owned and gets a test.
        var inventory = ModuleDiscovery.Inventory(Environment.CurrentDirectory);
        model.MultiTenant = IsMultiTenantHost(inventory?.ProgramCsPath);

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
        WriteIfMissing("module/Application/ModuleArea", model,
            Path.Combine(appDir, "IntegrationEvents", $"{model.ModuleName}Area.cs"),
            generated, skipped);
        WriteIfMissing("module/Application/IntegrationEvent", model,
            Path.Combine(appDir, "IntegrationEvents", $"{entity}CreatedIntegrationEvent.cs"),
            generated, skipped);
        WriteIfMissing("module/Application/UpdatedIntegrationEvent", model,
            Path.Combine(appDir, "IntegrationEvents", $"{entity}UpdatedIntegrationEvent.cs"),
            generated, skipped);
        WriteIfMissing("module/Application/DeletedIntegrationEvent", model,
            Path.Combine(appDir, "IntegrationEvents", $"{entity}DeletedIntegrationEvent.cs"),
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

        // ── AI connector surface (opt-in; marks files that already exist too) ──
        if (s.Ai)
            MarkForAi(module, model, host, inventory, domainDir, appDir, generated, skipped);

        if (model.RequiredPermission is not null)
            EnsureApiPermission(module, model, host, generated);

        // ── Tenant isolation test (multi-tenant hosts) ──
        if (model.MultiTenant && inventory is not null)
        {
            var testsDir = Path.Combine(inventory.SolutionDir, "tests", $"{inventory.RootNamespace}.Tests");
            if (Directory.Exists(testsDir))
            {
                WriteIfMissing("module/Tests/TenantIsolationTests", model,
                    Path.Combine(testsDir, $"{entity}TenantIsolationTests.cs"), generated, skipped);
            }
        }

        // ── BFFs: every BFF gets the module's typed client with this entity's read methods ──
        if (ModuleDiscovery.Inventory(Environment.CurrentDirectory) is { Bffs.Count: > 0 } app)
        {
            foreach (var bff in app.Bffs)
            {
                foreach (var file in BffApiClients.EnsureModule(_templates, bff, module.Name, [entity]))
                    generated.Add($"src/Bff/{bff.ProjectName}/{file}");
            }
        }

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

    /// <summary>
    /// <c>--ai</c>: the entity joins the platform's index and gets generated Search/Calculate, the list query becomes a capability,
    /// the lookup query the record source, both checking the CRUD permission in the mediator (capabilities run as the platform's
    /// user, not behind the HTTP endpoint), the indexing role may read it, and a test class proves the manifest and extraction.
    /// </summary>
    private void MarkForAi(
        CodeGen.ModuleInfo module, ModuleModel model, HostFiles host, ModuleDiscovery.AppInventory? inventory,
        string domainDir, string appDir, List<string> generated, List<string> skipped)
    {
        var entity = model.EntityName!;
        var plural = CodeGen.Pluralize(entity);
        var permission = model.RequiredPermission ?? UiAccessGates.CrudPermission(module.Name, model.RouteName!);
        var names = AiWiring.NamesFor(module.RootNamespace, module.Name, entity, permission);
        var checksPermission = model.RequiredPermission is not null;

        var entityFile = Path.Combine(domainDir, $"{entity}.cs");
        var entitySource = File.Exists(entityFile) ? File.ReadAllText(entityFile) : string.Empty;
        UpdateExisting(entityFile, domainDir, generated, t => AiWiring.MarkEntity(t, entity, names));
        UpdateExisting(Path.Combine(appDir, $"Get{plural}Query.cs"), appDir, generated,
            t => AiWiring.MarkListQuery(t, $"Get{plural}Query", plural, names, checksPermission));

        // The batch lookup: a page of the index in one query. Needs the repository method, the query and its handler.
        var infraDir = CodeGen.LayerDir(module.Directory, module.Namespace, "Infrastructure");
        UpdateExisting(Path.Combine(domainDir, $"I{entity}Repository.cs"), domainDir, generated, t => AiWiring.EnsureRepositoryByIds(t, entity, implementation: false));
        UpdateExisting(Path.Combine(infraDir, $"{entity}Repository.cs"), infraDir, generated, t => AiWiring.EnsureRepositoryByIds(t, entity, implementation: true));
        var repositoryHasByIds = File.Exists(Path.Combine(domainDir, $"I{entity}Repository.cs"))
            && File.ReadAllText(Path.Combine(domainDir, $"I{entity}Repository.cs")).Contains("GetByIdsAsync", StringComparison.Ordinal)
            && File.Exists(Path.Combine(infraDir, $"{entity}Repository.cs"))
            && File.ReadAllText(Path.Combine(infraDir, $"{entity}Repository.cs")).Contains("GetByIdsAsync", StringComparison.Ordinal);
        var batchQuery = repositoryHasByIds ? $"Get{plural}ByIdsQuery" : null;
        if (batchQuery is not null)
        {
            foreach (var (template, file) in new[] { ("ai/GetByIdsQuery", $"Get{plural}ByIdsQuery.cs"), ("ai/GetByIdsHandler", $"Get{plural}ByIdsHandler.cs") })
            {
                if (File.Exists(Path.Combine(appDir, file)))
                {
                    skipped.Add(CodeGen.Rel(appDir, file));
                    continue;
                }

                _templates.RenderToFile(template, model, Path.Combine(appDir, file));
                generated.Add(CodeGen.Rel(appDir, file));
            }
        }

        UpdateExisting(Path.Combine(appDir, $"Get{entity}ByIdQuery.cs"), appDir, generated,
            t => AiWiring.MarkLookupQuery(t, $"Get{entity}ByIdQuery", entity, AiWiring.TitleField(entitySource), names, checksPermission, batchQuery));

        var program = File.Exists(host.ProgramCs) ? File.ReadAllText(host.ProgramCs) : string.Empty;
        if (checksPermission)
            UpdateExisting(host.ProgramCs, host.ApiDir, generated, t => AiWiring.EnsureIndexerGrant(t, permission));
        else
            AnsiConsole.MarkupLine("[yellow]![/] The host declares no permissions: the AI queries check none in the mediator, so any platform user the connector resolves may run them. Grant the indexing role ({0}) read access yourself.", AiWiring.IndexerRole);

        if (!AiWiring.HasConnector(program))
        {
            AnsiConsole.MarkupLine("[yellow]![/] The host does not run the AI connector yet: run [cyan]modulus add-ai[/] (the attributes do nothing until then).");
            return;
        }

        var testsDir = inventory is null ? null : Path.Combine(inventory.SolutionDir, "tests", $"{inventory.RootNamespace}.Tests");
        if (testsDir is null || !File.Exists(Path.Combine(testsDir, "AiConnectorTests.cs")))
            return;

        var testModel = new
        {
            RootNamespace = module.RootNamespace,
            EntityName = entity,
            ModuleLower = module.Name.ToLowerInvariant(),
            model.RouteName,
            ListCapability = names.ListCapability,
            SearchCapability = names.SearchCapability,
            CalculateCapability = names.CalculateCapability,
            names.ResourceType,
            AdminRole = UiAccessGates.AdminRole,
            TestInstance = AiWiring.TestInstance,
            // Extraction needs a record the test can create (the generated create request takes a name) and an instance without a company.
            CanExtract = checksPermission && !model.MultiTenant && AiWiring.TitleField(entitySource) == "Name",
        };
        if (File.Exists(Path.Combine(testsDir, $"{entity}AiCapabilityTests.cs")))
            skipped.Add($"tests/{entity}AiCapabilityTests.cs");
        else
        {
            _templates.RenderToFile("ai/EntityAiCapabilityTests", testModel, Path.Combine(testsDir, $"{entity}AiCapabilityTests.cs"));
            generated.Add($"tests/{entity}AiCapabilityTests.cs");
        }
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
    /// <summary>The API host resolves companies (<c>AddMultiTenancy(</c> in its Program.cs).</summary>
    internal static bool IsMultiTenantHost(string? programCs)
        => !string.IsNullOrEmpty(programCs) && File.Exists(programCs)
            && File.ReadAllText(programCs).Contains("AddMultiTenancy(", StringComparison.Ordinal);

    private sealed record HostFiles(string ApiDir, string ApiCsproj, string ProgramCs);

    /// <summary>
    /// Prefers the discovered host paths (custom layouts); falls back to the generated-app convention when there is no
    /// <c>.slnx</c> (e.g. tests). Always the API project: that is where the generated endpoints live.
    /// </summary>
    private static HostFiles ResolveHost(CodeGen.ModuleInfo module)
    {
        // Generated endpoints (and the permission they require) live in the API host, whatever the app kind.
        var inventory = ModuleDiscovery.Inventory(Environment.CurrentDirectory);

        var hostCsproj = inventory?.ApiProjectPath is { Length: > 0 } ap && File.Exists(ap)
            ? ap
            : Path.Combine(Environment.CurrentDirectory, "src", "API", $"{module.RootNamespace}.Api", $"{module.RootNamespace}.Api.csproj");
        var hostDir = Path.GetDirectoryName(hostCsproj)!;
        var programCs = inventory?.ProgramCsPath is { Length: > 0 } pc && File.Exists(pc)
            ? pc
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
        var wired = ApiPermissionWiring.EnsurePermission(
            original, module.Name, permission, UiAccessGates.CrudPermissionDescription(model.EntityPlural ?? module.Name));
        if (wired == original)
            return;

        if (!Ux.DryRun)
            Ux.WriteFile(host.ProgramCs, wired);
        generated.Add(CodeGen.Rel(host.ApiDir, "Program.cs (updated)"));
    }

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
