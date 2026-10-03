namespace Modulus.Realtime.Transports;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Realtime.Delivery;

/// <summary>Builds a connection from the request that opened it.</summary>
internal static class RealtimeConnectionFactory
{
    public const int MaxTypeFilters = 50;

    public static RealtimeConnection Create(HttpContext context, string id, string transport, IReadOnlyList<string>? types, int capacity)
    {
        var tenant = context.RequestServices.GetService<ICurrentTenant>();
        var info = tenant?.TenantId is { } tenantId ? new TenantInfo(tenantId, tenant.TenantSlug ?? string.Empty) : null;
        return new RealtimeConnection(id, transport, context.User, info, types, capacity);
    }

    /// <summary>Comma-separated values (several query values are joined), trimmed, without empties or duplicates.</summary>
    public static List<string> SplitList(IEnumerable<string?> values)
        => [.. values
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)];

    /// <summary>When the caller's access token expires: the authentication ticket's expiry, else the <c>exp</c> claim.</summary>
    public static DateTimeOffset? TokenExpiry(HttpContext context)
    {
        if (context.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties?.ExpiresUtc is { } expires)
            return expires;
        return long.TryParse(context.User.FindFirst("exp")?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
