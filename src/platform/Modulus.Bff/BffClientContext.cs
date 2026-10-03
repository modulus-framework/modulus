namespace Modulus.Bff;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;

/// <summary>Endpoint metadata naming the BFF client an endpoint belongs to.</summary>
public sealed record BffClientMetadata(string Name, BffClientKind Kind);

/// <summary>Endpoint metadata that exempts an endpoint from the web client's CSRF header check.</summary>
public sealed class BffSkipCsrfMetadata
{
    public static readonly BffSkipCsrfMetadata Instance = new();
}

/// <summary>
/// Endpoint metadata of a remote API marked <see cref="BffRemoteApiOptions.EventStream"/>: the web client accepts a
/// <c>GET</c> asking for <c>text/event-stream</c> without the CSRF header, since a browser <c>EventSource</c> cannot send
/// headers. Such a request only reads, and the SameSite session cookie is not sent cross-site, so no other site can use it.
/// </summary>
public sealed class BffEventStreamMetadata
{
    public static readonly BffEventStreamMetadata Instance = new();
}

/// <summary>
/// The BFF client serving the current request: the endpoint's <see cref="BffClientMetadata"/>, else the host's
/// default client (<see cref="BffBuilder.SetDefaultClient"/>, e.g. a Razor Pages host that is one web client).
/// </summary>
public interface IBffClientContext
{
    /// <summary>The client name, or <c>null</c> outside a BFF endpoint in a host without a default client.</summary>
    string? Name { get; }

    /// <summary>The client kind, or <c>null</c> when <see cref="Name"/> is.</summary>
    BffClientKind? Kind { get; }

    /// <summary>The client's settings, or <c>null</c> when <see cref="Name"/> is.</summary>
    BffClientOptions? Options { get; }
}

/// <summary>Reads the current client from the request's endpoint, so it also works inside outbound handlers.</summary>
internal sealed class BffClientContext(IHttpContextAccessor accessor, IOptionsMonitor<BffClientOptions> clients, BffDefaultClient? defaultClient = null)
    : IBffClientContext
{
    private BffClientMetadata? Metadata => accessor.HttpContext is { } context
        ? context.GetEndpoint()?.Metadata.GetMetadata<BffClientMetadata>()
            ?? (defaultClient is { } d ? new BffClientMetadata(d.Name, clients.Get(d.Name).Kind) : null)
        : null;

    public string? Name => Metadata?.Name;

    public BffClientKind? Kind => Metadata?.Kind;

    public BffClientOptions? Options => Metadata is { } m ? clients.Get(m.Name) : null;

    /// <summary>The tag stamped on <see cref="Activity.Current"/> for every BFF request.</summary>
    public const string ActivityTag = "bff.client";
}

/// <summary>The client a host serves outside BFF endpoints (<see cref="BffBuilder.SetDefaultClient"/>).</summary>
internal sealed record BffDefaultClient(string Name);
