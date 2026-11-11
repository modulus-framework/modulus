using Microsoft.AspNetCore.Builder;

namespace Modulus.MultiTenancy;

/// <summary>
/// Endpoint metadata that asks <see cref="TenantMiddleware"/> to run the endpoint in the host context: a platform-operator route
/// (a partner client's company binding, for example) acts for the whole installation, whatever company the caller selected.
/// The middleware still refuses a caller who selected a company they may not enter, and the endpoint's own permission still decides.
/// </summary>
public sealed class HostTenantContextMetadata
{
    /// <summary>The one instance the endpoint extension adds.</summary>
    public static HostTenantContextMetadata Instance { get; } = new();

    private HostTenantContextMetadata()
    {
    }
}

/// <summary>Marks endpoints as platform-operator routes that run in the host context (see <see cref="HostTenantContextMetadata"/>).</summary>
public static class HostTenantContextEndpointExtensions
{
    /// <summary>Runs the route group or endpoint in the host context. Authorization and the usual checks still apply.</summary>
    public static TBuilder RequireHostTenantContext<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(HostTenantContextMetadata.Instance);
        return builder;
    }
}
