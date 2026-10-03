namespace Modulus.Realtime;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Realtime.Delivery;
using Modulus.Realtime.Transports;

/// <summary>
/// The SignalR transport (<c>{path}/hub</c>, when <c>Realtime:SignalR:Enabled</c>): the same messages as the SSE stream,
/// sent to the client method <c>event</c> as a <see cref="RealtimeEnvelope"/>, plus what only a two-way connection can
/// do: <see cref="Subscribe"/> / <see cref="Unsubscribe"/> topics while connected and <see cref="Resume"/> after an
/// automatic reconnect. Filter event types with <c>?types=a,b.*</c> on the hub URL. Derive from it to add your own
/// client-to-server methods (map it with <c>MapModulusRealtime&lt;THub&gt;()</c>); call the base members when overriding.
/// </summary>
public class RealtimeHub : Hub
{
    /// <summary>The client method messages are sent to.</summary>
    public const string ClientMethod = "event";

    private const string Transport = "signalr";
    private static readonly object ConnectionKey = new();

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext() ?? throw new InvalidOperationException("The realtime hub needs an HTTP transport.");
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<ModulusRealtimeOptions>>().Value;
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        var types = RealtimeConnectionFactory.SplitList(http.Request.Query["types"]);
        if (types.Count > RealtimeConnectionFactory.MaxTypeFilters)
            throw new HubException($"At most {RealtimeConnectionFactory.MaxTypeFilters} event types.");

        var connection = RealtimeConnectionFactory.Create(http, Context.ConnectionId, Transport, types, options.MaxQueuedMessagesPerConnection);
        if (!dispatcher.TryRegister(connection))
            throw new HubException("Too many open realtime connections for this user.");
        Context.Items[ConnectionKey] = connection;

        // Send through the hub context (not this transient hub instance), from one pump per connection.
        var hubContext = (IHubContext)services.GetRequiredService(typeof(IHubContext<>).MakeGenericType(GetType()));
        var client = hubContext.Clients.Client(Context.ConnectionId);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<RealtimeHub>();
        var caller = Context;
        connection.OnClosed = () =>
        {
            if (connection.CloseReason is not null)
                caller.Abort();
        };
        _ = Task.Run(() => PumpAsync(connection, client, logger));

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(ConnectionKey, out var value) && value is RealtimeConnection connection)
            Services.GetRequiredService<RealtimeDispatcher>().Unregister(connection);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>Follows <paramref name="topic"/> (it must match a registered topic the caller may follow).</summary>
    public virtual async Task Subscribe(string topic)
    {
        var connection = Connection;
        var options = Services.GetRequiredService<IOptions<ModulusRealtimeOptions>>().Value;
        if (connection.Follows(topic))
            return;
        if (connection.TopicCount >= options.MaxTopicsPerConnection)
            throw new HubException($"At most {options.MaxTopicsPerConnection} topics.");

        var tenant = Services.GetService<ICurrentTenant>();
        RealtimeTopicDecision decision;
        using (tenant?.Change(connection.Tenant))
        {
            decision = await Services.GetRequiredService<IRealtimeTopicAuthorizer>()
                .AuthorizeAsync(Context.User ?? connection.User, topic, Services, Context.ConnectionAborted).ConfigureAwait(false);
        }

        switch (decision)
        {
            case RealtimeTopicDecision.Unknown:
                throw new HubException($"'{topic}' is not a topic.");
            case RealtimeTopicDecision.Denied:
                throw new HubException($"Not allowed to follow '{topic}'.");
            default:
                connection.Follow(topic);
                break;
        }
    }

    /// <summary>Stops following <paramref name="topic"/>.</summary>
    public virtual Task Unsubscribe(string topic)
    {
        Connection.Unfollow(topic);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Replays what this client missed after <paramref name="lastEventId"/> (the id of the last envelope it received).
    /// Call it right after an automatic reconnect. False when that is no longer possible: reload the state.
    /// </summary>
    public virtual async Task<bool> Resume(string lastEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lastEventId);
        var connection = Connection;
        var replay = await Services.GetRequiredService<RealtimeDispatcher>()
            .ReplayAsync(connection, lastEventId, Context.ConnectionAborted).ConfigureAwait(false);
        foreach (var message in replay.Messages)
        {
            if (!connection.TryEnqueue(message))
                break;
        }

        return replay.Complete;
    }

    private IServiceProvider Services => Context.GetHttpContext()?.RequestServices
        ?? throw new InvalidOperationException("The realtime hub needs an HTTP transport.");

    private RealtimeConnection Connection => Context.Items.TryGetValue(ConnectionKey, out var value) && value is RealtimeConnection connection
        ? connection
        : throw new HubException("The connection is not registered.");

    private static async Task PumpAsync(RealtimeConnection connection, IClientProxy client, ILogger logger)
    {
        try
        {
            await foreach (var message in connection.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var envelope = new RealtimeEnvelope(message.Id, message.Type, message.DataElement, message.Topic, message.Timestamp);
                await client.SendAsync(ClientMethod, envelope).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime messages could not be sent to SignalR connection {ConnectionId}.", connection.Id);
            connection.Close("send-failed");
        }
    }
}
