namespace Modulus.Outbox;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;

internal sealed class EfOutboxWriter(
    DbContext        db,
    ICurrentTenant   tenant,
    IHttpContextAccessor http)
    : IOutboxWriter
{
    public Task WriteAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var correlationId = http.HttpContext?
            .Request.Headers["X-Correlation-Id"]
            .FirstOrDefault();

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            MessageType   = @event.GetType().AssemblyQualifiedName!,
            Payload       = JsonSerializer.Serialize(@event, @event.GetType()),
            TenantId      = tenant.TenantId ?? Guid.Empty,
            ModuleName    = db.GetType().Name
                               .Replace("DbContext", string.Empty),
            CorrelationId = correlationId,
            CausationId   = @event.EventId.ToString(),
        });

        return Task.CompletedTask;
    }
}