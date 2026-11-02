namespace Modulus.Tenancy.Lifecycle;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Storage;
using Modulus.Tenancy.Lifecycle.Certificates;
using Modulus.Tenancy.Lifecycle.Export;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The lifecycle engine. Each dataset is handled by its module's handler inside the company's tenant context; archives and
/// certificates are kept in the host context, outside the company's data. Concurrency: one node runs an operation at a time when a
/// distributed lock is registered (<see cref="IDistributedLock"/>).
/// </summary>
internal sealed class TenantLifecycleProvider(
    ITenantLifecycleStore store,
    TenantDataRegistry registry,
    IServiceScopeFactory scopes,
    IFileStorage files,
    IOptions<TenantLifecycleOptions> options,
    TimeProvider clock,
    ILogger<TenantLifecycleProvider> logger,
    IDistributedLock? locks = null) : ITenantLifecycleProvider
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);

    private TenantLifecycleOptions Settings => options.Value;

    /// <inheritdoc />
    public Task<LifecycleOperation> RequestExportAsync(Guid tenantId, string requestedBy, CancellationToken ct = default)
    {
        RequireTenant(tenantId);
        RequireName(requestedBy, nameof(requestedBy));
        ArchiveCrypto.ParseKey(Settings.ExportEncryptionKey);
        return AddOperationAsync(new LifecycleOperation { TenantId = tenantId, Kind = OperationKind.Export, RequestedBy = requestedBy }, ct);
    }

    /// <inheritdoc />
    public async Task<LifecycleOperation> RequestOffboardingAsync(Guid tenantId, string requestedBy, bool exportFirst, CancellationToken ct = default)
    {
        RequireTenant(tenantId);
        RequireName(requestedBy, nameof(requestedBy));
        if (exportFirst)
            ArchiveCrypto.ParseKey(Settings.ExportEncryptionKey);
        SigningKey(); // refuses now, not at the end of a long offboarding, when no key is configured

        var open = (await store.ListOperationsAsync(tenantId, ct))
            .Any(o => o.Kind == OperationKind.Offboarding && o.State is OperationState.Pending or OperationState.Running or OperationState.OnHold);
        if (open)
            throw new TenantLifecycleRefusedException("An offboarding of this company is already in progress.");

        return await AddOperationAsync(new LifecycleOperation
        {
            TenantId = tenantId,
            Kind = OperationKind.Offboarding,
            RequestedBy = requestedBy,
            ExportFirst = exportFirst,
        }, ct);
    }

    /// <inheritdoc />
    public Task<LifecycleOperation?> GetOperationAsync(Guid operationId, CancellationToken ct = default)
        => store.FindOperationAsync(operationId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<LifecycleOperation>> ListOperationsAsync(Guid tenantId, CancellationToken ct = default)
        => store.ListOperationsAsync(tenantId, ct);

    /// <inheritdoc />
    public async Task RunOperationAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await store.FindOperationAsync(operationId, ct)
            ?? throw new TenantLifecycleRefusedException("The operation does not exist.");
        if (operation.State is OperationState.Completed or OperationState.Expired || operation.Kind == OperationKind.RetentionDeletion)
            return;

        IAsyncDisposable? lease = null;
        if (locks is not null)
        {
            lease = await locks.TryAcquireAsync("modulus:tenant-lifecycle:" + operation.Id.ToString("N"), LeaseDuration, ct);
            if (lease is null)
                return; // another node is running it
        }

        try
        {
            if (operation.State is OperationState.Pending or OperationState.Failed or OperationState.OnHold)
            {
                operation.State = OperationState.Running;
                operation.Error = null;
                await store.SaveOperationAsync(operation, ct);
            }

            if (operation.Kind == OperationKind.Export)
                await RunExportAsync(operation, ct);
            else
                await RunOffboardingAsync(operation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception type only: a message can carry data that must not reach the operation record.
            operation.State = OperationState.Failed;
            operation.Error = ex.GetType().Name;
            await store.SaveOperationAsync(operation, ct);
            logger.LogWarning("Tenant lifecycle operation {OperationId} ({Kind}) failed: {Error}", operation.Id, operation.Kind, operation.Error);
            throw;
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async Task OpenExportAsync(Guid operationId, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var operation = await store.FindOperationAsync(operationId, ct);
        if (operation is null || operation.Kind == OperationKind.RetentionDeletion || operation.ExportPath is null)
            throw new TenantLifecycleRefusedException("No export archive is available for this operation.");
        if (operation.ExportExpiresAt is { } expiresAt && expiresAt <= clock.GetUtcNow())
            throw new TenantLifecycleRefusedException("The export archive has expired.");

        var key = ArchiveCrypto.ParseKey(Settings.ExportEncryptionKey);
        // Verify the whole archive first (nothing is written), then decrypt it to the destination.
        await using (var verify = await OpenHostFileAsync(operation.ExportPath, ct))
            await ArchiveCrypto.DecryptAsync(verify, null, key, ct);
        await using var source = await OpenHostFileAsync(operation.ExportPath, ct);
        await ArchiveCrypto.DecryptAsync(source, destination, key, ct);
    }

    /// <inheritdoc />
    public async Task<LegalHold> PlaceLegalHoldAsync(Guid tenantId, string reason, string placedBy, CancellationToken ct = default)
    {
        RequireTenant(tenantId);
        RequireName(reason, nameof(reason));
        RequireName(placedBy, nameof(placedBy));
        var hold = new LegalHold { TenantId = tenantId, Reason = reason, PlacedBy = placedBy, PlacedAt = clock.GetUtcNow() };
        await store.AddLegalHoldAsync(hold, ct);
        logger.LogInformation("Legal hold {HoldId} placed on tenant {TenantId}", hold.Id, tenantId);
        return hold;
    }

    /// <inheritdoc />
    public async Task ReleaseLegalHoldAsync(Guid holdId, string releasedBy, CancellationToken ct = default)
    {
        RequireName(releasedBy, nameof(releasedBy));
        var hold = await store.FindLegalHoldAsync(holdId, ct)
            ?? throw new TenantLifecycleRefusedException("The legal hold does not exist.");
        if (!hold.IsActive)
            return;
        hold.ReleasedBy = releasedBy;
        hold.ReleasedAt = clock.GetUtcNow();
        await store.SaveLegalHoldAsync(hold, ct);
        logger.LogInformation("Legal hold {HoldId} released", holdId);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyCertificateAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await store.FindOperationAsync(operationId, ct)
            ?? throw new TenantLifecycleRefusedException("The operation does not exist.");
        if (operation.CertificatePayload is null || operation.CertificateSignature is null)
            return false;
        if (operation.CertificateKeyId != Settings.CertificateKeyId)
            throw new TenantLifecycleRefusedException("The certificate was signed with a key this application no longer names; keep the old key to verify it.");
        return DeletionCertificates.Verify(operation.CertificatePayload, operation.CertificateSignature, SigningKey());
    }

    /// <inheritdoc />
    public async Task RunRetentionAsync(CancellationToken ct = default)
    {
        IAsyncDisposable? lease = null;
        if (locks is not null)
        {
            lease = await locks.TryAcquireAsync(RetentionLockKey, LeaseDuration, ct);
            if (lease is null)
                return; // another node is sweeping this tick
        }

        try
        {
            await SweepAsync(ct);
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync();
        }
    }

    private const string RetentionLockKey = "modulus:tenant-lifecycle:retention";

    private async Task SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        foreach (var item in await store.ListDuePendingDeletionsAsync(now, 100, ct))
        {
            // One bad item (a company that no longer resolves, a handler that fails) is logged and skipped, so it cannot stop
            // every other due deletion behind it. It stays pending and is tried again on the next sweep.
            try
            {
                await SweepItemAsync(item, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Retention deletion of {Dataset} for tenant {TenantId} failed: {Error}", item.Dataset, item.TenantId, ex.GetType().Name);
            }
        }

        foreach (var operation in await store.ListExpiredExportsAsync(now, ct))
        {
            try
            {
                await ExpireExportAsync(operation, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Expiring export {OperationId} failed: {Error}", operation.Id, ex.GetType().Name);
            }
        }
    }

    private async Task SweepItemAsync(PendingDeletion item, DateTimeOffset now, CancellationToken ct)
    {
        if ((await store.ListActiveLegalHoldsAsync(item.TenantId, ct)).Count > 0)
            return;
        var registered = registry.Datasets.FirstOrDefault(d => d.Module == item.Module && d.Dataset.Name == item.Dataset);
        if (registered is null)
            return; // the dataset is no longer declared; leave it pending for a human to look at

        var tenant = await FindTenantAsync(item.TenantId, ct);
        long rows;
        using (var scope = scopes.CreateScope())
        using (scope.ServiceProvider.EnterTenant(tenant))
        {
            var handler = (ITenantDataHandler)scope.ServiceProvider.GetRequiredService(registered.HandlerType);
            rows = await handler.DeleteAsync(registered.Dataset, ct);
        }

        item.DoneAt = now;
        item.Rows = rows;
        await store.SavePendingDeletionAsync(item, ct);

        var receipt = new LifecycleOperation
        {
            TenantId = item.TenantId,
            Kind = OperationKind.RetentionDeletion,
            State = OperationState.Completed,
            RequestedBy = "retention",
            RequestedAt = now,
            CompletedAt = now,
        };
        receipt.Outcomes.Add(new DatasetOutcome(item.Module, item.Dataset, registered.Dataset.Category, DatasetAction.DeletedByRetention, rows));
        Sign(receipt, now);
        await store.AddOperationAsync(receipt, ct);
        logger.LogInformation("Retention deleted {Rows} rows of {Dataset} for tenant {TenantId}", rows, item.Dataset, item.TenantId);
    }

    private async Task ExpireExportAsync(LifecycleOperation operation, CancellationToken ct)
    {
        if ((await store.ListActiveLegalHoldsAsync(operation.TenantId, ct)).Count > 0)
            return; // a legal hold overrides the ordinary export lifetime

        using var host = HostContext();
        if (await files.ExistsAsync(operation.ExportPath!, ct))
            await files.DeleteAsync(operation.ExportPath!, ct);
        operation.ExportPath = null;
        operation.State = OperationState.Expired;
        await store.SaveOperationAsync(operation, ct);
        logger.LogInformation("Export {OperationId} expired and was deleted", operation.Id);
    }

    private async Task<LifecycleOperation> AddOperationAsync(LifecycleOperation operation, CancellationToken ct)
    {
        operation.RequestedAt = clock.GetUtcNow();
        await store.AddOperationAsync(operation, ct);
        logger.LogInformation("Tenant lifecycle {Kind} {OperationId} requested for tenant {TenantId}", operation.Kind, operation.Id, operation.TenantId);
        return operation;
    }

    private async Task RunExportAsync(LifecycleOperation operation, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!operation.HasStep("export"))
        {
            operation.ExportPath = ArchivePath(operation);
            await BuildArchiveAsync(operation, operation.ExportPath, ct);
            operation.ExportExpiresAt = now.Add(Settings.ExportLifetime);
            operation.Complete("export", now);
            await store.SaveOperationAsync(operation, ct);
        }

        operation.State = OperationState.Completed;
        operation.CompletedAt = now;
        await store.SaveOperationAsync(operation, ct);
        logger.LogInformation("Export {OperationId} completed", operation.Id);
    }

    private async Task RunOffboardingAsync(LifecycleOperation operation, CancellationToken ct)
    {
        var tenant = await FindTenantAsync(operation.TenantId, ct);
        var now = clock.GetUtcNow();

        // 1. Suspend: the company becomes read-only on its next request. Not destructive, so it runs before the hold check.
        if (!operation.HasStep("suspend"))
        {
            using var scope = scopes.CreateScope();
            var writer = scope.ServiceProvider.GetService<ITenantStatusWriter>()
                ?? throw new InvalidOperationException("Offboarding needs the tenant store's status writer (AddEfCoreTenantStore with multi-tenancy).");
            if (!await writer.SetStatusAsync(operation.TenantId, TenantStatus.Suspended, null, ct))
                throw new TenantLifecycleRefusedException("The company does not exist.");
            operation.Complete("suspend", now);
            await store.SaveOperationAsync(operation, ct);
        }

        // 2. Stop what runs for the company: each module's steps, each recorded on its own.
        using (var scope = scopes.CreateScope())
        using (scope.ServiceProvider.EnterTenant(tenant))
        {
            foreach (var step in scope.ServiceProvider.GetServices<ITenantOffboardingStep>())
            {
                if (operation.HasStep("disable:" + step.Name))
                    continue;
                await step.DisableAsync(ct);
                operation.Complete("disable:" + step.Name, now);
                await store.SaveOperationAsync(operation, ct);
            }
        }

        // 3. Legal hold: nothing below deletes anything while a hold is active.
        if (await HeldAsync(operation, ct))
            return;

        // 4. Export first, when asked.
        if (operation.ExportFirst && !operation.HasStep("export"))
        {
            operation.ExportPath = ArchivePath(operation);
            await BuildArchiveAsync(operation, operation.ExportPath, ct);
            operation.ExportExpiresAt = now.Add(Settings.ExportLifetime);
            operation.Complete("export", now);
            await store.SaveOperationAsync(operation, ct);
        }

        // 5. Datasets, in registration order (a module lists its dependents first). Each outcome is saved, so a resume skips it.
        using (var scope = scopes.CreateScope())
        using (scope.ServiceProvider.EnterTenant(tenant))
        {
            var handlers = Handlers(scope.ServiceProvider);
            foreach (var registered in registry.Datasets)
            {
                if (operation.Outcomes.Exists(o => o.Module == registered.Module && o.Dataset == registered.Dataset.Name))
                    continue;
                if (await HeldAsync(operation, ct))
                    return;

                var handler = handlers[registered.HandlerType];
                var dataset = registered.Dataset;
                var outcome = dataset.Category switch
                {
                    TenantDataCategory.Global => new DatasetOutcome(registered.Module, dataset.Name, dataset.Category, DatasetAction.Global, 0),
                    TenantDataCategory.Owned => new DatasetOutcome(
                        registered.Module, dataset.Name, dataset.Category, DatasetAction.Deleted,
                        await handler.DeleteAsync(dataset, ct)),
                    _ => await RetainAsync(operation, registered, handler, now, ct),
                };
                operation.Outcomes.Add(outcome);
                await store.SaveOperationAsync(operation, ct);
            }
        }

        // 6. Certificate, signed; the operation is complete.
        operation.Complete("certificate", now);
        operation.State = OperationState.Completed;
        operation.CompletedAt = now;
        Sign(operation, now);
        await store.SaveOperationAsync(operation, ct);
        logger.LogInformation("Offboarding {OperationId} completed with {Datasets} datasets", operation.Id, operation.Outcomes.Count);
    }

    /// <summary>Retained data is counted and either kept for good or scheduled for deletion. Audit records are never scheduled: deleting
    /// rows would break the hash chain, so they are kept, and the chain stays verifiable.</summary>
    private async Task<DatasetOutcome> RetainAsync(
        LifecycleOperation operation, RegisteredDataset registered, ITenantDataHandler handler, DateTimeOffset now, CancellationToken ct)
    {
        var dataset = registered.Dataset;
        var rows = await handler.CountAsync(dataset, ct);
        var days = dataset.Category == TenantDataCategory.Audit ? null : Settings.RetentionDays(dataset.Retention);
        if (days is null)
            return new DatasetOutcome(registered.Module, dataset.Name, dataset.Category, DatasetAction.Retained, rows);

        var deleteAfter = now.AddDays(days.Value);
        await store.SavePendingDeletionAsync(new PendingDeletion
        {
            Id = DeterministicId(operation.Id, dataset.Name),
            TenantId = operation.TenantId,
            OperationId = operation.Id,
            Module = registered.Module,
            Dataset = dataset.Name,
            DeleteAfter = deleteAfter,
        }, ct);
        return new DatasetOutcome(registered.Module, dataset.Name, dataset.Category, DatasetAction.PendingDeletion, rows, deleteAfter);
    }

    private async Task<bool> HeldAsync(LifecycleOperation operation, CancellationToken ct)
    {
        if ((await store.ListActiveLegalHoldsAsync(operation.TenantId, ct)).Count == 0)
        {
            if (operation.State == OperationState.OnHold)
            {
                operation.State = OperationState.Running;
                await store.SaveOperationAsync(operation, ct);
            }
            return false;
        }

        operation.State = OperationState.OnHold;
        await store.SaveOperationAsync(operation, ct);
        logger.LogInformation("Tenant lifecycle {OperationId} waits: a legal hold is active", operation.Id);
        return true;
    }

    /// <summary>
    /// Gathers the company's data in its tenant context and writes the archive straight through the encryption stream, so the
    /// only file that exists is the ciphertext, which is then stored in the host context.
    /// </summary>
    private async Task BuildArchiveAsync(LifecycleOperation operation, string path, CancellationToken ct)
    {
        var key = ArchiveCrypto.ParseKey(Settings.ExportEncryptionKey);
        var tenant = await FindTenantAsync(operation.TenantId, ct);
        var encryptedPath = Path.GetTempFileName();
        try
        {
            using (var scope = scopes.CreateScope())
            using (scope.ServiceProvider.EnterTenant(tenant))
            {
                var handlers = Handlers(scope.ServiceProvider);
                await using var file = File.Create(encryptedPath);
                using var sealedArchive = ArchiveCrypto.CreateEncryptingStream(file, key);
                using var zip = new System.IO.Compression.ZipArchive(sealedArchive, System.IO.Compression.ZipArchiveMode.Create);
                var writer = new ExportArchiveWriter(zip, files);
                foreach (var registered in registry.Datasets.Where(d => d.Dataset.Category != TenantDataCategory.Global))
                {
                    writer.BeginDataset(registered.Dataset.Name);
                    await handlers[registered.HandlerType].ExportAsync(registered.Dataset, writer, ct);
                    writer.EndDataset();
                }
                writer.WriteManifest(operation.TenantId, operation.Id, clock.GetUtcNow());
            }

            using var host = HostContext();
            await using var upload = File.OpenRead(encryptedPath);
            await files.UploadAsync(path, upload, "application/octet-stream", ct);
        }
        finally
        {
            File.Delete(encryptedPath);
        }
    }

    private void Sign(LifecycleOperation operation, DateTimeOffset completedAt)
    {
        var backupsExpireAt = completedAt.AddDays(Settings.BackupsDays);
        operation.CertificateKeyId = Settings.CertificateKeyId;
        operation.CertificatePayload = DeletionCertificates.BuildPayload(operation, completedAt, backupsExpireAt, Settings.CertificateKeyId);
        operation.CertificateSignature = DeletionCertificates.Sign(operation.CertificatePayload, SigningKey());
    }

    private byte[] SigningKey()
    {
        var text = Settings.CertificateSigningKey;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("TenantLifecycle:CertificateSigningKey is not set: certificates cannot be signed.");
        var key = Convert.FromBase64String(text);
        if (key.Length < 32)
            throw new InvalidOperationException("TenantLifecycle:CertificateSigningKey must decode to at least 32 bytes.");
        return key;
    }

    private string ArchivePath(LifecycleOperation operation) =>
        $"lifecycle/exports/{operation.TenantId:N}/{operation.Id:N}.zip.enc";

    private async Task<TenantInfo> FindTenantAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var tenants = scope.ServiceProvider.GetService<ITenantStore>()
            ?? throw new InvalidOperationException("The tenant data lifecycle needs multi-tenancy (an ITenantStore).");
        return await tenants.FindByIdAsync(tenantId, ct)
            ?? throw new TenantLifecycleRefusedException("The company does not exist.");
    }

    private async Task<Stream> OpenHostFileAsync(string path, CancellationToken ct)
    {
        using var host = HostContext();
        return await files.DownloadAsync(path, ct);
    }

    /// <summary>Enters the host context (all companies, outside any one company's data) until disposed.</summary>
    private HostScope HostContext()
    {
        var scope = scopes.CreateScope();
        var change = scope.ServiceProvider.GetService<ICurrentTenant>()?.Change(null);
        return new HostScope(scope, change);
    }

    private Dictionary<Type, ITenantDataHandler> Handlers(IServiceProvider services)
    {
        var handlers = new Dictionary<Type, ITenantDataHandler>();
        foreach (var type in registry.Datasets.Select(d => d.HandlerType).Distinct())
            handlers[type] = (ITenantDataHandler)services.GetRequiredService(type);
        return handlers;
    }

    private static Guid DeterministicId(Guid operationId, string dataset) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(operationId.ToString("N") + "|" + dataset))[..16]);

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A company id is required.", nameof(tenantId));
    }

    private static void RequireName(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A value is required.", parameter);
    }

    /// <summary>The host context and the scope that holds it, disposed together.</summary>
    private sealed class HostScope(IServiceScope scope, IDisposable? change) : IDisposable
    {
        public void Dispose()
        {
            change?.Dispose();
            scope.Dispose();
        }
    }
}
