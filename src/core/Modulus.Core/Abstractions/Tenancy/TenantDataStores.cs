namespace Modulus.Core.Abstractions.Tenancy;

using Modulus.Core.Abstractions;

/// <summary>How a tenant's data for one store key is isolated.</summary>
public enum TenantDataIsolation
{
    /// <summary>The tenant uses the host database (guarded by query filters and row-level security).</summary>
    Shared,

    /// <summary>The tenant has its own database; its connection string lives in the secret store.</summary>
    DatabasePerTenant,
}

/// <summary>Lifecycle of a tenant data store. Only <c>Active</c> and <c>Rotating</c> stores serve requests.</summary>
public enum TenantDataStoreStatus
{
    /// <summary>Being created; not yet served.</summary>
    Provisioning,

    /// <summary>Served.</summary>
    Active,

    /// <summary>A new credential is being written; the previous one is still served until the swap completes.</summary>
    Rotating,

    /// <summary>Refused: no connection is resolved for this tenant.</summary>
    Disabled,
}

/// <summary>
/// One catalog row: where a tenant's database is described, never the connection string itself. The connection string is
/// the <c>connectionString</c> field of the secret at <see cref="SecretPath"/>.
/// </summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="StoreKey">The store within the tenant (for example <c>primary</c>).</param>
/// <param name="Isolation">How the tenant's data is isolated.</param>
/// <param name="ProviderKey">The database provider, for example <c>postgresql</c>. Checked against the context on every resolve.</param>
/// <param name="SecretPath">The secret reference, for example <c>tenant-db/{tenantId}/primary</c>. Empty for shared stores.</param>
/// <param name="SecretVersion">The secret version the catalog expects to be current; an older read is refused.</param>
/// <param name="Status">The lifecycle state.</param>
public sealed record TenantDataStore(
    Guid TenantId,
    string StoreKey,
    TenantDataIsolation Isolation,
    string ProviderKey,
    string SecretPath,
    int SecretVersion,
    TenantDataStoreStatus Status);

/// <summary>Reads the tenant data-store catalog. It never returns a secret value.</summary>
public interface ITenantDataStoreCatalog
{
    /// <summary>Returns the store row, or <c>null</c> when the tenant has none.</summary>
    Task<TenantDataStore?> FindAsync(Guid tenantId, string storeKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// The database a tenant's context should open. <see cref="ConnectionString"/> is <c>null</c> for a shared store, which means
/// the host connection. The string is a credential: <see cref="ToString"/> never prints it.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreKey">The store.</param>
/// <param name="ProviderKey">The provider the target was resolved for.</param>
/// <param name="ConnectionString">The connection string, or <c>null</c> for the host connection.</param>
/// <param name="SecretVersion">The secret version the string came from.</param>
public sealed record TenantDatabaseTarget(
    Guid TenantId,
    string StoreKey,
    string ProviderKey,
    string? ConnectionString,
    int SecretVersion)
{
    /// <inheritdoc />
    public override string ToString()
        => $"TenantDatabaseTarget {{ TenantId = {TenantId}, StoreKey = {StoreKey}, ProviderKey = {ProviderKey}, ConnectionString = {(ConnectionString is null ? "host" : "***")}, SecretVersion = {SecretVersion} }}";
}

/// <summary>
/// Resolves the database for a tenant. Every call checks the catalog row, the provider and that the secret belongs to this
/// tenant; a missing, disabled or tampered row is refused, never answered with the host database or another tenant's.
/// </summary>
public interface ITenantDatabaseResolver
{
    /// <summary>Resolves the target, reading the catalog and secret store when the cache is cold.</summary>
    Task<TenantDatabaseTarget> ResolveAsync(Guid tenantId, string storeKey, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>Returns a fresh cached target without I/O. Used by synchronous connection opens.</summary>
    bool TryGetCached(Guid tenantId, string storeKey, string providerKey, out TenantDatabaseTarget? target);

    /// <summary>Drops the cached catalog entry for a tenant store, so the next resolve reads the catalog again.</summary>
    void Evict(Guid tenantId, string storeKey);

    /// <summary>
    /// Takes the target this node last evicted for a tenant store, so the connection pool of its old string can be cleared
    /// after a rotation. Returns <c>false</c> when there is none. Taking it removes it.
    /// </summary>
    bool TryTakeSuperseded(Guid tenantId, string storeKey, out TenantDatabaseTarget? target);
}

/// <summary>
/// Provider operations for tenant databases: a connectivity probe before a connection string is trusted, and clearing the
/// connection pool of a string that was replaced. Each provider package registers its own implementation.
/// </summary>
public interface ITenantDatabaseProviders
{
    /// <summary>Opens a connection with the string and runs a no-op. Throws when the database cannot be reached.</summary>
    Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>Clears the pooled connections made with the string. Connections in use are discarded when they return.</summary>
    void ClearPool(string providerKey, string connectionString);
}

/// <summary>
/// Runs before a tenant's request reaches the application, so that the tenant's database targets are resolved while the
/// request is still async. A synchronous connection open can then use them (see the EF tenant connection interceptor).
/// A failure answers the request with <c>503</c> instead of failing deep inside the application.
/// </summary>
public interface ITenantWarmup
{
    /// <summary>Resolves what this warmup needs for <paramref name="tenant"/>.</summary>
    Task WarmAsync(TenantInfo tenant, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes a tenant's database connection string into the secret store and keeps the catalog row in step. Runs on the
/// provisioning host, which holds the writer identity; the runtime host never calls it.
/// </summary>
public interface ITenantDataStoreProvisioner
{
    /// <summary>
    /// Creates the store: writes the secret and marks the row <c>Active</c>. Idempotent: an active store is returned unchanged,
    /// and a store left <c>Provisioning</c> by an interrupted run is completed.
    /// </summary>
    Task<TenantDataStore> ProvisionAsync(
        Guid tenantId, string storeKey, string providerKey, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the connection string of an active store with a check-and-set write, then evicts the cached target. The store
    /// reads as <c>Rotating</c> while the write is in flight; a failed rotation can be retried.
    /// </summary>
    Task<TenantDataStore> RotateAsync(
        Guid tenantId, string storeKey, string connectionString, CancellationToken cancellationToken = default);
}
