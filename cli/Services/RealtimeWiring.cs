using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edits <c>modulus add-realtime</c> makes to existing files: the API host's Program.cs (<c>AddModulusRealtime</c> with
/// one <c>AddEvent</c> per pushed integration event, <c>MapModulusRealtime</c>), its settings, and a BFF's <c>/realtime</c>
/// event-stream route. Every edit is idempotent and keeps the file's line endings; re-running adds events created since.
/// </summary>
internal static partial class RealtimeWiring
{
    public const string PackageId = "Cobytelabs.Modulus.Realtime";

    /// <summary>An integration event to push, and the permission its recipients need (null: everyone in the tenant).</summary>
    internal sealed record RealtimeEventInfo(WebhooksWiring.IntegrationEventInfo Event, string? Permission)
    {
        public string Audience => Permission is null
            ? "RealtimeAudience.Tenant"
            : $"RealtimeAudience.Permission(\"{Permission}\")";
    }

    /// <summary>
    /// The CRUD permission of the entity an event is about: <c>ProductCreatedIntegrationEvent</c> in module Catalog →
    /// the permission on <c>ProductsEndpoint.cs</c> (<c>catalog:products:manage</c>); null when it cannot be told.
    /// </summary>
    public static string? PermissionFor(WebhooksWiring.IntegrationEventInfo info, IReadOnlyList<ModuleDiscovery.ModuleSummary> modules)
    {
        var match = EntityEvent().Match(info.TypeName);
        var module = ModuleOf().Match(info.Namespace);
        if (!match.Success || !module.Success)
            return null;

        var entity = match.Groups["entity"].Value;
        var owner = modules.FirstOrDefault(m => string.Equals(m.Name, module.Groups[1].Value, StringComparison.Ordinal));
        if (owner is null)
            return null;
        var endpoint = Path.Combine(CodeGen.LayerDir(owner.Directory, owner.Namespace, "Presentation"), $"{CodeGen.Pluralize(entity)}Endpoint.cs");
        return File.Exists(endpoint) ? Commands.GenerateGrpcCommand.ReadPermission(File.ReadAllText(endpoint)) : null;
    }

    /// <summary>
    /// Program.cs: <c>AddModulusRealtime</c> before <c>Build()</c> with every event not yet added, and
    /// <c>MapModulusRealtime()</c> after the endpoints (else before <c>app.Run()</c>).
    /// </summary>
    public static string EnsureApiProgram(string program, IReadOnlyList<RealtimeEventInfo> events)
    {
        var nl = WebhooksWiring.NewLine(program);
        var text = program;

        if (!text.Contains("AddModulusRealtime(", StringComparison.Ordinal))
        {
            var build = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (build < 0)
                return program;
            text = text[..build] +
                "// ── Realtime ───────────────────────────────────────────────────" + nl +
                "// Integration events pushed to connected clients (modulus add-realtime; re-run it to add events" + nl +
                "// created since), each to the audience it names, within the event's tenant. Clients read the SSE" + nl +
                "// stream GET /realtime/events (EventSource; resumes with Last-Event-ID); SignalR (/realtime/hub, for" + nl +
                "// topic subscriptions and two-way calls) is on when \"Realtime:SignalR:Enabled\". With more than one" + nl +
                "// replica, add the Redis backplane (Cobytelabs.Modulus.Realtime.Redis: AddRedisRealtimeBackplane)." + nl +
                "builder.Services.AddModulusRealtime(builder.Configuration, realtime =>" + nl +
                "{" + nl +
                "});" + nl + nl +
                text[build..];
        }

        text = EnsureEvents(text, events);

        if (!text.Contains("MapModulusRealtime(", StringComparison.Ordinal))
        {
            var block = "// Realtime endpoints: GET /realtime/events (SSE) and, when enabled, the /realtime/hub SignalR hub." + nl +
                        "app.MapModulusRealtime();" + nl;
            var endpoints = text.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal);
            var end = endpoints < 0 ? -1 : text.IndexOf(".ToArray());", endpoints, StringComparison.Ordinal);
            var lineEnd = end < 0 ? -1 : text.IndexOf('\n', end);
            if (lineEnd >= 0)
            {
                text = text[..(lineEnd + 1)] + nl + block + text[(lineEnd + 1)..];
            }
            else
            {
                var run = text.IndexOf("app.Run();", StringComparison.Ordinal);
                if (run < 0)
                    return program;
                text = text[..run] + block + nl + text[run..];
            }
        }

        text = WebhooksWiring.EnsureUsing(text, "using Modulus.Realtime;");
        foreach (var ns in events.Select(e => e.Event.Namespace).Distinct(StringComparer.Ordinal))
            text = WebhooksWiring.EnsureUsing(text, $"using {ns};");
        return text;
    }

    // Adds `realtime.AddEvent<T>(_ => audience);` for each event not yet in the AddModulusRealtime callback.
    private static string EnsureEvents(string text, IReadOnlyList<RealtimeEventInfo> events)
    {
        var call = text.IndexOf("AddModulusRealtime(", StringComparison.Ordinal);
        var open = call < 0 ? -1 : text.IndexOf("realtime =>", call, StringComparison.Ordinal);
        var close = open < 0 ? -1 : text.IndexOf("});", open, StringComparison.Ordinal);
        if (close < 0)
            return text;

        var nl = WebhooksWiring.NewLine(text);
        var missing = events.Where(e => !text.Contains($"realtime.AddEvent<{e.Event.TypeName}>", StringComparison.Ordinal)).ToList();
        if (missing.Count == 0)
            return text;

        var lineStart = text.LastIndexOf('\n', close) + 1;
        var lines = string.Concat(missing.Select(e => $"    realtime.AddEvent<{e.Event.TypeName}>(_ => {e.Audience});{nl}"));
        return text[..lineStart] + lines + text[lineStart..];
    }

    /// <summary><c>appsettings.json</c> gets a <c>Realtime</c> section (sign-in required, SSE on, SignalR as chosen).</summary>
    public static string EnsureApiSettings(string json, bool signalR)
        => WebhooksWiring.EnsureTopLevelSection(json, "Realtime",
        [
            "\"Path\": \"/realtime\",",
            "\"RequireAuthenticatedUser\": true,",
            "\"Sse\": { \"Enabled\": true, \"HeartbeatInterval\": \"00:00:15\" },",
            $"\"SignalR\": {{ \"Enabled\": {(signalR ? "true" : "false")} }},",
            "\"ReplayBufferSize\": 512,",
            "\"MaxConnectionsPerUser\": 20",
        ]);

    /// <summary>A BFF client routes <c>/realtime</c> to the API as an event stream (EventSource reads pass without the CSRF header).</summary>
    public static string EnsureBffRemoteApi(string json, string service = "api")
        => GraphQLWiring.EnsureRemoteApi(json, "/realtime", service, eventStream: true);

    [GeneratedRegex("^(?<entity>[A-Z][A-Za-z0-9]*?)(Created|Updated|Deleted|Changed|Removed|Added)IntegrationEvent$")]
    private static partial Regex EntityEvent();

    [GeneratedRegex(@"\.Modules\.([A-Za-z0-9_]+)\.")]
    private static partial Regex ModuleOf();
}
