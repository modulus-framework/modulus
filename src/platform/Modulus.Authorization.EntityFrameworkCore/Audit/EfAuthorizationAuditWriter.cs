namespace Modulus.Authorization.EntityFrameworkCore.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Audit;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Core.Correlation;

/// <summary>
/// Persists audit events durably to <see cref="AuthorizationStoreDbContext"/>'s
/// own outbox table, immediately (its own transaction — this context is
/// deliberately isolated from the module transaction fan-out, so it cannot ride
/// the same transaction as the store write that triggered the audit). Drained
/// and dispatched by <c>AuthorizationAuditRelayService</c>.
/// </summary>
public sealed class EfAuthorizationAuditWriter : IAuthorizationAuditWriter
{
    private readonly IDbContextFactory<AuthorizationStoreDbContext> _factory;
    private readonly IServiceProvider _sp;

    public EfAuthorizationAuditWriter(
        IDbContextFactory<AuthorizationStoreDbContext> factory,
        IIntegrationEventRegistry registry,
        IServiceProvider sp)
    {
        _factory = factory;
        _sp = sp;

        // Guarantee the audit event types resolve on dispatch even if the host
        // never passed Modulus.Platform's assembly to AddModulusEvents(...) —
        // Register is idempotent (a dictionary upsert), safe to call every time
        // a writer is constructed.
        registry.Register(typeof(AuthorizationAdministrativeChangeEvent));
    }

    public async Task WriteAsync(IIntegrationEvent auditEvent, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // ICurrentTenant/ICorrelationContext are only registered when the host
        // sets up multi-tenancy / calls Modulus.AspNetCore's AddModulusCorrelation
        // — resolve both optionally so this package doesn't force either
        // dependency, and so a TryAdd fallback here can't win a registration-order
        // race against the host's real multi-tenancy setup.
        var tenant = _sp.GetService<ICurrentTenant>();
        var correlation = _sp.GetService<ICorrelationContext>();
        var causation = _sp.GetService<ICausationIdContext>();
        var serializer = _sp.GetRequiredService<IMessageSerializer>();

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            MessageType = IntegrationEventNaming.GetName(auditEvent.GetType()),
            Payload = serializer.Serialize(auditEvent, auditEvent.GetType()),
            TenantId = tenant?.TenantId ?? Guid.Empty,
            ModuleName = "Authorization",
            CorrelationId = correlation is { IsSet: true } ? correlation.CorrelationId : null,
            CausationId = causation?.CausationId,
        });

        await db.SaveChangesAsync(ct);
    }
}
