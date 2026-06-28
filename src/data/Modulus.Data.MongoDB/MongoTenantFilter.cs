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
    /// When no tenant is in scope (host / background context without a tenant
    /// scope) this returns <see cref="FilterDefinition{T}.Empty"/> — i.e. no
    /// restriction (host sees all) — rather than filtering on
    /// <c>Guid.Empty</c>, which previously surfaced orphan documents and hid
    /// legitimate host data.
    /// </para>
    /// </summary>
    public static FilterDefinition<T> For<T>(
        ICurrentTenant tenant)
        where T : IHasTenantId
        => tenant.TenantId is { } id
            ? Builders<T>.Filter.Eq(x => x.TenantId, id)
            : Builders<T>.Filter.Empty;

    public static FilterDefinition<T> And<T>(
        ICurrentTenant tenant,
        FilterDefinition<T> additional)
        where T : IHasTenantId
        => Builders<T>.Filter.And(For<T>(tenant), additional);
}