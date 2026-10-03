namespace Modulus.Webhooks;

using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// One delivery cycle, modeled on the outbox processor: pick due deliveries, claim them atomically (a conditional
/// <c>UPDATE</c>, so replicas never send the same delivery at the same time), send them concurrently, then record each
/// outcome: delivered, retry later on the schedule, or dead-lettered. Delivery is at least once (a crash between the
/// send and the save repeats it); receivers de-duplicate on <c>webhook-id</c>.
/// </summary>
internal sealed class WebhookDeliveryProcessor(
    ModulusWebhooksDbContext db,
    IHttpClientFactory httpClientFactory,
    WebhookSecretProtector secretProtector,
    IOptions<ModulusWebhooksOptions> options,
    TimeProvider clock,
    ILogger<WebhookDeliveryProcessor> logger)
{
    public const string HttpClientName = "Modulus.Webhooks";

    private const int PurgeBatchSize = 1000;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromDays(1);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>Runs one cycle; returns the number of deliveries attempted.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        await PurgeAsync(settings, ct);

        var now = Now();
        var candidates = await db.WebhookDeliveries
            .Where(d => d.DeliveredAt == null && d.DeadLetteredAt == null
                     && (d.LockedUntil == null || d.LockedUntil < now)
                     && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(Math.Max(1, settings.BatchSize))
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var claimedAt = Now();
        var lockUntil = claimedAt + settings.LockDuration;
        await db.WebhookDeliveries
            .Where(d => candidates.Contains(d.Id) && d.DeliveredAt == null && d.DeadLetteredAt == null
                     && (d.LockedUntil == null || d.LockedUntil < claimedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedBy, _instanceId).SetProperty(d => d.LockedUntil, lockUntil), ct);

        var deliveries = await db.WebhookDeliveries
            .Where(d => candidates.Contains(d.Id) && d.LockedBy == _instanceId && d.DeliveredAt == null && d.DeadLetteredAt == null)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);
        if (deliveries.Count == 0)
            return 0;

        var subscriptionIds = deliveries.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var secrets = subscriptions.Values.ToDictionary(s => s.Id, s => SigningSecrets(s, claimedAt));

        using var gate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrency));
        var outcomes = await Task.WhenAll(deliveries.Select(async delivery =>
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription) || !subscription.IsEnabled)
                return Outcome.Unavailable(subscription is null ? "The subscription no longer exists." : "The subscription is disabled.");
            if (secrets[subscription.Id] is not { Count: > 0 } keys)
                return Outcome.Failure(null, "The signing secret cannot be decrypted (check the Data Protection key ring).", null);

            await gate.WaitAsync(ct);
            try
            {
                return await SendAsync(delivery, subscription, keys, settings, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        var completedAt = Now();
        for (var i = 0; i < deliveries.Count; i++)
        {
            var delivery = deliveries[i];
            subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription);
            Apply(delivery, subscription, outcomes[i], settings, completedAt);
        }

        // Not `ct`: the requests already went out; losing their bookkeeping to a shutdown would resend them.
        await db.SaveChangesAsync(CancellationToken.None);
        return deliveries.Count;
    }

    private async Task<Outcome> SendAsync(
        WebhookDelivery delivery,
        WebhookSubscription subscription,
        IReadOnlyList<string> keys,
        ModulusWebhooksOptions settings,
        CancellationToken ct)
    {
        var timestamp = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(StandardWebhooks.IdHeader, delivery.MessageId);
        request.Headers.TryAddWithoutValidation(StandardWebhooks.TimestampHeader,
            timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(StandardWebhooks.SignatureHeader,
            string.Join(' ', keys.Select(k => StandardWebhooks.Sign(k, delivery.MessageId, timestamp, delivery.Payload))));
        request.Headers.TryAddWithoutValidation("User-Agent", settings.UserAgent);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return Outcome.Success(status);
            if (response.StatusCode == HttpStatusCode.Gone)
                return Outcome.Gone();

            var body = await ReadPrefixAsync(response, settings.MaxStoredResponseLength, ct);
            var error = string.IsNullOrWhiteSpace(body)
                ? $"The endpoint answered {status}."
                : $"The endpoint answered {status}: {body}";
            return Outcome.Failure(status, error, RetryAfter(response));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Outcome.Cancelled();
        }
        catch (OperationCanceledException)
        {
            return Outcome.Failure(null, $"No response within {settings.RequestTimeout}.", null);
        }
        catch (HttpRequestException ex)
        {
            return Outcome.Failure(null, ex.Message, null);
        }
    }

    private void Apply(
        WebhookDelivery delivery,
        WebhookSubscription? subscription,
        Outcome outcome,
        ModulusWebhooksOptions settings,
        DateTime now)
    {
        delivery.LockedBy = null;
        delivery.LockedUntil = null;
        if (outcome.Kind == OutcomeKind.Cancelled)
            return;

        if (outcome.Kind == OutcomeKind.Unavailable)
        {
            delivery.DeadLetteredAt = now;
            delivery.LastError = outcome.Error;
            WebhookMetrics.DeadLettered.Add(1);
            return;
        }

        delivery.AttemptCount++;
        delivery.LastAttemptAt = now;
        delivery.LastStatusCode = outcome.StatusCode;

        switch (outcome.Kind)
        {
            case OutcomeKind.Success:
                delivery.DeliveredAt = now;
                delivery.LastError = null;
                delivery.NextAttemptAt = null;
                if (subscription is not null)
                    subscription.FailingSince = null;
                WebhookMetrics.Delivered.Add(1);
                return;

            case OutcomeKind.Gone:
                delivery.DeadLetteredAt = now;
                delivery.LastError = "The endpoint answered 410 Gone; the subscription was disabled.";
                WebhookMetrics.DeadLettered.Add(1);
                if (subscription is not null)
                    Disable(subscription, "The endpoint answered 410 Gone.", now);
                return;
        }

        delivery.LastError = Truncate(outcome.Error, 2048);
        WebhookMetrics.Failed.Add(1);
        var schedule = settings.EffectiveRetrySchedule;
        if (delivery.AttemptCount > schedule.Count)
        {
            delivery.DeadLetteredAt = now;
            delivery.NextAttemptAt = null;
            WebhookMetrics.DeadLettered.Add(1);
            logger.LogWarning("Webhook delivery {DeliveryId} ({EventType}) dead-lettered after {Attempts} attempt(s): {Error}",
                delivery.Id, delivery.EventType, delivery.AttemptCount, delivery.LastError);
        }
        else
        {
            var delay = schedule[delivery.AttemptCount - 1];
            if (outcome.RetryAfter is { } retryAfter && retryAfter > delay)
                delay = retryAfter < MaxRetryAfter ? retryAfter : MaxRetryAfter;
            delivery.NextAttemptAt = now + delay;
            logger.LogInformation("Webhook delivery {DeliveryId} ({EventType}) failed (attempt {Attempt}), next attempt at {Next}: {Error}",
                delivery.Id, delivery.EventType, delivery.AttemptCount, delivery.NextAttemptAt, delivery.LastError);
        }

        if (subscription is null)
            return;
        subscription.FailingSince ??= now;
        if (settings.DisableAfterFailingFor is { } limit && subscription.IsEnabled && now - subscription.FailingSince >= limit)
            Disable(subscription, $"The endpoint failed every attempt for {limit}.", now);
    }

    private void Disable(WebhookSubscription subscription, string reason, DateTime now)
    {
        if (!subscription.IsEnabled)
            return;
        subscription.IsEnabled = false;
        subscription.DisabledReason = reason;
        subscription.UpdatedAt = now;
        logger.LogWarning("Webhook subscription {SubscriptionId} disabled: {Reason}", subscription.Id, reason);
    }

    private List<string> SigningSecrets(WebhookSubscription subscription, DateTime now)
    {
        var keys = new List<string>(2);
        try
        {
            keys.Add(secretProtector.Unprotect(subscription.ProtectedSecret));
            if (subscription.ProtectedPreviousSecret is { } previous && subscription.PreviousSecretExpiresAt > now)
                keys.Add(secretProtector.Unprotect(previous));
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogError(ex, "The signing secret of webhook subscription {SubscriptionId} cannot be decrypted.", subscription.Id);
        }

        return keys;
    }

    private async Task PurgeAsync(ModulusWebhooksOptions settings, CancellationToken ct)
    {
        if (settings.PurgeAfter is not { } retention || retention <= TimeSpan.Zero)
            return;

        var cutoff = Now() - retention;
        var expired = await db.WebhookDeliveries
            .Where(d => (d.DeliveredAt != null && d.DeliveredAt < cutoff) || (d.DeadLetteredAt != null && d.DeadLetteredAt < cutoff))
            .OrderBy(d => d.CreatedAt)
            .Take(PurgeBatchSize)
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (expired.Count > 0)
            await db.WebhookDeliveries.Where(d => expired.Contains(d.Id)).ExecuteDeleteAsync(ct);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
            return date - clock.GetUtcNow();
        return null;
    }

    private static async Task<string> ReadPrefixAsync(HttpResponseMessage response, int maxLength, CancellationToken ct)
    {
        if (maxLength <= 0)
            return string.Empty;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[maxLength];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            return new string(buffer, 0, read).Trim();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return string.Empty;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    private enum OutcomeKind
    {
        Success,
        Gone,
        Failure,
        Unavailable,
        Cancelled,
    }

    private readonly record struct Outcome(OutcomeKind Kind, int? StatusCode, string? Error, TimeSpan? RetryAfter)
    {
        public static Outcome Success(int status) => new(OutcomeKind.Success, status, null, null);

        public static Outcome Gone() => new(OutcomeKind.Gone, 410, null, null);

        public static Outcome Failure(int? status, string error, TimeSpan? retryAfter) => new(OutcomeKind.Failure, status, error, retryAfter);

        public static Outcome Unavailable(string error) => new(OutcomeKind.Unavailable, null, error, null);

        public static Outcome Cancelled() => new(OutcomeKind.Cancelled, null, null, null);
    }
}

/// <summary>The <c>Modulus.Webhooks</c> meter.</summary>
internal static class WebhookMetrics
{
    public const string MeterName = "Modulus.Webhooks";

    private static readonly Meter s_meter = new(MeterName);

    public static readonly Counter<long> Delivered = s_meter.CreateCounter<long>("modulus.webhooks.delivered", description: "Deliveries accepted by their endpoint.");

    public static readonly Counter<long> Failed = s_meter.CreateCounter<long>("modulus.webhooks.failed", description: "Failed delivery attempts.");

    public static readonly Counter<long> DeadLettered = s_meter.CreateCounter<long>("modulus.webhooks.dead_lettered", description: "Deliveries given up on.");
}
