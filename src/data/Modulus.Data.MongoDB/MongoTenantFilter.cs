namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

public static class MongoTenantFilter
{
    /// <summary>
    /// Returns a filter that restricts queries to the current tenant.
    /// Apply on EVERY query in every repository method.
    /// </summary>
    public static FilterDefinition<T> For<T>(
        ICurrentTenant tenant)
        where T : IHasTenantId
    {
        var tenantId = tenant.TenantId ?? Guid.Empty;
        return Builders<T>.Filter.Eq(x => x.TenantId, tenantId);
    }

    public static FilterDefinition<T> And<T>(
        ICurrentTenant tenant,
        FilterDefinition<T> additional)
        where T : IHasTenantId
        => Builders<T>.Filter.And(For<T>(tenant), additional);
}