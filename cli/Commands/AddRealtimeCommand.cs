using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-realtime [--events a,b] [--bff web,mobile|all] [--signalr]</c>: pushes integration events to connected
/// clients. It wires the API host (<c>AddModulusRealtime</c> with one <c>AddEvent</c> per event, addressed to the holders of
/// the entity's CRUD permission when it can be told, else the whole tenant; <c>MapModulusRealtime</c>; the <c>Realtime</c>
/// settings), adds a test class and, with <c>--bff</c>, routes <c>/realtime</c> through those BFFs as an event stream. SSE is
/// the transport; <c>--signalr</c> also turns on the hub (topic subscriptions, two-way calls). Edits are idempotent, so
/// running it again only adds events created since.
/// </summary>
internal sealed class AddRealtimeCommand : Command<AddRealtimeCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("Integration events to push, by event name or type name (comma-separated). Default: every [IntegrationEventName] event in the modules' Application/IntegrationEvents.")]
        [CommandOption("--events")]
        public string? Events { get; init; }

        [Description("BFFs that relay /realtime to the API (web, mobile, partner; comma-separated, or all).")]
        [CommandOption("--bff")]
        public string? Bff { get; init; }

        [Description("Also enable the SignalR hub (/realtime/hub) for topic subscriptions and two-way calls. SSE stays the default transport.")]
        [CommandOption("--signalr")]
        public bool SignalR { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddRealtimeCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var bffs = GenerateGrpcCommand.SelectBffs(app.Bffs, s.Bff);
        var events = AddWebhooksCommand.SelectEvents(WebhooksWiring.FindIntegrationEvents(app.SolutionDir), s.Events)
            .Select(e => new RealtimeWiring.RealtimeEventInfo(e, RealtimeWiring.PermissionFor(e, app.Modules)))
            .ToList();
        var program = File.ReadAllText(app.ProgramCsPath);
        var model = new RealtimeModel
        {
            RootNamespace = app.RootNamespace,
            Events = events,
            HasAdminRole = UiAccessGates.HasAdminRole(program),
        };

        var written = new List<string>();
        var hints = new List<string>();

        // ── API host ────────────────────────────────────────────────
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, RealtimeWiring.PackageId, FrameworkVersion.Current, Ux.DryRun))
            written.Add(Path.GetRelativePath(app.SolutionDir, app.ApiProjectPath) + " (updated)");
        if (!Edit(app.ProgramCsPath, t => RealtimeWiring.EnsureApiProgram(t, events), written, app)
            && !program.Contains("MapModulusRealtime(", StringComparison.Ordinal))
        {
            hints.Add("Program.cs: add builder.Services.AddModulusRealtime(builder.Configuration, realtime => { realtime.AddEvent<...>(_ => RealtimeAudience...); }) before Build() and app.MapModulusRealtime().");
        }

        Edit(Path.Combine(apiDir, "appsettings.json"), t => RealtimeWiring.EnsureApiSettings(t, s.SignalR), written, app);

        // ── Tests ───────────────────────────────────────────────────
        var testsDir = Path.Combine(app.SolutionDir, "tests", $"{app.RootNamespace}.Tests");
        if (Directory.Exists(testsDir))
            Write("realtime/RealtimeTests", model, Path.Combine(testsDir, "RealtimeTests.cs"), written, app);

        // ── BFFs: /realtime relayed to the API as an event stream ───
        foreach (var bff in bffs)
        {
            if (!Edit(bff.AppSettings, t => RealtimeWiring.EnsureBffRemoteApi(t), written, app)
                && !(File.Exists(bff.AppSettings) && File.ReadAllText(bff.AppSettings).Contains("\"/realtime\"", StringComparison.Ordinal)))
            {
                hints.Add($"{bff.ProjectName}: add {{ \"LocalPath\": \"/realtime\", \"Service\": \"api\", \"EventStream\": true }} to Bff:Clients:<client>:RemoteApis.");
            }
        }

        AnsiConsole.MarkupLine("[green]✓[/] Realtime with {0} event(s)", events.Count);
        foreach (var e in events)
        {
            AnsiConsole.MarkupLine("  [cyan]{0}[/] [grey]→ {1}[/]", Markup.Escape(e.Event.Name),
                Markup.Escape(e.Permission is null ? "everyone in the tenant" : $"holders of {e.Permission}"));
        }

        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        if (events.Count == 0)
            AnsiConsole.MarkupLine("  [yellow]![/] No [[IntegrationEventName]] event found: push app messages with IRealtimePublisher, or declare an event and run modulus add-realtime again.");
        AnsiConsole.MarkupLine("  [grey]An event reaches clients once a module publishes it (IModuleBus, or the outbox); IRealtimePublisher pushes anything else.[/]");
        AnsiConsole.MarkupLine("  [grey]Browser: new EventSource(\"/realtime/events\") through the web BFF (same origin, session cookie); listen with addEventListener(\"<event name>\", ...).[/]");
        AnsiConsole.MarkupLine("  [grey]More than one API replica: add Cobytelabs.Modulus.Realtime.Redis and builder.Services.AddRedisRealtimeBackplane(builder.Configuration).[/]");
        return 0;
    }

    internal sealed class RealtimeModel
    {
        public string RootNamespace { get; init; } = "";
        public IReadOnlyList<RealtimeWiring.RealtimeEventInfo> Events { get; init; } = [];
        public bool HasAdminRole { get; init; }
        public string AdminRole => UiAccessGates.AdminRole;

        /// <summary>An event the test can raise (a <c>(Guid Id)</c> event addressed by permission), or null.</summary>
        public RealtimeWiring.RealtimeEventInfo? TestEvent => Events.FirstOrDefault(e => e.Event.IdOnly && e.Permission is not null);

        public bool HasTestEvent => TestEvent is not null;
        public string TestEventType => TestEvent?.Event.TypeName ?? "";
        public string TestEventName => TestEvent?.Event.Name ?? "";
        public string TestEventNamespace => TestEvent?.Event.Namespace ?? "";
        public string TestEventPermission => TestEvent?.Permission ?? "";
    }

    private void Write(string template, RealtimeModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
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
