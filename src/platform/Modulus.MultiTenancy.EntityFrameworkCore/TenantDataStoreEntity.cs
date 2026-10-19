namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Modulus.Core.Abstractions.Tenancy;

/// <summary>
/// One row of <c>ModulusTenantDataStores</c>: a tenant's store and where its secret lives. It holds no connection string.
/// </summary>
public class TenantDataStoreEntity
{
    /// <summary>The owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>The store within the tenant.</summary>
    public string StoreKey { get; set; } = string.Empty;

    /// <summary>How the tenant's data is isolated.</summary>
    public TenantDataIsolation Isolation { get; set; }

    /// <summary>The database provider.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>The secret reference (empty for shared stores).</summary>
    public string SecretPath { get; set; } = string.Empty;

    /// <summary>The secret version the catalog expects to be current.</summary>
    public int SecretVersion { get; set; }

    /// <summary>The lifecycle state.</summary>
    public TenantDataStoreStatus Status { get; set; }
}
