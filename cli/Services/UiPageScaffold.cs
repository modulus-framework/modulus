using Spectre.Console;

namespace Modulus.Cli.Services;

/// <summary>A page the auth commands add: its template, the file name and (for MVC) the controller that serves it.</summary>
internal sealed record UiAuthPage(string Template, string PageName, string Route);

/// <summary>Shared by the commands that add pages with their own route (<c>ui add-auth</c>, <c>add-2fa</c>, <c>add-chart</c> ...): renders pages from the template package into the app's UI project.</summary>
internal static class UiPageScaffold
{
    public static readonly IReadOnlyList<UiAuthPage> Recovery =
    [
        new("forgot-password", "ForgotPassword", "/auth/forgot-password"),
        new("reset-password", "ResetPassword", "/auth/reset-password"),
    ];

    public static readonly IReadOnlyList<UiAuthPage> TwoFactor =
    [
        new("two-factor", "TwoFactor", "/auth/two-factor"),
    ];

    public static readonly IReadOnlyList<UiAuthPage> Sessions =
    [
        new("session-manager", "Sessions", "/auth/sessions"),
    ];

    public static int Run(IReadOnlyList<UiAuthPage> pages, string controllerTemplate, string controllerFile,
        string? engineOption, string? output, string what, string folder = "Auth", bool sharedController = false, object? model = null,
        bool callsApi = false, bool anonymous = false)
    {
        var start = Path.GetFullPath(output ?? "./");
        var inventory = ModuleDiscovery.Inventory(start)
            ?? throw new InvalidOperationException(
                "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
        if (inventory.Kind == AppKind.Api)
            throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to add {what} to.");

        var engine = UiScaffold.ResolveEngine(engineOption, inventory.SolutionDir);
        var templates = UiTemplatePackage.Resolve()
            ?? throw new InvalidOperationException(
                $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");

        var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
        var renderer = new TemplateEngine();

        // Anonymous pages (password recovery) must be reachable while signed out. Razor Pages puts them under /Account, where the
        // host already allows anonymous access by folder; MVC and Blazor open their routes below.
        if (anonymous && engine == "razor-pages")
            folder = "Account";

        // Resolve and check everything before writing anything.
        var plan = pages.Select(p => (Page: p,
                File: UiScaffold.TemplateFile(templates, engine, p.Template)
                      ?? throw new InvalidOperationException(
                          $"The {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package has no '{p.Template}' page for {engine}. " +
                          $"If it is newer than this package, set {UiTemplatePackage.VersionEnvironmentVariable} to a newer Modulus UI version."),
                Target: UiScaffold.OutputPath(engine, uiDir, folder, p.PageName)))
            .ToList();
        var controller = engine == "mvc" ? Path.Combine(uiDir, "Controllers", controllerFile) : null;
        // A shared controller (one for all the pages of a kind) is written once and then left alone.
        var existing = plan.Select(x => x.Target).Append(sharedController ? null : controller).OfType<string>().Where(File.Exists).ToList();
        if (anonymous && plan.All(x => File.Exists(x.Target)) && !Ux.Force)
        {
            // The recovery pages were already generated (the Razor Pages app creates them with its sign-in pages).
            Ux.Info($"{what[..1].ToUpperInvariant()}{what[1..]} are already in this app: {string.Join(", ", existing.Select(f => Path.GetRelativePath(start, f)))}. Nothing to add.");
            return 0;
        }
        if (existing.Count > 0 && !Ux.Force)
            throw new InvalidOperationException(
                $"Already present: {string.Join(", ", existing.Select(f => Path.GetRelativePath(start, f)))}. Pass --force to overwrite.");

        foreach (var (page, file, target) in plan)
        {
            renderer.RenderToFile(file, model ?? new { PageName = page.PageName }, target);
            Ux.Success($"{page.PageName} page added", $"{Path.GetRelativePath(start, target)}  →  {page.Route}");
        }

        var rootNamespace = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
        if (controller is not null && !(sharedController && File.Exists(controller) && !Ux.Force))
        {
            renderer.RenderToFile(controllerTemplate, new { Namespace = rootNamespace + ".Controllers" }, controller);
            Ux.Success("Controller added", Path.GetRelativePath(start, controller));
        }

        // Recovery routes are anonymous on purpose, so the Web host's loosening allowlist must name them (the security guard refuses
        // to start outside Development otherwise). Razor Pages reaches them through its /Account folder, which is a framework endpoint.
        if (anonymous && engine != "razor-pages")
        {
            var allowlist = Path.Combine(uiDir, "Security", "loosening-allowlist.json");
            // No method: the page answers GET (the form) and POST (the submit), and the guard matches the route with every method.
            var routes = pages.Select(p => (p.Route, (string?)null, $"{what}: {p.PageName}")).ToList();
            if (File.Exists(allowlist))
            {
                if (UiHostWiring.AddAllowlistEntries(allowlist, routes))
                    Ux.Success("Web host updated", Path.GetRelativePath(start, allowlist));
            }
            else
                Ux.Warning($"No Security/loosening-allowlist.json in {Path.GetRelativePath(start, uiDir)}; add {string.Join(", ", pages.Select(p => p.Route))} (GET) to the Web host's allowlist, or it will refuse to start outside Development.");
        }

        // The pages' scripts call /account/* and /authorization/* on their own host. On a split app those endpoints are in the
        // API host, so the Web host forwards them (with the signed-in user's token) and needs its controllers routed.
        var programPath = Path.Combine(uiDir, "Program.cs");
        var split = inventory.Kind == AppKind.WebAppApi && File.Exists(programPath);
        if (split && callsApi && UiAccessGates.HasSignIn(File.ReadAllText(programPath)))
        {
            foreach (var change in UiHostWiring.EnsureIdentityProxy(uiDir, programPath, rootNamespace, renderer))
                Ux.Success("Web host updated", change);
        }

        if (split && (controller is not null || callsApi) && File.Exists(programPath))
        {
            foreach (var change in UiHostWiring.EnsureControllerRouting(programPath))
                Ux.Success("Web host updated", change);
        }

        if (split && callsApi && !UiAccessGates.HasSignIn(File.ReadAllText(programPath)))
            Ux.Warning("This Web host has no sign-in, so the pages' calls to /account/* and /authorization/* have no token and will be refused. Add a sign-in first (ui add-auth needs one).");
        else if (!split && callsApi)
            Ux.Info("The pages call the Modulus.Identity /account/* endpoints on the same host (the cookie session is used for 2FA).");
        return 0;
    }
}
