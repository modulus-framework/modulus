namespace Modulus.AuditLogging.Security.Retention;

/// <summary>
/// Chains under a legal hold (A11-1). A held chain is never purged, by retention or by a direct purge, whatever its age. Placing a hold is
/// always allowed; releasing one is an administrative act the application does not perform.
/// </summary>
public interface ISecurityAuditHolds
{
    /// <summary>Whether the chain is on hold.</summary>
    Task<bool> IsHeldAsync(Guid chainId, CancellationToken ct = default);

    /// <summary>Puts a chain on hold, with the reason someone must later release it for. Placing a hold that exists changes nothing.</summary>
    Task PlaceAsync(Guid chainId, string reason, CancellationToken ct = default);

    /// <summary>Releases a hold. False when the chain was not on hold.</summary>
    Task<bool> ReleaseAsync(Guid chainId, CancellationToken ct = default);
}

/// <summary>The in-process hold register, for tests and single-process tools. Holds survive only as long as the process; production uses the durable store.</summary>
public sealed class SecurityAuditHolds : ISecurityAuditHolds
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, string> _reasons = [];

    public Task<bool> IsHeldAsync(Guid chainId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_reasons.ContainsKey(chainId));
    }

    public Task PlaceAsync(Guid chainId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
            _reasons.TryAdd(chainId, reason);
        return Task.CompletedTask;
    }

    public Task<bool> ReleaseAsync(Guid chainId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_reasons.Remove(chainId));
    }
}
