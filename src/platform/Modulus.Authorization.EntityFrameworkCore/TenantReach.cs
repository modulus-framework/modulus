using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Which companies' authorization rows a read may see (the companies whose grants a company inherits). The host reads through the query
/// filter, which lets it see everything, so it has no reach to pass on (null). A resolved company reads its own rows and those of the
/// companies above it. An unresolved company reads nothing.
/// </summary>
internal static class TenantReach
{
    /// <summary>The reach of <paramref name="tenant"/>: null for the host, otherwise its lineage (empty when unresolved).</summary>
    public static IReadOnlyList<Guid>? Of(ICurrentTenant? tenant)
        => tenant is null || tenant.IsHost ? null : tenant.Lineage;
}
