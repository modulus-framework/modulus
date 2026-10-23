namespace Modulus.AuditLogging.Security.Retention;

/// <summary>
/// Where purged security audit entries go before they leave the live store (A11-1). Retention writes the entries here first and purges
/// only after the write succeeds, so a purge never destroys evidence.
/// </summary>
public interface ISecurityAuditArchive
{
    /// <summary>Stores the entries of one chain, in sequence order. Throws when the write fails.</summary>
    Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default);

    /// <summary>
    /// Reads back the archived entries of one chain, in sequence order, for restore validation (<see cref="SecurityAuditRestoreValidator"/>).
    /// An archive that cannot be read back keeps the default, which refuses: validation then reports the chain as not provable.
    /// </summary>
    Task<IReadOnlyList<SecurityAuditRecord>> ReadAsync(Guid chainId, CancellationToken ct = default)
        => throw new NotSupportedException("This archive cannot be read back, so restore validation cannot check the purged entries.");
}

/// <summary>Thrown when no archive is configured: retention then keeps every entry.</summary>
public sealed class SecurityAuditArchiveNotConfiguredException() : InvalidOperationException(
    "No security audit archive is configured, so retention keeps every entry. Register an ISecurityAuditArchive to purge.");

/// <summary>The default archive: it refuses, so nothing is purged until an archive is registered.</summary>
public sealed class NoSecurityAuditArchive : ISecurityAuditArchive
{
    public Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
        => throw new SecurityAuditArchiveNotConfiguredException();
}
