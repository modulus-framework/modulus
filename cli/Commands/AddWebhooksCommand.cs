using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-webhooks [--events a,b]</c>: adds outgoing webhooks to an app. It generates the store module
/// (<c>src/Modules/{App}.Modules.Webhooks/{App}.Modules.Webhooks.Infrastructure</c>: <c>AppWebhooksDbContext</c>, its
/// design-time factory and <c>WebhooksModule</c>), wires the API host (the module, <c>AddModulusWebhooks</c> with one
/// <c>AddEvent</c> per integration event, the Admin grant, <c>MapModulusWebhooks</c>, settings) and adds a test class.
/// Existing files are never overwritten and edits are idempotent, so running it again only adds events created since.
/// </summary>
internal sealed class AddWebhooksCommand : Command<AddWebhooksCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("Integration events to expose, by event name or type name (comma-separated). Default: every [IntegrationEventName] event in the modules' Application/IntegrationEvents.")]
        [CommandOption("--events")]
        public string? Events { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddWebhooksCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (app.Kind == AppKind.WebApp)
            throw new InvalidOperationException("A web app maps no API surface, so it has nowhere to serve the webhook management API. Use an api or webapp+api app.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var events = SelectEvents(WebhooksWiring.FindIntegrationEvents(app.SolutionDir), s.Events);
        var program = File.ReadAllText(app.ProgramCsPath);
        var hasAdminRole = UiAccessGates.HasAdminRole(program);
        var grantsAdmin = hasAdminRole && program.Contains("AddModulusAuthorization(", StringComparison.Ordinal);
        var provider = app.Modules.Select(m => m.DatabaseProvider).FirstOrDefault(p => NewAppCommand.KnownProviders.Contains(p)) ?? "SQLite";
        var model = new WebhooksModel
        {
            RootNamespace = app.RootNamespace,
            DbProvider = provider,
            Events = events,
            HasAdminRole = hasAdminRole,
        };

        var written = new List<string>();
        var hints = new List<string>();

        // ── Store module ────────────────────────────────────────────
        var moduleDir = Path.Combine(app.SolutionDir, "src", "Modules", model.WebhooksNamespace);
        var infraName = $"{model.WebhooksNamespace}.Infrastructure";
        var infraDir = Path.Combine(moduleDir, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        Write("webhooks/infrastructure.csproj", model, infraCsproj, written, app);
        Write("webhooks/AppWebhooksDbContext", model, Path.Combine(infraDir, "AppWebhooksDbContext.cs"), written, app);
        Write("webhooks/AppWebhooksDbContextFactory", model, Path.Combine(infraDir, "AppWebhooksDbContextFactory.cs"), written, app);
        Write("webhooks/WebhooksModule", model, Path.Combine(infraDir, "WebhooksModule.cs"), written, app);
        if (!Ux.DryRun)
            SolutionHelper.AddProject(app.SolutionPath, Path.GetRelativePath(app.SolutionDir, infraCsproj));

        // ── API host ────────────────────────────────────────────────
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var reference = Path.GetRelativePath(apiDir, infraCsproj).Replace('\\', '/');
        Edit(app.ApiProjectPath, t => WebhooksWiring.EnsureApiProjectReference(t, reference), written, app);
        if (!Edit(app.ProgramCsPath, t => WebhooksWiring.EnsureApiProgram(t, model.WebhooksNamespace, events, grantsAdmin ? UiAccessGates.AdminRole : null), written, app)
            && !program.Contains("MapModulusWebhooks(", StringComparison.Ordinal))
        {
            hints.Add("Program.cs: add modules.AddModule<WebhooksModule>(), builder.Services.AddModulusWebhooks(builder.Configuration, webhooks => { webhooks.AddEvent<...>(); }) before Build() and app.MapModulusWebhooks().");
        }

        if (hasAdminRole && !grantsAdmin)
            hints.Add("Grant webhooks:manage to the administrators: builder.Services.AddModulusAuthorization() and AddPermissionGrants(grants => grants.GrantToRole(\"Admin\", \"webhooks:manage\")).");
        else if (!hasAdminRole)
            hints.Add("No Admin role in this host: callers need a \"permission\" claim of webhooks:manage (or a grant through AddModulusAuthorization) to manage subscriptions.");

        Edit(Path.Combine(apiDir, "appsettings.json"), t => WebhooksWiring.EnsureSettings(t, "Production", model.WebhooksConnectionString), written, app);
        Edit(Path.Combine(apiDir, "appsettings.Development.json"), t => WebhooksWiring.EnsureSettings(t, "Development", null), written, app);

        // ── Tests ───────────────────────────────────────────────────
        var testsDir = Path.Combine(app.SolutionDir, "tests", $"{app.RootNamespace}.Tests");
        if (Directory.Exists(testsDir))
        {
            var testingSettings = Path.Combine(apiDir, "appsettings.Testing.json");
            if (!File.Exists(testingSettings))
            {
                Ux.WriteFile(testingSettings, "{\n}\n");
                written.Add(Path.GetRelativePath(app.SolutionDir, testingSettings).Replace('\\', '/'));
            }

            Edit(testingSettings, t => WebhooksWiring.EnsureSettings(t, "Testing", null), written, app);
            Write("webhooks/WebhookTests", model, Path.Combine(testsDir, "WebhookTests.cs"), written, app);
        }

        AnsiConsole.MarkupLine("[green]✓[/] Webhooks with {0} event(s)", events.Count);
        foreach (var e in events)
            AnsiConsole.MarkupLine("  [cyan]{0}[/] [grey]({1})[/]", Markup.Escape(e.Name), Markup.Escape(e.TypeName));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        if (events.Count == 0)
            AnsiConsole.MarkupLine("  [yellow]![/] No [[IntegrationEventName]] event found: declare one in a module's Application/IntegrationEvents and run modulus add-webhooks again.");
        AnsiConsole.MarkupLine("  [grey]An event reaches subscribers once a module publishes it (IModuleBus, or the outbox). Manage subscriptions at /api/webhooks.[/]");
        AnsiConsole.MarkupLine("  [grey]Signing secrets are encrypted with ASP.NET Data Protection: persist and share its key ring in production.[/]");
        return 0;
    }

    /// <summary>Every event, or the ones <paramref name="option"/> names (by event name or type name).</summary>
    internal static IReadOnlyList<WebhooksWiring.IntegrationEventInfo> SelectEvents(
        IReadOnlyList<WebhooksWiring.IntegrationEventInfo> events, string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return events;

        var selected = new List<WebhooksWiring.IntegrationEventInfo>();
        foreach (var wanted in option.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = events.FirstOrDefault(e => string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(e.TypeName, wanted, StringComparison.Ordinal))
                ?? throw new ArgumentException(events.Count == 0
                    ? $"Unknown event '{wanted}': the modules declare no [IntegrationEventName] event."
                    : $"Unknown event '{wanted}'. Events: {string.Join(", ", events.Select(e => e.Name))}.");
            if (!selected.Contains(match))
                selected.Add(match);
        }

        return selected;
    }

    internal sealed class WebhooksModel
    {
        public string RootNamespace { get; init; } = "";
        public string DbProvider { get; init; } = "SQLite";
        public IReadOnlyList<WebhooksWiring.IntegrationEventInfo> Events { get; init; } = [];
        public bool HasAdminRole { get; init; }
        public string AdminRole => UiAccessGates.AdminRole;
        public string WebhooksNamespace => $"{RootNamespace}.Modules.Webhooks";
        public string FrameworkVersion => Services.FrameworkVersion.Current;
        public string EfProviderPackage => DbProviderInfo.Package(DbProvider);
        public string EfProviderVersion => DbProviderInfo.Version(DbProvider);
        public string UseDbMethod => DbProviderInfo.UseMethod(DbProvider);
        public string WebhooksConnectionString => DbProviderInfo.ConnectionString(DbProvider, "Webhooks");
    }

    private void Write(string template, WebhooksModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        _templates.RenderToFile(template, model, path);
        written.Add(display);
    }

    // Applies a pure text edit; true when the file changed (and was written, unless --dry-run).
    private static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return false;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return false;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
        return true;
    }
}
