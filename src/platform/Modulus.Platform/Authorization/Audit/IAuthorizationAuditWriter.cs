namespace Modulus.Authorization.Audit;

using Modulus.Events.Abstractions;

/// <summary>
/// Durable sink for authorization audit events (auth blueprint §5.14/§16).
/// The default (<see cref="NullAuthorizationAuditWriter"/>) is a no-op so the
/// framework works without an audit transport configured; call
/// <c>AddEfCoreAuthorizationAudit</c> (<c>Modulus.Authorization.EntityFrameworkCore</c>)
/// to persist events durably over the outbox transport.
/// </summary>
public interface IAuthorizationAuditWriter
{
    Task WriteAsync(IIntegrationEvent auditEvent, CancellationToken ct = default);
}

/// <summary>No-op default — audit emission is opt-in via <c>AddEfCoreAuthorizationAudit</c>.</summary>
public sealed class NullAuthorizationAuditWriter : IAuthorizationAuditWriter
{
    public static readonly NullAuthorizationAuditWriter Instance = new();

    public Task WriteAsync(IIntegrationEvent auditEvent, CancellationToken ct = default)
        => Task.CompletedTask;
}
