namespace Modulus.MultiTenancy.EntityFrameworkCore;

using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Events.Abstractions;
using Modulus.Observability;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Secrets;

/// <summary>
/// Provisions and rotates tenant databases. The steps are: probe the connection string, write it to the secret store
/// (check-and-set), migrate the tenant's database, then mark the store <c>Active</c>. A failure before the last step leaves the
/// store <c>Provisioning</c>, which the resolver does not serve, and a retry completes it. After an activation or rotation the
/// node evicts its cached target and publishes <see cref="TenantDataStoreChanged"/>, so other nodes clear their pools when the
/// event reaches them (through a broker-backed bus). Without one, they see the change when their cache entry expires.
/// </summary>
public sealed class TenantDataStoreProvisioner<TTenant, TMembership, TDataStore>(
    TenantStoreDbContext<TTenant, TMembership, TDataStore> db,
    ISecretWriter writer,
    ITenantDatabaseResolver resolver,
    ITenantDatabaseProviders providers,
    IServiceScopeFactory scopes,
    IOptions<TenantDataStoreOptions> options,
    ISecurityAuditLog? audit = null,
    IModuleBus? bus = null) : ITenantDataStoreProvisioner
    where TTenant : TenantEntity
    where TMembership : TenantMembershipEntity
    where TDataStore : TenantDataStoreEntity, new()
{
    private static readonly Counter<long> s_provisioned = ModulusMeters.MultiTenancy.CreateCounter<long>("modulus.tenant_data_store.provisioned");
    private static readonly Counter<long> s_rotated = ModulusMeters.MultiTenancy.CreateCounter<long>("modulus.tenant_data_store.rotated");

    /// <inheritdoc />
    public async Task<TenantDataStore> ProvisionAsync(
        Guid tenantId, string storeKey, string providerKey, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var reference = SecretFor(tenantId, storeKey);

        var row = await FindAsync(tenantId, storeKey, cancellationToken);
        if (row is null)
        {
            row = new TDataStore
            {
                TenantId = tenantId,
                StoreKey = storeKey,
                Isolation = TenantDataIsolation.DatabasePerTenant,
                ProviderKey = providerKey,
                SecretPath = reference.Path,
                SecretVersion = 0,
                Status = TenantDataStoreStatus.Provisioning,
            };
            db.TenantDataStores.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (row.Status is TenantDataStoreStatus.Active or TenantDataStoreStatus.Rotating)
        {
            // Already provisioned: idempotent. Changing a live connection string is a rotation.
            return ToRecord(row);
        }
        else if (row.Status == TenantDataStoreStatus.Disabled)
        {
            throw new ConflictException("The tenant's data store is disabled and cannot be provisioned.");
        }
        else if (!string.Equals(row.ProviderKey, providerKey, StringComparison.Ordinal))
        {
            throw new ConflictException("The tenant's data store was started for another database provider.");
        }

        await ProbeAsync(providerKey, connectionString, cancellationToken);

        // Resumes safely: an interrupted run may already have written the secret, so the write is against its current version.
        row.SecretVersion = await WriteSecretAsync(reference, connectionString, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await MigrateTenantAsync(tenantId, storeKey, providerKey, connectionString, cancellationToken);

        row.Status = TenantDataStoreStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        resolver.Evict(tenantId, storeKey);
        Record(tenantId, storeKey, row.SecretVersion, "tenant-data-store.provisioned");
        await PublishChangedAsync(row, cancellationToken);
        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<TenantDataStore> RotateAsync(
        Guid tenantId, string storeKey, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var reference = SecretFor(tenantId, storeKey);

        var row = await FindAsync(tenantId, storeKey, cancellationToken)
            ?? throw new NotFoundException("No data store is provisioned for this tenant.");
        if (row.Status is not (TenantDataStoreStatus.Active or TenantDataStoreStatus.Rotating))
            throw new ConflictException($"The tenant's data store is {row.Status}; only an active store can be rotated.");

        // A store left Rotating by a failed attempt is retried: the status only says a write is in flight, and the
        // previous string keeps being served until the swap completes.
        row.Status = TenantDataStoreStatus.Rotating;
        await db.SaveChangesAsync(cancellationToken);

        await ProbeAsync(row.ProviderKey, connectionString, cancellationToken);

        row.SecretVersion = await WriteSecretAsync(reference, connectionString, cancellationToken);
        row.Status = TenantDataStoreStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        resolver.Evict(tenantId, storeKey);
        Record(tenantId, storeKey, row.SecretVersion, "tenant-data-store.rotated");
        await PublishChangedAsync(row, cancellationToken);
        return ToRecord(row);
    }

    /// <summary>Opens the candidate string before anything is written. A refused connection changes nothing.</summary>
    private async Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await providers.ProbeAsync(providerKey, connectionString, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message names no part of the connection string; the inner exception keeps the provider's detail.
            throw new TenantDatabaseUnavailableException("The tenant's database could not be reached. Nothing was changed.", ex);
        }
    }

    /// <summary>
    /// Migrates this tenant's database before the store is active. The store is still <c>Provisioning</c> here, so the resolver
    /// refuses it; the setup override gives the migration the candidate string instead (see <see cref="TenantDatabaseSetup"/>).
    /// The host's shared contexts are migrated in the same pass, as in the tenant fan-out.
    /// </summary>
    private async Task MigrateTenantAsync(
        Guid tenantId, string storeKey, string providerKey, string connectionString, CancellationToken cancellationToken)
    {
        var slug = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The tenant does not exist.");

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var current = sp.GetRequiredService<ICurrentTenant>();
        using var _ = current.Change(new TenantInfo(tenantId, slug));
        using var __ = TenantDatabaseSetup.Use(tenantId, storeKey, providerKey, connectionString);
        try
        {
            await sp.MigrateModulusDatabasesAsync(DatabaseInitializationMode.MigrateOrCreate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message names no part of the connection string; the inner exception keeps the provider's detail.
            throw new TenantDatabaseUnavailableException("The tenant's database could not be set up. The store stays inactive; run the provisioning again.", ex);
        }
    }

    private async Task PublishChangedAsync(TDataStore row, CancellationToken cancellationToken)
    {
        if (bus is null)
            return;
        await bus.PublishAsync(new TenantDataStoreChanged(row.TenantId, row.StoreKey, row.ProviderKey, row.SecretVersion), cancellationToken);
    }

    private void Record(Guid tenantId, string storeKey, int version, string action)
    {
        // The event names the store and version only. Connection strings and secret values never enter the audit log.
        (action.EndsWith("provisioned", StringComparison.Ordinal) ? s_provisioned : s_rotated).Add(1);
        audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Tenancy,
            Action = action,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = tenantId,
            Target = $"tenant:{tenantId}",
            Details = new Dictionary<string, string?> { ["store"] = storeKey, ["version"] = version.ToString() },
        });
    }

    private Task<TDataStore?> FindAsync(Guid tenantId, string storeKey, CancellationToken cancellationToken)
        => db.TenantDataStores.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.StoreKey == storeKey, cancellationToken);

    private async Task<int> WriteSecretAsync(SecretReference reference, string connectionString, CancellationToken cancellationToken)
    {
        // Null means no secret yet, which the store treats as create-only (version 0).
        var current = await writer.GetCurrentVersionAsync(reference, cancellationToken);
        var data = new Dictionary<string, string> { [options.Value.ConnectionStringField] = connectionString };
        return await writer.WriteAsync(reference, data, current ?? 0, cancellationToken);
    }

    private SecretReference SecretFor(Guid tenantId, string storeKey)
        => SecretReference.Parse($"{options.Value.SecretPrefix}/{tenantId:D}/{storeKey}");

    private static TenantDataStore ToRecord(TDataStore row)
        => new(row.TenantId, row.StoreKey, row.Isolation, row.ProviderKey, row.SecretPath, row.SecretVersion, row.Status);
}
