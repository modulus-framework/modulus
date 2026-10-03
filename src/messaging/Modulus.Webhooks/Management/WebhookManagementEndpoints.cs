namespace Modulus.Webhooks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// The webhook management API. Every endpoint requires <see cref="WebhookPermissions.Manage"/> and sees only the current
/// tenant's subscriptions and deliveries (the host's when there is no tenant).
/// </summary>
public static class WebhookManagementEndpoints
{
    /// <summary>
    /// Maps, under <paramref name="prefix"/>: <c>GET event-types</c>; <c>GET|POST subscriptions</c>;
    /// <c>GET|PUT|DELETE subscriptions/{id}</c>; <c>POST subscriptions/{id}/rotate-secret</c>;
    /// <c>POST subscriptions/{id}/test</c>; <c>GET subscriptions/{id}/deliveries</c>; <c>GET deliveries/{id}</c>;
    /// <c>POST deliveries/{id}/retry</c>. Returns the group for further conventions.
    /// </summary>
    public static RouteGroupBuilder MapModulusWebhooks(this IEndpointRouteBuilder endpoints, string prefix = "/api/webhooks")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var isService = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        if (!isService.IsService(typeof(WebhookSecretProtector)))
            throw new InvalidOperationException("Call AddModulusWebhooks(configuration) before MapModulusWebhooks().");
        if (!isService.IsService(typeof(ModulusWebhooksDbContext)))
            throw new InvalidOperationException("Webhooks need a store: call AddModulusWebhooksStore<TContext>() with the app's webhooks DbContext.");

        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization(WebhookPermissions.Manage)
            .WithTags("Webhooks");

        group.MapGet("/event-types", (IWebhookEventCatalog catalog)
            => TypedResults.Ok(catalog.Events.Select(e => new WebhookEventTypeResponse(e.Name, e.Description)).ToList()));

        group.MapGet("/subscriptions", async (HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
        {
            var tenant = TenantOf(http);
            var items = await db.WebhookSubscriptions.AsNoTracking()
                .Where(s => s.TenantId == tenant)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            return TypedResults.Ok(items.Select(s => ToResponse(s)).ToList());
        });

        group.MapGet("/subscriptions/{id:guid}", async (Guid id, HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
            await FindAsync(db, http, id, ct) is { } subscription
                ? Results.Ok(ToResponse(subscription))
                : Results.NotFound());

        group.MapPost("/subscriptions", CreateAsync);
        group.MapPut("/subscriptions/{id:guid}", UpdateAsync);

        group.MapDelete("/subscriptions/{id:guid}", async (Guid id, HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
        {
            if (await FindAsync(db, http, id, ct) is not { } subscription)
                return Results.NotFound();
            db.WebhookSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/subscriptions/{id:guid}/rotate-secret", async (
            Guid id, HttpContext http, ModulusWebhooksDbContext db, WebhookSecretProtector protector,
            IOptions<ModulusWebhooksOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            if (await FindAsync(db, http, id, ct) is not { } subscription)
                return Results.NotFound();

            var now = clock.GetUtcNow().UtcDateTime;
            var secret = StandardWebhooks.GenerateSecret();
            subscription.ProtectedPreviousSecret = subscription.ProtectedSecret;
            subscription.PreviousSecretExpiresAt = now + options.Value.SecretRotationOverlap;
            subscription.ProtectedSecret = protector.Protect(secret);
            subscription.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(subscription, secret));
        });

        group.MapPost("/subscriptions/{id:guid}/test", async (
            Guid id, HttpContext http, ModulusWebhooksDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (await FindAsync(db, http, id, ct) is not { } subscription)
                return Results.NotFound();
            if (!subscription.IsEnabled)
                return Validation("isEnabled", "The subscription is disabled.");

            var now = clock.GetUtcNow().UtcDateTime;
            var delivery = new WebhookDelivery
            {
                SubscriptionId = subscription.Id,
                TenantId = subscription.TenantId,
                EventId = Guid.NewGuid(),
                EventType = WebhookEventCatalog.TestEventName,
                Payload = WebhookPayload.Build(WebhookEventCatalog.TestEventName, now, new { message = "A test delivery from Modulus." }),
                CreatedAt = now,
            };
            db.WebhookDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"{Prefix(http)}/deliveries/{delivery.Id}", ToResponse(delivery, includePayload: false));
        });

        group.MapGet("/subscriptions/{id:guid}/deliveries", async (
            Guid id, string? status, int? page, int? pageSize, HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
        {
            if (await FindAsync(db, http, id, ct) is null)
                return Results.NotFound();

            var query = db.WebhookDeliveries.AsNoTracking().Where(d => d.SubscriptionId == id);
            if (!string.IsNullOrEmpty(status))
            {
                if (!Enum.TryParse<WebhookDeliveryStatus>(status, ignoreCase: true, out var wanted) || int.TryParse(status, out _))
                    return Validation("status", "Must be pending, delivered or failed.");
                query = wanted switch
                {
                    WebhookDeliveryStatus.Delivered => query.Where(d => d.DeliveredAt != null),
                    WebhookDeliveryStatus.Failed => query.Where(d => d.DeliveredAt == null && d.DeadLetteredAt != null),
                    _ => query.Where(d => d.DeliveredAt == null && d.DeadLetteredAt == null),
                };
            }

            var number = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(d => d.CreatedAt)
                .Skip((number - 1) * size).Take(size)
                .ToListAsync(ct);
            return Results.Ok(new WebhookPage<WebhookDeliveryResponse>(
                items.Select(d => ToResponse(d, includePayload: false)).ToList(), number, size, total));
        });

        group.MapGet("/deliveries/{id:guid}", async (Guid id, HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
        {
            var tenant = TenantOf(http);
            var delivery = await db.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenant, ct);
            return delivery is null ? Results.NotFound() : Results.Ok(ToResponse(delivery, includePayload: true));
        });

        group.MapPost("/deliveries/{id:guid}/retry", async (Guid id, HttpContext http, ModulusWebhooksDbContext db, CancellationToken ct) =>
        {
            var tenant = TenantOf(http);
            var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenant, ct);
            if (delivery is null)
                return Results.NotFound();
            if (delivery.DeliveredAt is not null)
                return Validation("status", "The delivery was already delivered.");

            // Sends it again soon with a fresh retry schedule (a disabled subscription dead-letters it again).
            delivery.DeadLetteredAt = null;
            delivery.NextAttemptAt = null;
            delivery.AttemptCount = 0;
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"{Prefix(http)}/deliveries/{delivery.Id}", ToResponse(delivery, includePayload: false));
        });

