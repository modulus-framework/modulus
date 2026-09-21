namespace Modulus.AuditLogging;

using Modulus.Core.Abstractions;

/// <inheritdoc cref="IAuditLogger" />
public sealed class AuditLogger(
    IAuditLogStore store,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IAuditLogger
{
    private readonly IAuditLogStore _store = store;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly TimeProvider _clock = clock;

    public Task LogAsync(
        string action,
        string? resource = null,
        string? resourceId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return _store.AppendAsync(new AuditLogEntry
        {
            OccurredAt = _clock.GetUtcNow(),
            TenantId = _currentTenant.TenantId,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            Action = action,
            Resource = resource,
            ResourceId = resourceId,
            Detail = detail,
        }, ct);
    }
}
