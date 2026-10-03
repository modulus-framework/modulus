namespace Modulus.Realtime;

using System.ComponentModel.DataAnnotations;

/// <summary>Settings of the <c>Realtime</c> section.</summary>
public sealed class ModulusRealtimeOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Realtime";

    /// <summary>The base path: the SSE stream is <c>{Path}/events</c>, the SignalR hub <c>{Path}/hub</c>.</summary>
    [Required]
    public string Path { get; set; } = "/realtime";

    /// <summary>Only signed-in callers may connect (default). When false, anonymous callers receive tenant-wide messages without a permission.</summary>
    public bool RequireAuthenticatedUser { get; set; } = true;

    /// <summary>Server-Sent Events, the default transport for server-to-client pushes.</summary>
    public RealtimeSseOptions Sse { get; set; } = new();

    /// <summary>SignalR, for two-way connections (topic subscriptions while connected, app hub methods). Off by default.</summary>
    public RealtimeSignalROptions SignalR { get; set; } = new();

    /// <summary>Messages each node keeps so a reconnecting client resumes after its last event id. 0 turns replay off.</summary>
    [Range(0, 100_000)]
    public int ReplayBufferSize { get; set; } = 512;

    /// <summary>How long a message stays replayable.</summary>
    public TimeSpan ReplayWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Messages queued for one connection; a client that falls further behind is disconnected (it resumes on reconnect).</summary>
    [Range(1, 100_000)]
    public int MaxQueuedMessagesPerConnection { get; set; } = 256;

    /// <summary>Open connections per user on one node (<c>429</c> beyond it). 0 = unlimited.</summary>
    [Range(0, 10_000)]
    public int MaxConnectionsPerUser { get; set; } = 20;

    /// <summary>Topics one connection may follow.</summary>
    [Range(0, 10_000)]
    public int MaxTopicsPerConnection { get; set; } = 50;

    /// <summary>How long a connection's permission check result is reused before it is evaluated again (grants can change).</summary>
    public TimeSpan PermissionRecheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>End a connection when the caller's access token expires, so the client reconnects with a fresh one.</summary>
    public bool CloseAtTokenExpiry { get; set; } = true;
}

/// <summary>Server-Sent Events settings.</summary>
public sealed class RealtimeSseOptions
{
    /// <summary>Maps <c>GET {Path}/events</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>An event-less keep-alive is written after this much silence (keeps proxies from closing the stream).</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The reconnection delay sent to the client (<c>retry:</c>).</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>SignalR settings.</summary>
public sealed class RealtimeSignalROptions
{
    /// <summary>Maps the hub at <c>{Path}/hub</c>.</summary>
    public bool Enabled { get; set; }
}
