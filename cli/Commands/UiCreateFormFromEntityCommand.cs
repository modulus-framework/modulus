using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiCreateFormFromEntityCommand : Command<UiCreateFormFromEntityCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Entity name, e.g. Order (its class is found under src/Modules)")]
        [CommandArgument(0, "<entity>")]
        public string Entity { get; init; } = "";

        [Description("Module that holds the entity (default: the only module that has it)")]
        [CommandOption("--module")]
        public string? Module { get; init; }

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
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
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add a form to.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);

            var (moduleDir, moduleName, entityFile) = EntityMetadata.FindInApp(inventory.SolutionDir, entity, s.Module);
            var fields = EntityMetadata.Parse(File.ReadAllText(entityFile), entity);
            if (fields.Count == 0)
                throw new InvalidOperationException($"{entity} has no editable properties (public get/set) to build a form from.");

            if (engine == "fluid")
                return WriteFluidPartial(start, inventory, entity, moduleDir, moduleName);

            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");
            var file = UiScaffold.TemplateFile(templates, engine, "entity-form")
                ?? throw new InvalidOperationException($"The package has no entity form for {engine}.");

            var plural = CodeGen.Pluralize(entity);
            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            // The Blazor form saves through the entity's typed client, which create-crud generates.
            if (engine == "blazor" && !File.Exists(UiLayout.ApiClientFile(uiDir, entity)))
                throw new InvalidOperationException($"The Blazor form saves through the {entity} typed client, which does not exist yet. Run `modulus ui create-crud {entity} --module {moduleName}` first.");
            // Razor Pages and Blazor group the form with its entity's pages (Pages/{Module}/{Plural}); MVC views stay in Views/{Plural}.
            var target = UiScaffold.OutputPath(engine, uiDir, engine == "mvc" ? plural : Path.Combine(moduleName, plural), $"{entity}Form");
            if (File.Exists(target) && !Ux.Force)
                throw new InvalidOperationException($"{Path.GetRelativePath(start, target)} already exists. Pass --force to overwrite it.");

            var moduleNamespace = Path.GetFileName(moduleDir);

            // The form binds the module's DTO, so the Web project references the module's Application project (as create-crud does).

            var application = Path.Combine(CodeGen.LayerDir(moduleDir, moduleNamespace, "Application"), $"{moduleNamespace}.Application.csproj");

            if (File.Exists(application) && ProjectFileService.EnsureCsprojProjectReference(inventory.UiProjectPath, application, Ux.DryRun))

                Ux.Success("Project reference added", Path.GetRelativePath(start, inventory.UiProjectPath));

            new TemplateEngine().RenderToFile(file, new
            {
                EntityName = entity,
                EntityNamePlural = plural,
                ModuleName = moduleName,
                ModuleNamespace = moduleNamespace,
                UiNamespace = Path.GetFileNameWithoutExtension(inventory.UiProjectPath),
                Fields = fields,
            }, target);

            Ux.Success($"Form for {entity} created", Path.GetRelativePath(start, target));
            Ux.Info($"{fields.Count} field{(fields.Count == 1 ? "" : "s")}: {string.Join(", ", fields.Select(f => f.Name))}");
            // The file is a partial: it has no page model, so nothing routes to it. A page that saves through the API is what `ui create-crud` writes.
            Ux.Info($"This is a form partial, not a page. For list, create and edit pages that save through the API, run `modulus ui create-crud {entity} --module {moduleName}`.");
            return 0;
        });
    }

    /// <summary>
    /// The Fluid form: a Liquid partial with the entity's fields, under <c>Templates/Shared/Forms</c> (never served as a page).
    /// It reads the values from a <c>values</c> hash, so a page renders it with the entity it edits.
    /// </summary>
    private static int WriteFluidPartial(string start, ModuleDiscovery.AppInventory inventory, string entity, string moduleDir, string moduleName)
    {
        var fields = CrudFields.Resolve(moduleDir, entity);
        var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
        var target = Path.Combine(uiDir, "Templates", "Shared", "Forms", $"{entity}Form.liquid");
        if (File.Exists(target) && !Ux.Force)
            throw new InvalidOperationException($"{Path.GetRelativePath(start, target)} already exists. Pass --force to overwrite it.");

        if (!Ux.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, UiFluidCrud.FormPartial(entity, fields));
        }

        Ux.Success($"Form for {entity} created", Path.GetRelativePath(start, target));
        Ux.Info($"{fields.Count} field{(fields.Count == 1 ? "" : "s")}: {string.Join(", ", fields.Select(f => f.Name))}");
        Ux.Info($"This is a Liquid form partial for {moduleName}. Render it with {{% render 'Shared/Forms/{entity}Form', values: values %}} inside a form that posts to the page.");
        return 0;
    }
}
