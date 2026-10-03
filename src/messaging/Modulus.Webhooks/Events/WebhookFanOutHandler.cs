namespace Modulus.Webhooks;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// Records one delivery per matching subscription of the event's tenant. It is an ordinary integration event handler,
/// so it runs wherever the event is handled: after the outbox relays it in a modular monolith, or in the service that
/// consumes it from the broker. The (subscription, event id) pair is unique, so a redelivered event is recorded once.
/// </summary>
internal sealed class WebhookFanOutHandler<TEvent>(
    IServiceProvider services,
    IWebhookEventCatalog catalog,
    TimeProvider clock,
    ILogger<WebhookFanOutHandler<TEvent>> logger)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var name = IntegrationEventNaming.GetName(typeof(TEvent));
        if (!catalog.TryGet(name, out var descriptor))
            return;

        var db = services.GetRequiredService<ModulusWebhooksDbContext>();
        var tenantId = services.GetService<ICurrentTenant>()?.TenantId ?? Guid.Empty;

        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.TenantId == tenantId && s.IsEnabled)
            .ToListAsync(ct);
        var targets = subscriptions.Where(s => s.Matches(name)).Select(s => s.Id).ToList();
        if (targets.Count == 0)
            return;

        var already = await db.WebhookDeliveries
            .Where(d => d.EventId == @event.EventId && targets.Contains(d.SubscriptionId))
            .Select(d => d.SubscriptionId)
            .ToListAsync(ct);
        var pending = targets.Except(already).ToList();
        if (pending.Count == 0)
            return;

        var data = descriptor.Payload is null ? @event : descriptor.Payload(@event);
        var payload = WebhookPayload.Build(name, @event.OccurredAt, data);
        var correlationId = services.GetService<ICorrelationContext>()?.CorrelationId;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var subscriptionId in pending)
        {
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                SubscriptionId = subscriptionId,
                TenantId = tenantId,
                EventId = @event.EventId,
                EventType = name,
                Payload = payload,
                CorrelationId = correlationId,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Webhook event {EventType} ({EventId}) recorded for {Count} subscription(s).", name, @event.EventId, pending.Count);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent handler of the same event may have won the unique index; then its rows stand.
            db.ChangeTracker.Clear();
            if (!await AllRecordedAsync(db, @event.EventId, pending, ct))
                throw;
            logger.LogDebug(ex, "Webhook event {EventId} was already recorded concurrently.", @event.EventId);
        }
    }

    private static async Task<bool> AllRecordedAsync(ModulusWebhooksDbContext db, Guid eventId, List<Guid> subscriptionIds, CancellationToken ct)
    {
        var recorded = await db.WebhookDeliveries.AsNoTracking()
            .CountAsync(d => d.EventId == eventId && subscriptionIds.Contains(d.SubscriptionId), ct);
        return recorded == subscriptionIds.Count;
    }
}

/// <summary>The Standard Webhooks body: <c>{"type", "timestamp", "data"}</c>.</summary>
internal static class WebhookPayload
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Build(string type, DateTime occurredAt, object? data)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["timestamp"] = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            ["data"] = data,
        };
        return JsonSerializer.Serialize(body, JsonOptions);
    }
}
