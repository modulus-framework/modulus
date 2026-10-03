namespace Modulus.AspNetCore.Security.Policy;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The reviewed list of endpoints that may be anonymous (<c>security/loosening-allowlist.json</c>):
/// <code>
/// { "endpoints": [ { "route": "/health/live", "methods": ["GET"], "reason": "Liveness probe" },
///                  { "route": "/connect/*", "reason": "OpenID Connect protocol endpoints" } ] }
/// </code>
/// A route ending in <c>*</c> matches every route with that prefix. Without <c>methods</c> an entry
/// covers every method. An entry's <c>reason</c> also explains an anonymous endpoint that carries no
/// reason of its own (a third-party library's).
/// </summary>
public sealed class LooseningAllowList
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>An empty list.</summary>
    public static LooseningAllowList Empty { get; } = new();

    /// <summary>The listed endpoints.</summary>
    [JsonPropertyName("endpoints")]
    public IList<LooseningAllowListEntry> Endpoints { get; init; } = [];

    /// <summary>Reads the list from <paramref name="path"/>; a missing file is an empty list.</summary>
    /// <exception cref="InvalidOperationException">The file is not valid JSON.</exception>
    public static LooseningAllowList Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return Empty;

        try
        {
            return JsonSerializer.Deserialize<LooseningAllowList>(File.ReadAllText(path), s_json) ?? Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The loosening allow-list '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>The first entry covering <paramref name="route"/> with every method in <paramref name="methods"/>.</summary>
    public LooseningAllowListEntry? Find(string route, IReadOnlyCollection<string> methods)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(methods);

        var normalized = Normalize(route);
        return Endpoints.FirstOrDefault(e => e.Covers(normalized, methods));
    }

    internal static string Normalize(string route)
    {
        var r = route.Trim();
        if (!r.StartsWith('/'))
            r = "/" + r;
        return r.Length > 1 ? r.TrimEnd('/') : r;
    }
}

/// <summary>One allow-listed route.</summary>
public sealed class LooseningAllowListEntry
{
    /// <summary>The route pattern as mapped (<c>/health/live</c>, <c>/products/{id}</c>), or a prefix ending in <c>*</c>.</summary>
    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    /// <summary>The HTTP methods covered; empty covers every method.</summary>
    [JsonPropertyName("methods")]
    public IList<string> Methods { get; init; } = [];

    /// <summary>Why the route may be anonymous.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>The approving ticket, if any.</summary>
    [JsonPropertyName("ticket")]
    public string? Ticket { get; init; }

    internal bool Covers(string normalizedRoute, IReadOnlyCollection<string> methods)
    {
        if (string.IsNullOrWhiteSpace(Route))
            return false;

        var pattern = Route.Trim();
        bool routeMatches;
        if (pattern.EndsWith('*'))
        {
            // "/connect/*" covers "/connect" and "/connect/…", never "/connectx".
            var prefix = LooseningAllowList.Normalize(pattern[..^1]);
            routeMatches = prefix == "/"
                || string.Equals(normalizedRoute, prefix, StringComparison.OrdinalIgnoreCase)
                || normalizedRoute.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            routeMatches = string.Equals(LooseningAllowList.Normalize(pattern), normalizedRoute, StringComparison.OrdinalIgnoreCase);
        }

        if (!routeMatches)
            return false;
        if (Methods.Count == 0)
            return true;

        // An endpoint without method metadata answers every method, so only an all-methods entry covers it.
        return methods.Count > 0
            && methods.All(m => Methods.Any(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase)));
    }
}
