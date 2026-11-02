namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// The tenant data lifecycle: exports, offboarding, legal holds, retention and deletion certificates. Requests record an operation
/// and return at once; the background worker runs it (or call <see cref="RunOperationAsync"/> directly). Every step is recorded, so
/// an interrupted operation resumes from its last completed step.
/// </summary>
public interface ITenantLifecycleProvider
{
    /// <summary>Requests an export archive of a company's data. The archive is encrypted and expires after <see cref="TenantLifecycleOptions.ExportLifetime"/>.</summary>
    Task<LifecycleOperation> RequestExportAsync(Guid tenantId, string requestedBy, CancellationToken ct = default);

    /// <summary>
    /// Requests the offboarding of a company: suspend it, disable its integrations, optionally export it, delete its owned data,
    /// keep the retained data for its periods, and sign a certificate. Refused while another offboarding of the company is open.
    /// </summary>
    Task<LifecycleOperation> RequestOffboardingAsync(Guid tenantId, string requestedBy, bool exportFirst, CancellationToken ct = default);

    /// <summary>Finds an operation, or returns null.</summary>
    Task<LifecycleOperation?> GetOperationAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>A company's operations, newest first.</summary>
    Task<IReadOnlyList<LifecycleOperation>> ListOperationsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Runs an operation from its last completed step. Completed operations are left alone; failed ones are retried.</summary>
    Task RunOperationAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>
    /// Writes the decrypted export archive to <paramref name="destination"/>. The whole archive is verified first, so a damaged
    /// archive writes nothing.
    /// </summary>
    /// <exception cref="TenantLifecycleRefusedException">There is no archive, it expired, or it is held.</exception>
    /// <exception cref="InvalidDataException">The archive is damaged or was encrypted with another key.</exception>
    Task OpenExportAsync(Guid operationId, Stream destination, CancellationToken ct = default);

    /// <summary>Places a legal hold: offboarding and retention stop deleting the company's data while it is active.</summary>
    Task<LegalHold> PlaceLegalHoldAsync(Guid tenantId, string reason, string placedBy, CancellationToken ct = default);

    /// <summary>Releases a legal hold. Releasing a released hold does nothing.</summary>
    Task ReleaseLegalHoldAsync(Guid holdId, string releasedBy, CancellationToken ct = default);

    /// <summary>Checks an operation's certificate signature.</summary>
    Task<bool> VerifyCertificateAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the datasets whose retention period has ended (each with its own certificate) and the export archives that have
    /// expired. Companies under a legal hold are skipped.
    /// </summary>
    Task RunRetentionAsync(CancellationToken ct = default);
}

/// <summary>A lifecycle request that cannot be done now (not found, expired, held, already in progress). The message is safe to show.</summary>
public sealed class TenantLifecycleRefusedException(string message) : InvalidOperationException(message);
