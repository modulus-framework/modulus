using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui create-crud &lt;Entity&gt;</c>: list, create/edit and delete pages for an entity of a module, over the module's API (the
/// endpoints <c>generate-crud</c> writes) through a typed client in the Web project. Razor Pages and Blazor.
/// </summary>
internal sealed class UiCreateCrudCommand : Command<UiCreateCrudCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Entity to build pages for, e.g. Product (its API must exist: modulus generate-crud Product)")]
        [CommandArgument(0, "<entity>")]
        public string Entity { get; init; } = "";

        [Description("Module name when the entity name exists in several")]
        [CommandOption("--module")]
        public string? Module { get; init; }

        [Description("UI engine: razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var entity = CodeGen.ValidateIdentifier(s.Entity, "Entity");
            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind != AppKind.WebAppApi)
                throw new InvalidOperationException(
                    $"CRUD pages need a web app that calls the API over HTTP (create the app with --kind webapp+api); this one is {(inventory.Kind?.Label() ?? "not marked with an app kind")}.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);
            if (engine == "mvc")
                throw new InvalidOperationException("The MVC engine has no CRUD pages yet. Use --engine razor-pages or blazor.");

            var (moduleDir, moduleName, _) = EntityMetadata.FindInApp(inventory.SolutionDir, entity, s.Module);
            var moduleNamespace = Path.GetFileName(moduleDir);
            var plural = CodeGen.Pluralize(entity);
            var route = plural.ToLowerInvariant();
            var uiProject = inventory.UiProjectPath;
            var uiDir = Path.GetDirectoryName(uiProject)!;
            var uiNamespace = Path.GetFileNameWithoutExtension(uiProject);

            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");

            var model = new
            {
                EntityName = entity,
                EntityNamePlural = plural,
                ModuleName = moduleName,
                ModuleNameLower = moduleName.ToLowerInvariant(),
                ModuleNamespace = moduleNamespace,
                RouteName = route,
                Route = route,
                UiNamespace = uiNamespace,
            };

            // Resolve and check everything before writing anything.
            var pages = new List<(string Source, string Target, bool Package)>();
            if (engine == "razor-pages")
            {
                var folder = Path.Combine(uiDir, "Pages", plural);
                pages.Add((UiScaffold.TemplateFile(templates, engine, "crud-index") ?? throw Missing(engine), Path.Combine(folder, "Index.cshtml"), true));
                pages.Add(("ui/CrudIndexModel", Path.Combine(folder, "Index.cshtml.cs"), false));
                pages.Add((UiScaffold.TemplateFile(templates, engine, "crud-edit") ?? throw Missing(engine), Path.Combine(folder, "Edit.cshtml"), true));
                pages.Add(("ui/CrudEditModel", Path.Combine(folder, "Edit.cshtml.cs"), false));
            }
            else
            {
                var folder = Path.Combine(uiDir, "Components", "Pages", plural);
                pages.Add((UiScaffold.TemplateFile(templates, engine, "crud-index") ?? throw Missing(engine), Path.Combine(folder, "Index.razor"), true));
                pages.Add((UiScaffold.TemplateFile(templates, engine, "crud-edit") ?? throw Missing(engine), Path.Combine(folder, "Edit.razor"), true));
            }

            var client = Path.Combine(uiDir, "ApiClients", $"{entity}ApiClient.cs");
            var menu = Path.Combine(uiDir, "Menu", $"{plural}MenuContributor.cs");
            var targets = pages.Select(p => p.Target).Append(client).Append(menu).ToList();
            var existing = targets.Where(File.Exists).ToList();
            if (existing.Count > 0 && !Ux.Force)
                throw new InvalidOperationException(
                    $"Already present: {string.Join(", ", existing.Select(f => Path.GetRelativePath(start, f)))}. Pass --force to overwrite.");

            var renderer = new TemplateEngine();
            foreach (var (source, target, _) in pages)
            {
                renderer.RenderToFile(source, model, target);
                Ux.Success("Page added", Path.GetRelativePath(start, target));
            }

            renderer.RenderToFile("ui/EntityApiClient", model, client);
            Ux.Success("Typed client added", Path.GetRelativePath(start, client));
            renderer.RenderToFile("ui/CrudMenuContributor", model, menu);
            Ux.Success("Menu entry added", Path.GetRelativePath(start, menu));

            WireHost(inventory, uiProject, uiDir, uiNamespace, moduleDir, moduleNamespace, entity, plural, start);

            Ux.Info($"The pages are at /{route}. They call /api/{moduleName.ToLowerInvariant()}/{route} on the API ({entity}'s endpoints from `modulus generate-crud`); set Api:BaseUrl in the Web project's appsettings.json.");
            return 0;
        });
    }

    private static void WireHost(ModuleDiscovery.AppInventory inventory, string uiProject, string uiDir, string uiNamespace,
        string moduleDir, string moduleNamespace, string entity, string plural, string start)
    {
        // The pages bind the module's DTOs, so the Web project references the module's Application project.
        var application = Path.Combine(CodeGen.LayerDir(moduleDir, moduleNamespace, "Application"), $"{moduleNamespace}.Application.csproj");
        if (File.Exists(application) && ProjectFileService.EnsureCsprojProjectReference(uiProject, application, Ux.DryRun))
            Ux.Success("Project reference added", Path.GetRelativePath(start, uiProject));

        var extensions = Path.Combine(uiDir, "ApiClientExtensions.cs");
        if (File.Exists(extensions))
            Edit(extensions, text => UiCrud.EnsureClientRegistered(text, entity), start);
        else
            Ux.Warning($"ApiClientExtensions.cs not found: register the client yourself with services.AddModulusHttpClient<{entity}ApiClient>().");

        var program = inventory.UiProgramCsPath;
        if (File.Exists(program))
            Edit(program, text => UiCrud.EnsureMenuRegistered(text, uiNamespace, plural), start);
        else
            Ux.Warning($"Program.cs not found: register {plural}MenuContributor yourself.");
    }

    private static void Edit(string path, Func<string, string> update, string start)
    {
        var original = File.ReadAllText(path);
        var updated = update(original);
        if (updated == original)
            return;
        Ux.WriteFile(path, updated);
        Ux.Success("Updated", Path.GetRelativePath(start, path));
    }

    private static InvalidOperationException Missing(string engine) =>
        new($"The template package has no CRUD pages for {engine}.");
}
