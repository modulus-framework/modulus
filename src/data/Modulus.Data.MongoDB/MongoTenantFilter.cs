namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

public static class MongoTenantFilter
{
    /// <summary>
    /// Returns a filter that restricts queries to the current tenant.
    /// Apply on EVERY query in every repository method.
    /// <para>
    /// <b>Fail-closed</b>, mirroring the EF Core query filter:
    /// </para>
    /// <list type="bullet">
    /// <item>Host context (<see cref="ICurrentTenant.IsHost"/> — multi-tenancy
    /// off or an explicit <c>Change(null)</c> scope) →
    /// <see cref="FilterDefinition{T}.Empty"/> (sees all).</item>
    /// <item>A resolved tenant → equality on its id.</item>
    /// <item>Multi-tenancy on but no tenant resolved → a filter that matches
    /// <b>nothing</b>, so a missing/misconfigured tenant never leaks every
    /// tenant's documents.</item>
    /// </list>
    /// </summary>
    public static FilterDefinition<T> For<T>(
        ICurrentTenant tenant)
        where T : IHasTenantId
    {
        if (tenant.IsHost)
            return Builders<T>.Filter.Empty;

        return tenant.TenantId is { } id
            ? Builders<T>.Filter.Eq(x => x.TenantId, id)
            // Fail-closed: unresolved tenant matches no documents.
            : Builders<T>.Filter.Where(_ => false);
    }

    public static FilterDefinition<T> And<T>(
        ICurrentTenant tenant,
        FilterDefinition<T> additional)
        where T : IHasTenantId
        => Builders<T>.Filter.And(For<T>(tenant), additional);
}