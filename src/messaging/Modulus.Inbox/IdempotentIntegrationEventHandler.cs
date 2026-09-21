using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modulus.Inbox;

using System.Text.Json;
using Modulus.Events.Abstractions;
using Modulus.Inbox.Abstractions;
using Modulus.Observability;

/// <summary>
/// Decorator that wraps <see cref="IIntegrationEventHandler{TEvent}"/> with
/// inbox deduplication. Applied at dispatch time by
/// <c>InboxHandlerDecorator</c> (registered by <c>AddInbox</c> /
/// <c>AddMongoInbox</c>) rather than by mutating DI registrations — see
/// <see cref="IIntegrationEventHandlerDecorator"/>'s remarks for why.
/// <para>
/// Dedup is anchored on <c>(EventId, HandlerName)</c> — see
/// <see cref="InboxMessage.HandlerName"/> — which becomes the
/// <see cref="InboxMessage"/> composite primary key. The handler name is the
/// wrapped inner handler's own concrete type name, so two different handlers
/// subscribed to the same event each get an independent claim instead of
/// racing over one shared-by-EventId row: without this, the first handler to
/// claim marks the event <c>Processed</c> and every OTHER handler for that
/// event is then skipped forever.
/// </para>
/// <para>
/// Behaviour by state of the existing inbox row for this
/// <c>(EventId, HandlerName)</c>:
/// <list type="bullet">
///   <item><c>Processed</c> — duplicate; skipped (inner NOT executed).</item>
///   <item><c>Processing</c> — in-flight elsewhere; deferred (throws
///       <see cref="InboxDeferralException"/>) so the broker redelivers later,
///       rather than executing the side effect twice.</item>
///   <item><c>RetryCount &gt;= MaxRetries</c> — poison message; dead-lettered
///       (inner NOT executed) to stop a perpetually-failing event hot-looping.</item>
///   <item>otherwise — the row is claimed (inserted or transitioned to
///       <c>Processing</c>), the inner handler runs, and the final state is
///       persisted.</item>
/// </list>
/// </para>
/// </summary>
public sealed class IdempotentIntegrationEventHandler<TEvent>(
    IIntegrationEventHandler<TEvent> inner,
    IInboxStore store,
    IOptions<InboxOptions> opts,
    ILogger<IdempotentIntegrationEventHandler<TEvent>> logger)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Stable identity of the wrapped inner handler, used as the second half
    /// of the inbox composite key. The inner handler's concrete type name is
    /// stable across process restarts (unlike an instance hash code) and
    /// unique per handler class (the normal case: one class implements
    /// <c>IIntegrationEventHandler&lt;TEvent&gt;</c> once).
    /// </summary>
    private readonly string _handlerName = TruncateToColumnWidth(
        inner.GetType().FullName ?? inner.GetType().Name);

    private const int HandlerNameMaxLength = 500; // matches InboxMessageConfiguration

    private static string TruncateToColumnWidth(string name)
        => name.Length <= HandlerNameMaxLength ? name : name[..HandlerNameMaxLength];

    /// <summary>
    /// Back-off before in-pipeline retry <paramref name="failedAttempt"/>
    /// (1-based): exponential (<c>base * 2^(n-1)</c>) or flat, with optional
    /// ±20% jitter, capped at 5 minutes.
    /// </summary>
    private static TimeSpan ComputeRetryDelay(InboxOptions options, int failedAttempt)
    {
        var @base = Math.Max(0, options.HandlerRetryBaseDelaySec);
        var seconds = options.HandlerRetryExponential
            ? @base * Math.Pow(2, Math.Min(failedAttempt - 1, 10))
            : @base;
        if (options.HandlerRetryJitter)
            seconds *= 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromSeconds(Math.Min(seconds, 300));
    }

    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        var id = @event.EventId;

        // 1. Atomically claim (or skip/defer).  The store handles all
        //    storage-specific logic (PK race, status check, retry budget).
        InboxMessage? claimed;
        try
        {
            claimed = await store.TryClaimAsync(
                id,
                _handlerName,
                IntegrationEventNaming.GetName(typeof(TEvent)),
                JsonSerializer.Serialize(@event),
                opts.Value.MaxRetries,
                TimeSpan.FromSeconds(Math.Max(1, opts.Value.ClaimTimeoutSeconds)),
                ct);
        }
        catch (InboxDeferralException)
        {
            logger.LogDebug("Inbox: {Type} {Id} ({Handler}) in-flight elsewhere; deferring.",
                typeof(TEvent).Name, id, _handlerName);
            throw;
        }

        if (claimed is null)
        {
            ModulusMeters.InboxDedupHits.Add(1);
            logger.LogDebug("Inbox: {Type} {Id} ({Handler}) skipped (duplicate or dead-lettered).",
                typeof(TEvent).Name, id, _handlerName);
            return;
        }

        // 2. We own the record — execute the real handler with the configured
        //    in-pipeline retries (fast, in-process retries before the inbox's
        //    own claim/retry/dead-letter cycle kicks in). Capture the final
        //    error so we can persist final state AND re-throw the ORIGINAL
        //    exception (a failure in the persistence step below must not
        //    mask it).
        Exception? handlerError = null;
        var retry = opts.Value;
        var maxAttempts = Math.Max(1, retry.HandlerRetryCount + 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await inner.HandleAsync(@event, ct);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                var delay = ComputeRetryDelay(retry, attempt);
                logger.LogWarning(ex,
                    "Inbox: handler failed for {Type} {Id} ({Handler}) (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs}ms",
                    typeof(TEvent).Name, id, _handlerName,
                    attempt, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                handlerError = ex;
                logger.LogError(ex,
                    "Inbox: handler failed for {Type} {Id} ({Handler})",
                    typeof(TEvent).Name, id, _handlerName);
                break;
            }
        }

        // 3. Persist final state in its own try/catch. A persistence failure
        //    here is logged but does NOT replace the handler's exception.
        try
        {
            if (handlerError is null)
                await store.MarkProcessedAsync(id, _handlerName, ct);
            else
                await store.MarkFailedAsync(id, _handlerName, handlerError.Message, ct);
        }
        catch (Exception persistEx)
        {
            logger.LogError(persistEx,
                "Inbox: failed to persist final state for {Type} {Id} ({Handler})",
                typeof(TEvent).Name, id, _handlerName);
        }

        if (handlerError is not null)
            throw handlerError;
    }
}
