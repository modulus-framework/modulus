namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;

/// <summary>
/// The database a module's tenant documents live in, for the ambient tenant of the scope. With
/// <c>AddMongoDatabase</c> it is the one shared database (isolation comes from <see cref="TenantScopedCollection{T}"/>);
/// with <c>AddMongoDatabasePerTenant</c> each company has its own database and the host context gets the host database.
/// Module contexts take it (<c>ModuleMongoContext(ITenantMongoDatabase, ...)</c>) so the same module runs in either tier.
/// </summary>
/// <remarks>
/// The database is chosen when the scope first resolves it, like an EF Core context registered per tenant: enter the
/// tenant before resolving the module context. Infrastructure collections (outbox, inbox) stay in the host database,
/// the registered <see cref="IMongoDatabase"/>, and carry the tenant id per document.
/// </remarks>
public interface ITenantMongoDatabase
{
    /// <summary>The database of the ambient tenant (or the host database in the host context).</summary>
    IMongoDatabase Database { get; }
}

/// <summary>The shared database for every tenant.</summary>
internal sealed class SharedTenantMongoDatabase(IMongoDatabase database) : ITenantMongoDatabase
{
    public IMongoDatabase Database { get; } = database;
}

/// <summary>One database per tenant on the registered client; no tenant in scope throws instead of falling back.</summary>
internal sealed class PerTenantMongoDatabase : ITenantMongoDatabase
{
    public PerTenantMongoDatabase(IMongoClient client, ICurrentTenant tenant, string hostDatabase, Func<Guid, string> tenantDatabase)
        => Database = client.GetDatabase(Resolve(tenant, hostDatabase, tenantDatabase));

    public IMongoDatabase Database { get; }

    internal static string Resolve(ICurrentTenant tenant, string hostDatabase, Func<Guid, string> tenantDatabase)
    {
        if (tenant.IsHost)
            return hostDatabase;
        if (tenant.TenantId is not { } id)
        {
            throw new InvalidOperationException(
                "MongoDB keeps one database per tenant, and no tenant is in scope. Resolve a tenant, or enter the host "
                + "context (ICurrentTenant.Change(null)) for host work.");
        }

        var name = tenantDatabase(id);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, hostDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException($"The database name of tenant {id} must be set and differ from the host database.");
        return name;
    }
}
