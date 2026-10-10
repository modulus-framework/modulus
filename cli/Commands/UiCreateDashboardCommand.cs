using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiCreateDashboardCommand : Command<UiCreateDashboardCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Dashboard to create: overview, analytics, reports or audit")]
        [CommandArgument(0, "<template>")]
        public string Template { get; init; } = "";

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("Folder (Views/Pages/Components) the page goes in")]
        [CommandOption("--module")]
        [DefaultValue("Dashboards")]
        public string Module { get; init; } = "Dashboards";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var page = UiScaffold.FindDashboard(s.Template);
            var module = CodeGen.ValidateIdentifier(s.Module, "Module");

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add a dashboard to.");

            var engine = UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir);
            var templates = UiTemplatePackage.Resolve()
                ?? throw new InvalidOperationException(
                    $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                    $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");

            var file = UiScaffold.TemplateFile(templates, engine, page.TemplateName)
                ?? throw new InvalidOperationException(
                    $"The '{page.Key}' dashboard has no {engine} template yet.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var pageName = char.ToUpperInvariant(page.Key[0]) + page.Key[1..];
            var target = UiScaffold.OutputPath(engine, uiDir, module, pageName);
            if (File.Exists(target) && !Ux.Force)
                throw new InvalidOperationException($"{Path.GetRelativePath(start, target)} already exists. Pass --force to overwrite it.");

            new TemplateEngine().RenderToFile(file, new
            {
                ModuleName = module,
                TotalUsers = 0, TotalModules = 0, ActiveSessions = 0,
                TotalReports = 0, ScheduledReports = 0, MonthlyReports = 0, AvgGenTime = "0s",
            }, target);

            Ux.Success($"{page.Title} page created", Path.GetRelativePath(start, target));
            Ux.Info(page.Description);
            return 0;
        });
    }
}
