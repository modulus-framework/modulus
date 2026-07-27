namespace Modulus.Authorization.EntityFrameworkCore.Audit;

/// <summary>
/// Tuning for <c>AuthorizationAuditRelayService</c>. A dedicated options type
/// (rather than reusing <c>Modulus.Outbox.Abstractions.OutboxOptions</c>) so a
/// host that also calls <c>Modulus.Outbox</c>'s <c>AddOutbox&lt;T&gt;</c> for its
/// own module contexts doesn't have its <c>IOptions&lt;OutboxOptions&gt;</c>
/// registration collide with this one.
/// </summary>
public sealed class AuthorizationAuditOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxRetries { get; set; } = 5;
    public int PollingIntervalSec { get; set; } = 5;
    public int LockTimeoutSec { get; set; } = 30;
    public int InitialBackoffSec { get; set; } = 2;
}
