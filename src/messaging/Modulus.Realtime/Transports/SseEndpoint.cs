namespace Modulus.Realtime.Transports;

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Modulus.Realtime.Delivery;

/// <summary>
/// <c>GET {path}/events[?types=a,b.*][&amp;topics=x,y]</c>: a Server-Sent Events stream of the caller's messages. The first
/// event is <c>modulus.ready</c>; each message is <c>id</c> / <c>event</c> (its type) / <c>data</c> (its JSON payload). A
/// reconnect carrying <c>Last-Event-ID</c> (sent by <c>EventSource</c> automatically, or <c>?lastEventId=</c>) replays what
/// was missed, or sends <c>modulus.reset</c> when that is no longer possible. The stream ends when the access token
/// expires, so the client reconnects with a fresh one.
/// </summary>
internal static class SseEndpoint
{
    public const string Transport = "sse";
    private const string HeartbeatEvent = "modulus.heartbeat";

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        RealtimeDispatcher dispatcher,
        IRealtimeTopicAuthorizer topicAuthorizer,
        IOptions<ModulusRealtimeOptions> options,
        TimeProvider clock)
    {
        var settings = options.Value;
        var query = context.Request.Query;
        var types = RealtimeConnectionFactory.SplitList(query["types"]);
        var topics = RealtimeConnectionFactory.SplitList(query["topics"]);
        if (types.Count > RealtimeConnectionFactory.MaxTypeFilters)
            return Problem(StatusCodes.Status400BadRequest, "too_many_types", $"At most {RealtimeConnectionFactory.MaxTypeFilters} event types.");
        if (topics.Count > settings.MaxTopicsPerConnection)
            return Problem(StatusCodes.Status400BadRequest, "too_many_topics", $"At most {settings.MaxTopicsPerConnection} topics.");

        foreach (var topic in topics)
        {
            var decision = await topicAuthorizer.AuthorizeAsync(context.User, topic, context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            if (decision == RealtimeTopicDecision.Unknown)
                return Problem(StatusCodes.Status400BadRequest, "unknown_topic", $"'{topic}' is not a topic.");
            if (decision == RealtimeTopicDecision.Denied)
                return Problem(StatusCodes.Status403Forbidden, "topic_denied", $"Not allowed to follow '{topic}'.");
        }

        var connection = RealtimeConnectionFactory.Create(context, Guid.NewGuid().ToString("N"), Transport, types, settings.MaxQueuedMessagesPerConnection);
        foreach (var topic in topics)
            connection.Follow(topic);
        if (!dispatcher.TryRegister(connection))
            return Problem(StatusCodes.Status429TooManyRequests, "too_many_connections", "Too many open realtime connections for this user.");

        // Unregistered whatever happens to the response, even if the stream never starts.
        context.Response.OnCompleted(() =>
        {
            dispatcher.Unregister(connection);
            return Task.CompletedTask;
        });

        var lastEventId = context.Request.Headers["Last-Event-ID"].ToString();
        if (lastEventId.Length == 0)
            lastEventId = query["lastEventId"].ToString();
        var expiresAt = settings.CloseAtTokenExpiry ? RealtimeConnectionFactory.TokenExpiry(context) : null;

        context.Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(StreamAsync(connection, dispatcher, settings, lastEventId, expiresAt, clock, context.RequestAborted));
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamAsync(
        RealtimeConnection connection,
        RealtimeDispatcher dispatcher,
        ModulusRealtimeOptions settings,
        string lastEventId,
        DateTimeOffset? expiresAt,
        TimeProvider clock,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (expiresAt is { } expiry)
            lifetime.CancelAfter(Max(expiry - clock.GetUtcNow(), TimeSpan.Zero));

        try
        {
            yield return new SseItem<string>(JsonSerializer.Serialize(new { connectionId = connection.Id }), RealtimeEvents.Ready)
            {
                ReconnectionInterval = settings.Sse.RetryInterval,
            };

            if (lastEventId.Length > 0)
            {
                var replay = await dispatcher.ReplayAsync(connection, lastEventId, lifetime.Token).ConfigureAwait(false);
                if (!replay.Complete)
                    yield return new SseItem<string>("{}", RealtimeEvents.Reset);
                foreach (var message in replay.Messages)
                    yield return Item(message);
            }

            while (true)
            {
                var outcome = await WaitAsync(connection.Reader, settings.Sse.HeartbeatInterval, lifetime.Token).ConfigureAwait(false);
                if (outcome == Wait.Ended)
                    yield break;
                if (outcome == Wait.Heartbeat)
                {
                    // No data line: EventSource dispatches nothing, but proxies see traffic.
                    yield return new SseItem<string>(string.Empty, HeartbeatEvent);
                    continue;
                }

                while (connection.Reader.TryRead(out var message))
                    yield return Item(message);
            }
        }
        finally
        {
            dispatcher.Unregister(connection);
        }
    }

    private static SseItem<string> Item(RealtimeMessage message) => new(message.Data, message.Type) { EventId = message.Id };

    private enum Wait
    {
        Data,
        Heartbeat,
        Ended,
    }

    private static async Task<Wait> WaitAsync(ChannelReader<RealtimeMessage> reader, TimeSpan heartbeat, CancellationToken lifetime)
    {
        if (lifetime.IsCancellationRequested)
            return Wait.Ended;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        timer.CancelAfter(heartbeat);
        try
        {
            return await reader.WaitToReadAsync(timer.Token).ConfigureAwait(false) ? Wait.Data : Wait.Ended;
        }
        catch (OperationCanceledException)
        {
            return lifetime.IsCancellationRequested ? Wait.Ended : Wait.Heartbeat;
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static IResult Problem(int status, string code, string detail)
        => TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
