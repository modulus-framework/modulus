using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui eject Card</c> / <c>ui eject Users</c> / <c>ui eject Users/Details</c> / <c>ui eject Tabler</c> — copies a framework view into the
/// app so the app owns it: a component view (<c>Views/Shared/Modulus/{Component}/Default.cshtml</c>), a feature UI page or partial
/// (<c>Pages/Users/Details.cshtml</c>) or a theme layout, shell partial or error page (<c>Themes/Tabler/Layouts/Application.cshtml</c>).
/// The app's file is served instead of the package's, with no registration; a page's handlers stay in the package, the app owns the markup.
/// <c>modulus ui diff</c> later shows how each copy drifted from the framework.
/// </summary>
internal sealed class UiEjectCommand : Command<UiEjectCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("What to eject: a component (Card, DataTable, Input), an installed feature UI (Users, Identity, Tenancy, ...) or theme (Tabler), or one of its views " +
            "(Users/Details, Tabler/Layouts/Application). Omit with --list to see what is available.")]
        [CommandArgument(0, "[targets]")]
        public string[] Targets { get; init; } = [];

        [Description("Eject everything: every component view, plus every installed feature UI and theme.")]
        [CommandOption("--all")]
        [DefaultValue(false)]
        public bool All { get; init; }

        [Description("List what can be ejected (and what the app already overrides).")]
        [CommandOption("--list")]
        [DefaultValue(false)]
        public bool List { get; init; }

        [Description("View name within a component (default: Default). Feature UIs and themes name a view by its path instead.")]
        [CommandOption("--view")]
        [DefaultValue("Default")]
        public string View { get; init; } = "Default";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var apiDir = UiEject.ResolveApiDir(Path.GetFullPath(s.Output ?? "./"));

            if (s.List)
            {
                return List(apiDir);
            }

            var views = Select(apiDir, s);
            if (views.Count == 0)
            {
                Ux.Error("Name what to eject (a component, feature UI or theme), or pass --all. Run `modulus ui eject --list` to see them.");
                return 1;
            }

            var written = 0;
            foreach (var view in views)
            {
                var relative = Path.GetRelativePath(apiDir, UiEject.PathFor(apiDir, view));
                var imports = UiEject.PendingImports(apiDir, view);
                switch (UiEject.Eject(apiDir, view, FrameworkVersion.Current, Ux.Force, Ux.DryRun))
                {
                    case UiEjectOutcome.Written when Ux.DryRun:
                        Ux.DryRunNote($"would write [cyan]{Markup.Escape(relative)}[/]");
                        if (imports is not null)
                        {
                            Ux.DryRunNote($"would write [cyan]{Markup.Escape(Path.GetRelativePath(apiDir, UiEject.PathFor(apiDir, imports)))}[/] (imports the view compiles with)");
                        }

                        written++;
                        break;
                    case UiEjectOutcome.Written:
                        Ux.Success($"Ejected {view.Target}", relative);
                        if (imports is not null)
                        {
                            Ux.Success("Added the imports it compiles with", Path.GetRelativePath(apiDir, UiEject.PathFor(apiDir, imports)));
                        }

                        written++;
                        break;
                    default:
                        Ux.Warning($"{relative} already exists (use --force to overwrite it with the framework's current view).");
                        break;
                }
            }

            if (views.Any(v => v.Kind == UiViewKind.Component) && UiEject.ViewImportsLackModulusUi(apiDir))
            {
                Ux.Warning("Views/Shared/Modulus/_ViewImports.cshtml exists but does not import Modulus.UI; ejected views will not compile until it has " +
                    "`@using global::Modulus.UI` and `@addTagHelper *, Modulus.UI.Core`.");
            }

            if (written > 0 && !Ux.DryRun)
            {
                Ux.Info("The app copy now takes precedence over the package's. Run `modulus ui diff` after a framework upgrade to see what changed.");
                if (views.Any(v => v.Kind == UiViewKind.Page))
                {
                    Ux.Info("A page's handlers (its PageModel) stay in the package: you own the markup, and behavior is still changed through the services the page uses.");
                }
            }

            return 0;
        });
    }

    /// <summary>The views to eject, imports first (so a view's own imports are already there when it is written), without duplicates.</summary>
    internal static List<UiView> Select(string apiDir, Settings s)
    {
        var views = new List<UiView>();

        if (s.All)
        {
            views.AddRange(UiViewCatalog.All.Where(v => v.Kind == UiViewKind.Component && string.Equals(v.View, s.View, StringComparison.OrdinalIgnoreCase)));
            views.AddRange(UiViewCatalog.All.Where(v => v.Kind != UiViewKind.Component && UiEject.IsInstalled(apiDir, v)));
        }

        foreach (var target in s.Targets)
        {
            var matched = UiViewCatalog.Match(target);

            // A component name means its --view (default: Default); "Card:Compact" already named one.
            if (matched[0].Kind == UiViewKind.Component && !target.Contains(':', StringComparison.Ordinal))
            {
                matched = [UiViewCatalog.Find(matched[0].Component, s.View)];
            }

            var missing = matched.Where(v => !UiEject.IsInstalled(apiDir, v)).Select(v => v.Component).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (missing is not null)
            {
                var view = matched.First(v => string.Equals(v.Component, missing, StringComparison.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    $"'{missing}' is not installed in this app (no reference to {UiEject.PackageFor(view)}), so there is nothing to override. " +
                    $"Run `modulus ui add {missing}` first.");
            }

            views.AddRange(matched);
        }

        return views
            .DistinctBy(v => (v.Kind, v.Component.ToUpperInvariant(), v.View.ToUpperInvariant()))
            .OrderBy(v => v.IsViewImports ? 0 : 1)
            .ToList();
    }

    private static int List(string apiDir)
    {
        var overridden = UiDiff.Compare(apiDir);
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Target").AddColumn("Kind").AddColumn("Views").AddColumn("In this app");

        foreach (var component in UiViewCatalog.Components)
        {
            var views = UiViewCatalog.ViewsOf(component);
            table.AddRow(
                $"[cyan]{Markup.Escape(component)}[/]",
                "component",
                Markup.Escape(string.Join(", ", views.Select(v => v.View))),
                overridden.Any(d => string.Equals(d.Framework.Component, component, StringComparison.OrdinalIgnoreCase) && d.Framework.Kind == UiViewKind.Component)
                    ? "[green]ejected[/]"
                    : "[grey]framework default[/]");
        }

        foreach (var group in UiViewCatalog.Groups)
        {
            var views = UiViewCatalog.ViewsOf(group);
            var count = overridden.Count(d => string.Equals(d.Framework.Component, group, StringComparison.OrdinalIgnoreCase));
            var status = !UiEject.IsInstalled(apiDir, views[0])
                ? "[grey]not installed[/]"
                : count == 0 ? "[grey]framework default[/]" : $"[green]{count} of {views.Count} ejected[/]";
            table.AddRow(
                $"[cyan]{Markup.Escape(group)}[/]",
                views[0].Kind == UiViewKind.Theme ? "theme" : group == "Shared" ? "shared partials" : "feature UI",
                $"{views.Count} (e.g. {Markup.Escape(views.First(v => !v.IsViewImports).Target)})",
                status);
        }

        AnsiConsole.Write(table);
        Ux.Info("Eject a whole group (`ui eject Users`) or one view (`ui eject Users/Details`, `ui eject Tabler/Layouts/Application`).");
        return 0;
    }
}
