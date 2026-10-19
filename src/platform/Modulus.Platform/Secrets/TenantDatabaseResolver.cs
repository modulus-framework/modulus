namespace Modulus.Secrets;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>
/// Resolves a tenant's database from the catalog and the secret store. The checks, in order: a catalog row exists; its status
/// serves requests; its provider is the one the context was built for; its secret path is exactly the canonical path of this
/// tenant and store (so a catalog edit cannot point a tenant at another tenant's secret); the secret is not older than the
/// catalog expects. Any failure is <see cref="TenantDatabaseUnavailableException"/>, never the host database.
/// Targets are cached for <see cref="TenantDataStoreOptions.CatalogCacheTtl"/>. The catalog is read in its own scope, so this
/// singleton never holds a database context.
/// </summary>
public sealed class TenantDatabaseResolver(
    IServiceScopeFactory scopes,
    ISecretStore secrets,
    ISecretCache secretCache,
    IOptions<TenantDataStoreOptions> options,
    TimeProvider clock) : ITenantDatabaseResolver
{
    private readonly ConcurrentDictionary<(Guid TenantId, string StoreKey), CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<(Guid TenantId, string StoreKey), TenantDatabaseTarget> _superseded = new();

    /// <inheritdoc />
    public async Task<TenantDatabaseTarget> ResolveAsync(
        Guid tenantId, string storeKey, string providerKey, CancellationToken cancellationToken = default)
    {
        ValidateArguments(storeKey, providerKey);
        if (TryGetCached(tenantId, storeKey, providerKey, out var cached))
            return cached!;

        var row = await FindRowAsync(tenantId, storeKey, providerKey, cancellationToken);
        var target = row.Isolation == TenantDataIsolation.Shared
            ? new TenantDatabaseTarget(tenantId, storeKey, providerKey, ConnectionString: null, row.SecretVersion)
            : await BuildDatabaseTargetAsync(row, cancellationToken);

        _cache[(tenantId, storeKey)] = new CacheEntry(target, clock.GetUtcNow());
        return target;
    }

    /// <inheritdoc />
    public bool TryGetCached(Guid tenantId, string storeKey, string providerKey, out TenantDatabaseTarget? target)
    {
        target = null;
        if (!_cache.TryGetValue((tenantId, storeKey), out var entry))
            return false;
        if (clock.GetUtcNow() - entry.FetchedAt >= options.Value.CatalogCacheTtl)
            return false;
        if (!string.Equals(entry.Target.ProviderKey, providerKey, StringComparison.Ordinal))
            return false;

        target = entry.Target;
        return true;
    }

    /// <inheritdoc />
    public void Evict(Guid tenantId, string storeKey)
    {
        // The last target this node served is kept, so its pool can be cleared once a rotated string replaces it.
        if (_cache.TryRemove((tenantId, storeKey), out var entry) && entry.Target.ConnectionString is not null)
            _superseded[(tenantId, storeKey)] = entry.Target;
    }

    /// <inheritdoc />
    public bool TryTakeSuperseded(Guid tenantId, string storeKey, out TenantDatabaseTarget? target)
        => _superseded.TryRemove((tenantId, storeKey), out target);

    private async Task<TenantDataStore> FindRowAsync(
        Guid tenantId, string storeKey, string providerKey, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ITenantDataStoreCatalog>();
        var row = await catalog.FindAsync(tenantId, storeKey, cancellationToken)
            ?? throw new TenantDatabaseUnavailableException("No data store is provisioned for this tenant.");

        if (row.Status is not (TenantDataStoreStatus.Active or TenantDataStoreStatus.Rotating))
            throw new TenantDatabaseUnavailableException($"The tenant's data store is {row.Status}.");
        if (!string.Equals(row.ProviderKey, providerKey, StringComparison.Ordinal))
            throw new TenantDatabaseUnavailableException("The tenant's data store is for another database provider.");
        return row;
    }

    private async Task<TenantDatabaseTarget> BuildDatabaseTargetAsync(TenantDataStore row, CancellationToken cancellationToken)
    {
        var canonical = $"{options.Value.SecretPrefix}/{row.TenantId:D}/{row.StoreKey}";
        if (!string.Equals(row.SecretPath, canonical, StringComparison.Ordinal)
            || !SecretReference.TryParse(canonical, out var reference) || reference is null)
        {
            throw new TenantDatabaseUnavailableException("The tenant's data store refers to an unexpected secret.");
        }

        var secret = await ReadSecretAsync(reference, row.SecretVersion, cancellationToken);
        var field = options.Value.ConnectionStringField;
        if (!secret.Data.TryGetValue(field, out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
            throw new TenantDatabaseUnavailableException("The tenant's secret has no connection string.");

        return new TenantDatabaseTarget(row.TenantId, row.StoreKey, row.ProviderKey, connectionString, secret.Version);
    }

    private async Task<SecretValue> ReadSecretAsync(SecretReference reference, int expectedVersion, CancellationToken cancellationToken)
    {
        var secret = await ReadOnceAsync(reference, cancellationToken);
        if (secret.Version >= expectedVersion)
            return secret;

        // The cached copy predates the catalog's version: drop it and read once more before refusing.
        secretCache.Evict(reference);
        secret = await ReadOnceAsync(reference, cancellationToken);
        return secret.Version >= expectedVersion
            ? secret
            : throw new TenantDatabaseUnavailableException("The tenant's secret is older than its data store expects.");
    }

    private async Task<SecretValue> ReadOnceAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        try
        {
            return await secrets.GetAsync(reference, cancellationToken);
        }
        catch (SecretNotFoundException ex)
        {
            throw new TenantDatabaseUnavailableException("The tenant's secret was not found.", ex);
        }
    }

    private static void ValidateArguments(string storeKey, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        if (!IsStoreKey(storeKey))
            throw new ArgumentException("The store key must be lower-case letters, digits and hyphens, starting with a letter.", nameof(storeKey));
    }

    private static bool IsStoreKey(string storeKey)
    {
        if (storeKey.Length is 0 or > 32 || !char.IsAsciiLetterLower(storeKey[0]))
            return false;
        foreach (var c in storeKey)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                return false;
        }
        return true;
    }

    private readonly record struct CacheEntry(TenantDatabaseTarget Target, DateTimeOffset FetchedAt);
}