        return group;
    }

    private static async Task<IResult> CreateAsync(
        WebhookSubscriptionRequest request,
        HttpContext http,
        ModulusWebhooksDbContext db,
        WebhookSecretProtector protector,
        IWebhookEventCatalog catalog,
        IOptions<ModulusWebhooksOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var settings = options.Value;
        var errors = Validate(request, catalog, settings);
        if (request.Secret is not null && !StandardWebhooks.IsValidSecret(request.Secret))
            errors["secret"] = ["Must be 'whsec_' followed by 24 to 64 base64 bytes."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var tenant = TenantOf(http);
        if (await db.WebhookSubscriptions.CountAsync(s => s.TenantId == tenant, ct) >= settings.MaxSubscriptionsPerTenant)
            return Validation("url", $"A tenant may have at most {settings.MaxSubscriptionsPerTenant} subscriptions.");

        var now = clock.GetUtcNow().UtcDateTime;
        var secret = request.Secret ?? StandardWebhooks.GenerateSecret();
        var subscription = new WebhookSubscription
        {
            TenantId = tenant,
            Url = request.Url!.Trim(),
            Description = Normalize(request.Description),
            EventTypes = NormalizeTypes(request.EventTypes!),
            ProtectedSecret = protector.Protect(secret),
            IsEnabled = request.IsEnabled ?? true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        return Results.Created($"{Prefix(http)}/subscriptions/{subscription.Id}", ToResponse(subscription, secret));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        WebhookSubscriptionRequest request,
        HttpContext http,
        ModulusWebhooksDbContext db,
        IWebhookEventCatalog catalog,
        IOptions<ModulusWebhooksOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (await FindAsync(db, http, id, ct) is not { } subscription)
            return Results.NotFound();

        var errors = Validate(request, catalog, options.Value);
        if (request.Secret is not null)
            errors["secret"] = ["The secret cannot be set on an update; rotate it instead."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        subscription.Url = request.Url!.Trim();
        subscription.Description = Normalize(request.Description);
        subscription.EventTypes = NormalizeTypes(request.EventTypes!);
        if (request.IsEnabled is { } enabled && enabled != subscription.IsEnabled)
        {
            subscription.IsEnabled = enabled;
            subscription.DisabledReason = null;
            subscription.FailingSince = null;
        }

        subscription.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(subscription));
    }

    private static Dictionary<string, string[]> Validate(
        WebhookSubscriptionRequest request, IWebhookEventCatalog catalog, ModulusWebhooksOptions settings)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (WebhookUrlRules.Validate(request.Url, settings) is { } urlError)
            errors["url"] = [urlError];

        var known = catalog.Events.Select(e => e.Name).ToList();
        if (request.EventTypes is not { Count: > 0 } types)
            errors["eventTypes"] = ["List at least one event type."];
        else if (types.Count > 100)
            errors["eventTypes"] = ["List at most 100 event types."];
        else if (types.Where(t => !WebhookEventFilter.IsValid(t?.Trim() ?? string.Empty, known)).ToList() is { Count: > 0 } unknown)
            errors["eventTypes"] = [.. unknown.Select(t => $"Unknown event type '{t}'.")];

        if (request.Description is { Length: > 500 })
            errors["description"] = ["Must be at most 500 characters."];
        return errors;
    }

    private static Task<WebhookSubscription?> FindAsync(ModulusWebhooksDbContext db, HttpContext http, Guid id, CancellationToken ct)
    {
        var tenant = TenantOf(http);
        return db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant, ct);
    }

    private static Guid TenantOf(HttpContext http)
        => http.RequestServices.GetService<ICurrentTenant>()?.TenantId ?? Guid.Empty;

    private static string Prefix(HttpContext http)
    {
        var path = http.Request.PathBase.Add(http.Request.Path).Value ?? string.Empty;
        var at = path.IndexOf("/subscriptions", StringComparison.Ordinal);
        if (at < 0)
            at = path.IndexOf("/deliveries", StringComparison.Ordinal);
        return at < 0 ? path : path[..at];
    }

    private static IResult Validation(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeTypes(IEnumerable<string> types)
        => [.. types.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription s, string? secret = null)
        => new(s.Id, s.Url, s.EventTypes, s.Description, s.IsEnabled, s.DisabledReason, s.FailingSince, s.CreatedAt, s.UpdatedAt, secret);

    private static WebhookDeliveryResponse ToResponse(WebhookDelivery d, bool includePayload)
        => new(d.Id, d.SubscriptionId, d.MessageId, d.EventId, d.EventType, d.Status, d.AttemptCount, d.CreatedAt,
            d.DeliveredAt, d.NextAttemptAt, d.LastAttemptAt, d.LastStatusCode, d.LastError, includePayload ? d.Payload : null);
}
