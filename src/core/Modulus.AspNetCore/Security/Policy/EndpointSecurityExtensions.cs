namespace Modulus.AspNetCore.Security.Policy;

using Microsoft.AspNetCore.Builder;
using Modulus.Core.Abstractions.Security;

/// <summary>Declares an endpoint's security facts on minimal APIs and route groups.</summary>
public static class EndpointSecurityExtensions
{
    /// <summary>
    /// Opens the endpoint to anonymous callers and records why. Use instead of a bare
    /// <c>AllowAnonymous()</c>: the startup security guard rejects an anonymous endpoint without a
    /// reason, and reports this one (checked against the loosening allow-list).
    /// </summary>
    /// <param name="builder">The endpoint or group.</param>
    /// <param name="reason">Why anonymous access is needed.</param>
    /// <param name="ticket">The review or change ticket that approved it, if any.</param>
    public static TBuilder Loosen<TBuilder>(this TBuilder builder, string reason, string? ticket = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return builder
            .WithMetadata(new LoosenedAttribute(reason) { Ticket = ticket })
            .AllowAnonymous();
    }

    /// <summary>Opens the endpoint to anonymous callers with a prepared <paramref name="loosening"/> (reason, ticket).</summary>
    public static TBuilder Loosen<TBuilder>(this TBuilder builder, LoosenedAttribute loosening)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(loosening);

        return builder.WithMetadata(loosening).AllowAnonymous();
    }

    /// <summary>Declares the endpoint's data classification and network requirement.</summary>
    public static TBuilder WithSecurityPolicy<TBuilder>(
        this TBuilder builder,
        DataClassification classification,
        NetworkRequirement network = NetworkRequirement.Any)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new EndpointSecurityPolicyAttribute
        {
            Classification = classification,
            Network = network,
        });
    }
}
