namespace Modulus.Core.Abstractions.Security;

/// <summary>How callers of one item of a non-HTTP security surface (a GraphQL field, a realtime topic) are checked.</summary>
public enum SecuritySurfaceAccess
{
    /// <summary>The item declares its own policy, permission, role or authorization callback.</summary>
    Policed,

    /// <summary>No check of its own; the hosting endpoint's requirement (a signed-in caller) applies.</summary>
    Inherited,

    /// <summary>Neither the item nor its hosting endpoint checks the caller.</summary>
    Anonymous,
}

/// <summary>One GraphQL field, realtime topic or similar item, as the startup security guard reports it.</summary>
/// <param name="Surface">The surface (<c>graphql</c>, <c>realtime</c>).</param>
/// <param name="Name">The item (<c>Query.products</c>, <c>topic orders:*</c>).</param>
/// <param name="Access">How its callers are checked.</param>
/// <param name="Policies">The policies, permissions or roles it requires (<c>(callback)</c> for code-only checks).</param>
public sealed record SecuritySurfaceEntry(string Surface, string Name, SecuritySurfaceAccess Access, IReadOnlyList<string> Policies);

/// <summary>
/// Describes a security surface the HTTP endpoint list does not show: one endpoint (<c>/graphql</c>, <c>/realtime</c>)
/// carries many fields or topics, each with its own authorization. Packages register one per surface and the startup
/// security guard (<c>AddModulusSecurityGuard</c>) adds the entries to its report, logs the anonymous ones and includes
/// them in the loosening fingerprint it records in the security audit.
/// </summary>
public interface ISecuritySurfaceContributor
{
    /// <summary>Lists the surface's items. Called once, after the host has started.</summary>
    /// <param name="services">The root service provider.</param>
    IEnumerable<SecuritySurfaceEntry> Describe(IServiceProvider services);
}
