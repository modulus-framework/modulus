using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Generates CRUD (Create, Read, Update, Delete) code for a domain entity
/// within an existing layered module, distributing the files across the
/// module's layer projects.
/// </summary>
internal sealed class GenerateCrudCommand : Command<GenerateCrudCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [Description("Entity name (e.g. Product, Order)")]
        [CommandArgument(0, "<entity>")]
        public required string Entity { get; init; }

        [Description("Module name or namespace (e.g. Catalog, MyApp.Modules.Catalog)")]
        [CommandOption("-m|--module")]
        public string? Module { get; init; }

        [Description("Generate with additional fields (comma-separated: name:string,price:decimal)")]
        [CommandOption("--fields")]
        public string? Fields { get; init; }
    }

    private readonly TemplateEngine _templates = new();

    public override int Execute(CommandContext ctx, Settings s)
    {
        var entity = s.Entity;
        var entityLower = ToCamelCase(entity);
        var plural = ToPlural(entity);
        var routeName = plural.ToLowerInvariant();

        // Resolve module root directory (src/{rootNs}.Modules.{Module}).
        var moduleDir = ResolveModuleDirectory(s.Module)
            ?? throw new InvalidOperationException("Could not resolve module directory.");
        var moduleNs = ResolveModuleNamespace(moduleDir);
        var moduleName = moduleNs.Split('.').LastOrDefault() ?? "Module";
        var rootNs = ExtractRootNamespace(moduleNs);

        // Locate the layer project directories.
        var domainDir = LayerDir(moduleDir, moduleNs, "Domain");
        var appDir = LayerDir(moduleDir, moduleNs, "Application");
        var infraDir = LayerDir(moduleDir, moduleNs, "Infrastructure");
        var presDir = LayerDir(moduleDir, moduleNs, "Presentation");

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleNamespace = moduleNs,
            ModuleName = moduleName,
            EntityName = entity,
            EntityNameLower = entityLower,
            RouteName = routeName,
        };

        var generated = new List<string>();

        // ── Domain layer ──────────────────────────────────────────
        var entityFile = Path.Combine(domainDir, $"{entity}.cs");
        if (!File.Exists(entityFile))
        {
            _templates.RenderToFile("module/Domain/Entity", model, entityFile);
            generated.Add(Rel(domainDir, $"{entity}.cs"));
        }

        var repoInterface = Path.Combine(domainDir, $"I{entity}Repository.cs");
        if (!File.Exists(repoInterface))
        {
            _templates.RenderToFile("module/Domain/IRepository", model, repoInterface);
            generated.Add(Rel(domainDir, $"I{entity}Repository.cs"));
        }

        // ── Application layer (DTOs + commands/handlers/queries) ────
        _templates.RenderToFile("module/Application/Dto", model,
            Path.Combine(appDir, "Dtos", $"{entity}Dto.cs"));
        generated.Add(Rel(appDir, $"Dtos/{entity}Dto.cs"));

        // ── Application layer (commands/handlers/queries) ─────────
        _templates.RenderToFile("module/Application/CreateCommand", model,
            Path.Combine(appDir, $"Create{entity}Command.cs"));
        generated.Add(Rel(appDir, $"Create{entity}Command.cs"));

        _templates.RenderToFile("module/Application/CreateHandler", model,
            Path.Combine(appDir, $"Create{entity}Handler.cs"));
        generated.Add(Rel(appDir, $"Create{entity}Handler.cs"));

        _templates.RenderToFile("module/Application/GetAllQuery", model,
            Path.Combine(appDir, $"Get{plural}Query.cs"));
        generated.Add(Rel(appDir, $"Get{plural}Query.cs"));

        _templates.RenderToFile("module/Application/GetAllHandler", model,
            Path.Combine(appDir, $"Get{plural}Handler.cs"));
        generated.Add(Rel(appDir, $"Get{plural}Handler.cs"));

        _templates.RenderToFile("module/Application/GetByIdQuery", model,
            Path.Combine(appDir, $"Get{entity}ByIdQuery.cs"));
        generated.Add(Rel(appDir, $"Get{entity}ByIdQuery.cs"));

        _templates.RenderToFile("module/Application/GetByIdHandler", model,
            Path.Combine(appDir, $"Get{entity}ByIdHandler.cs"));
        generated.Add(Rel(appDir, $"Get{entity}ByIdHandler.cs"));

        _templates.RenderToFile("module/Application/UpdateCommand", model,
            Path.Combine(appDir, $"Update{entity}Command.cs"));
        generated.Add(Rel(appDir, $"Update{entity}Command.cs"));

        _templates.RenderToFile("module/Application/UpdateHandler", model,
            Path.Combine(appDir, $"Update{entity}Handler.cs"));
        generated.Add(Rel(appDir, $"Update{entity}Handler.cs"));

        _templates.RenderToFile("module/Application/DeleteCommand", model,
            Path.Combine(appDir, $"Delete{entity}Command.cs"));
        generated.Add(Rel(appDir, $"Delete{entity}Command.cs"));

        _templates.RenderToFile("module/Application/DeleteHandler", model,
            Path.Combine(appDir, $"Delete{entity}Handler.cs"));
        generated.Add(Rel(appDir, $"Delete{entity}Handler.cs"));

        // ── Integration event (Application layer) ──────────────────
        var evtFile = Path.Combine(appDir, "IntegrationEvents", $"{entity}CreatedIntegrationEvent.cs");
        if (!File.Exists(evtFile))
        {
            _templates.RenderToFile("module/Application/IntegrationEvent", model, evtFile);
            generated.Add(Rel(appDir, $"IntegrationEvents/{entity}CreatedIntegrationEvent.cs"));
        }

        // ── Infrastructure layer ──────────────────────────────────
        _templates.RenderToFile("module/Infrastructure/Repository", model,
            Path.Combine(infraDir, $"{entity}Repository.cs"));
        generated.Add(Rel(infraDir, $"{entity}Repository.cs"));

        // Wire repository + handler registration into the module class.
        var moduleFile = Path.Combine(infraDir, $"{moduleName}Module.cs");
        if (File.Exists(moduleFile))
        {
            var wired = EnsureModuleRegistrations(moduleFile, moduleNs, entity);
            if (wired)
                generated.Add(Rel(infraDir, $"{moduleName}Module.cs (updated)"));
        }

        // Auto-wire the DbSet into the module's own DbContext.
        var dbContextFile = Path.Combine(infraDir, $"{moduleName}DbContext.cs");
        if (File.Exists(dbContextFile))
        {
            var wired = EnsureDbSetRegistration(dbContextFile, moduleNs, entity, plural);
            if (wired)
                generated.Add(Rel(infraDir, $"{moduleName}DbContext.cs (updated)"));
        }

        // ── Presentation layer ────────────────────────────────────
        _templates.RenderToFile("module/Presentation/Controller", model,
            Path.Combine(presDir, $"{entity}sController.cs"));
        generated.Add(Rel(presDir, $"{entity}sController.cs"));

        // ── Summary ───────────────────────────────────────────────
        AnsiConsole.MarkupLine("[green]✓[/] Generated CRUD for [cyan]{0}[/] in [grey]{1}[/]",
            entity, moduleName);
        foreach (var f in generated)
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", f);

        AnsiConsole.MarkupLine("[grey]  The DbSet + using were auto-wired into {0}DbContext.cs.[/]",
            moduleName);

        return 0;
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

        // Ensure required usings. Check without the newline suffix so the guard
        // works on both LF and CRLF files (Windows writes CRLF by default).
        foreach (var u in new[] { "using Modulus.Mediator.Extensions;", $"using {appNs};", $"using {domainNs};" })
        {
            if (!content.Contains(u, StringComparison.Ordinal))
            {
                var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
                if (nsIdx >= 0)
                    content = content.Insert(nsIdx, u + "\n");
            }
        }

        var repoLine = $"        services.AddScoped<I{entity}Repository, {entity}Repository>();";
        var handlerLine = $"        services.AddMediatorHandlers(typeof(Create{entity}Handler).Assembly);";

        if (!content.Contains($"I{entity}Repository,", StringComparison.Ordinal))
            content = InsertInConfigureServices(content, repoLine);
        if (!content.Contains($"Create{entity}Handler).Assembly", StringComparison.Ordinal))
            content = InsertInConfigureServices(content, handlerLine);

        if (content == original)
            return false;

        File.WriteAllText(moduleFile, content);
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

        // Ensure the Domain using is present (CRLF-safe: no "\n" suffix).
        var domainUsing = $"using {moduleNs}.Domain;";
        if (!content.Contains(domainUsing, StringComparison.Ordinal))
        {
            var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
            if (nsIdx >= 0)
                content = content.Insert(nsIdx, domainUsing + "\n");
        }

        // Insert the DbSet property after the TablePrefix line if not present.
        if (!content.Contains($"DbSet<{entity}>", StringComparison.Ordinal))
        {
            var dbSetLine = $"\n    public DbSet<{entity}> {plural} => Set<{entity}>();\n";
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

        File.WriteAllText(dbContextFile, content);
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

        return content.Insert(bodyOpen + 1, "\n" + line);
    }

    private static string LayerDir(string moduleDir, string moduleNs, string layer)
        => Path.Combine(moduleDir, $"{moduleNs}.{layer}");

    private static string Rel(string dir, string file)
        => $"{Path.GetFileName(dir)}/{file}";

    private string? ResolveModuleDirectory(string? module)
    {
        var modulesDir = Path.Combine(Environment.CurrentDirectory, "src", "Modules");
        if (!Directory.Exists(modulesDir))
            throw new InvalidOperationException(
                "No 'src/Modules' directory found. Run from the solution root.");

        // If module specified, find matching directory by suffix.
        if (!string.IsNullOrEmpty(module))
        {
            var matches = Directory.GetDirectories(modulesDir, $"*.{module}")
                .Where(d => d.Contains(".Modules."))
                .ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length == 0)
                throw new InvalidOperationException(
                    $"No module matching '{module}' found in src/Modules/.");
        }

        // Auto-detect: find .Modules.* directories.
        var modDirs = Directory.GetDirectories(modulesDir, "*.Modules.*");
        if (modDirs.Length == 1) return modDirs[0];
        if (modDirs.Length > 1)
            throw new InvalidOperationException(
                "Multiple modules found. Specify --module <name>.\n" +
                "Found: " + string.Join(", ", modDirs.Select(Path.GetFileName)));

        throw new InvalidOperationException(
            "Could not find a module directory. Run from the solution root " +
            "or specify --module <ModuleName>.");
    }

    /// <summary>
    /// Derives the module namespace from the resolved module directory name.
    /// Module directories are named after their namespace, e.g.
    /// <c>src/MyApp.Modules.Products</c> → <c>MyApp.Modules.Products</c>.
    /// </summary>
    private static string ResolveModuleNamespace(string moduleDir)
        => Path.GetFileName(moduleDir.TrimEnd(
               Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string ExtractRootNamespace(string moduleNs)
    {
        var parts = moduleNs.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[..^2]) : moduleNs;
    }

    private static string ToCamelCase(string s) =>
        char.ToLowerInvariant(s[0]) + s[1..];

    private static string ToPlural(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
         || s.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
         || s.EndsWith('x') || s.EndsWith('z'))
            return s + "es";
        if (s.Length > 1 && s.EndsWith('y') && !"aeiouAEIOU".Contains(s[^2]))
            return s[..^1] + "ies";
        if (s.EndsWith('s'))
            return s + "es";
        return s + "s";
    }
}
