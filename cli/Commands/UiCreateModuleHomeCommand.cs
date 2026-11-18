using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui create-module-home &lt;Module&gt;</c>: a landing page for one business module (counts of its entities, APIs and permissions
/// as sample values) from the template package's <c>module-home</c> page. MVC gets it inside the module's area with its own controller.
/// </summary>
internal sealed class UiCreateModuleHomeCommand : Command<UiCreateModuleHomeCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("The business module the page is for (for example Catalog)")]
        [CommandArgument(0, "<module>")]
        public string Module { get; init; } = "";

        [Description("UI engine: mvc, razor-pages, blazor or fluid (default: the one recorded in .modulus.json)")]
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
            var module = CodeGen.ValidateIdentifier(s.Module, "Module");
            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add a module page to.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);
            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");
            var file = UiScaffold.TemplateFile(templates, engine, "module-home")
                ?? throw new InvalidOperationException($"The template package has no 'module-home' page for {engine}.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var route = "/" + module.ToLowerInvariant();
            var target = engine == "mvc"
                ? Path.Combine(UiLayout.MvcArea(uiDir, module), "Views", "Home", "Index.cshtml")
                : UiScaffold.OutputPath(engine, uiDir, module, "Home");
            if (File.Exists(target) && !Ux.Force)
                throw new InvalidOperationException($"{Path.GetRelativePath(start, target)} already exists. Pass --force to overwrite it.");

            var renderer = new TemplateEngine();
            renderer.RenderToFile(file, new { ModuleName = module }, target);
            Ux.Success($"{module} module page created", Path.GetRelativePath(start, target));

            if (engine == "mvc")
            {
                var rootNamespace = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
                var area = UiLayout.MvcArea(uiDir, module);
                var controller = Path.Combine(area, "Controllers", "HomeController.cs");
                if (File.Exists(controller) && !Ux.Force)
                    throw new InvalidOperationException($"{Path.GetRelativePath(start, controller)} already exists. Pass --force to overwrite it.");
                var useAuth = File.Exists(inventory.UiProgramCsPath) && UiAccessGates.HasSignIn(File.ReadAllText(inventory.UiProgramCsPath));
                renderer.RenderToFile("ui/ModuleHomeController", new
                {
                    Namespace = $"{rootNamespace}.Areas.{module}.Controllers",
                    ModuleName = module,
                    Route = route,
                    UseAuth = useAuth,
                }, controller);
                Ux.Success("Controller added", Path.GetRelativePath(start, controller));

                // The area's own imports and layout, written once.
                foreach (var (template, name) in new[] { ("ui/MvcAreaViewImports", "_ViewImports.cshtml"), ("ui/MvcAreaViewStart", "_ViewStart.cshtml") })
                {
                    var shared = Path.Combine(area, "Views", name);
                    if (!File.Exists(shared))
                        renderer.RenderToFile(template, new { UiNamespace = rootNamespace, ModuleName = module }, shared);
                }

                foreach (var change in UiHostWiring.EnsureControllerRouting(inventory.UiProgramCsPath))
                    Ux.Success("Web host updated", change);
            }

            Ux.Info($"The page is at {route}.");
            return 0;
        });
    }
}
