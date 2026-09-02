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

        [Description("NOT YET SUPPORTED: extra fields (name:string,price:decimal). Reserved for a future release.")]
        [CommandOption("--fields")]
        public string? Fields { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        if (s.Fields is not null)
            Ux.Warning("--fields is reserved and currently ignored (entities ship with a single Name field).");

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
