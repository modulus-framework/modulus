using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui add-admin</c>: scaffolds the admin dashboard starter (users, roles, tenants, notifications, audit log, settings,
/// dashboards, account pages) for the app's UI engine, and adds its menu to the host. The files are the app's: edit them freely.
/// </summary>
internal sealed class UiAddAdminCommand : Command<UiAddAdminCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Pages to add, comma separated (default: all). One of: dashboard, analytics, reports, users, roles, tenants, settings, audit, health, profile, notifications, notification-preferences")]
        [CommandOption("--pages")]
        public string? Pages { get; init; }

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("Folder (Views/Pages/Components) the pages go in")]
        [CommandOption("--folder")]
        [DefaultValue("Admin")]
        public string Folder { get; init; } = "Admin";

        [Description("Do not add AddModulusAdminUi() to Program.cs")]
        [CommandOption("--no-menu")]
        public bool NoMenu { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var keys = UiAdminScaffold.ParseKeys(s.Pages);
            var folder = CodeGen.ValidateIdentifier(s.Folder, "Folder");

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add the admin pages to.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);
            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var rootNamespace = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
            var entries = UiAdminScaffold.Select(UiAdminScaffold.ReadEntries(templates, engine), keys);

            // Resolve and check everything before writing anything. The manifest puts the pages in "Admin"; --folder moves them.
            var plan = entries.Select(e =>
            {
                var file = Path.Combine(templates, engine, e.Template);
                if (!File.Exists(file))
                    throw new InvalidOperationException($"The package has no '{e.Template}' template for {engine}.");
                var relative = e.Path.Replace("/Admin/", $"/{folder}/", StringComparison.Ordinal);
                return (Entry: e, File: file, Target: Path.Combine(uiDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            }).ToList();

            var existing = plan.Select(x => x.Target).Where(File.Exists).ToList();
            if (existing.Count > 0 && !Ux.Force)
                throw new InvalidOperationException(
                    $"Already present: {string.Join(", ", existing.Select(f => Path.GetRelativePath(start, f)))}. Pass --force to overwrite.");

            var renderer = new TemplateEngine();
            foreach (var (entry, file, target) in plan)
            {
                renderer.RenderToFile(file, new { Namespace = rootNamespace + ".Controllers", PageName = Path.GetFileNameWithoutExtension(entry.Path), ModuleName = folder }, target);
                var page = UiAdminScaffold.PageOf(entry.Path);
                Ux.Success(page is null ? "Controller added" : $"{page.File} page added",
                    page is null ? Path.GetRelativePath(start, target) : $"{Path.GetRelativePath(start, target)}  →  {page.Route}");
            }

            // MVC views have no layout until a _ViewStart names one (the Razor Pages host sets it under Pages/). Left alone when the app has its own.
            var viewStart = Path.Combine(uiDir, "Views", "_ViewStart.cshtml");
            if (engine == "mvc" && !File.Exists(viewStart))
            {
                Ux.WriteFile(viewStart,
                    "@{\n    // The Modulus UI framework's application shell (header, menu, footer, theme settings).\n    Layout = \"_ModulusLayout\";\n}\n");
                Ux.Success("Layout added", Path.GetRelativePath(start, viewStart));
            }

            var programPath = Path.Combine(uiDir, "Program.cs");
            if (File.Exists(programPath))
            {
                if (engine == "mvc")
                    foreach (var change in UiHostWiring.EnsureControllerRouting(programPath))
                        Ux.Success("Web host updated", change);
                if (!s.NoMenu)
                    foreach (var change in UiHostWiring.EnsureAdminMenu(programPath, UiAdminScaffold.WiringLine(keys)))
                        Ux.Success("Web host updated", change);
            }
            else if (!s.NoMenu)
                Ux.Warning($"No Program.cs in {Path.GetRelativePath(start, uiDir)}; add {UiAdminScaffold.WiringLine(keys)} yourself to get the menu.");

            if (File.Exists(programPath) && !UiAccessGates.HasSignIn(File.ReadAllText(programPath)))
                Ux.Warning("This Web host has no sign-in, and the admin pages require a signed-in user ([[Authorize]]), so they will be refused. " +
                           "Create the app with --auth openiddict (or add your own authentication) before using them.");

            Ux.Info("The pages read their data through Modulus.Ui.Abstractions.Admin (IUserDirectory, IRoleDirectory, ITenantDirectory, INotificationCenter, IAuditTrail, IAdminSettingsStore, IMetricsSource). " +
                    "Until you register your own implementation they show sample data.");
            return 0;
        });
    }
}
