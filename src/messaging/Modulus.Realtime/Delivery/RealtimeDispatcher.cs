namespace Modulus.Realtime.Delivery;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>The outcome of a resume request.</summary>
internal sealed record RealtimeReplay(IReadOnlyList<RealtimeMessage> Messages, bool Complete);

/// <summary>
/// This node's open connections and its replay buffer. Registering a connection, recording a message and taking a
/// replay snapshot happen under one lock, so a reconnecting client gets every message exactly once: those recorded before
/// it registered come from the buffer, the rest live (<see cref="RealtimeConnection.FirstLiveId"/>).
/// </summary>
internal sealed class RealtimeDispatcher(
    IServiceScopeFactory scopes,
    IOptions<ModulusRealtimeOptions> options,
    TimeProvider clock,
    RealtimeMetrics metrics,
    ILogger<RealtimeDispatcher> logger)
    : IRealtimeDispatcher
{
    private readonly Lock _gate = new();
    private readonly List<RealtimeConnection> _connections = [];
    private readonly LinkedList<RealtimeMessage> _buffer = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int ConnectionCount
    {
        get
        {
            lock (_gate)
                return _connections.Count;
        }
    }

    /// <summary>Registers a connection; false when its user already has the maximum number open on this node.</summary>
    public bool TryRegister(RealtimeConnection connection)
    {
        var max = options.Value.MaxConnectionsPerUser;
        lock (_gate)
        {
            if (max > 0 && connection.UserId is { } user
                && _connections.Count(c => string.Equals(c.UserId, user, StringComparison.Ordinal)) >= max)
            {
                return false;
            }

            _connections.Add(connection);
        }

        metrics.Connected(connection.Transport);
        return true;
    }

    public void Unregister(RealtimeConnection connection)
    {
        bool removed;
        lock (_gate)
            removed = _connections.Remove(connection);
        connection.Close();
        if (removed)
            metrics.Disconnected(connection.Transport);
    }

    public RealtimeConnection? Find(string id)
    {
        lock (_gate)
            return _connections.Find(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// The buffered messages after <paramref name="lastEventId"/> that the connection would have received and has not
    /// received live. Not complete when the id is no longer (or never was) in the buffer: the client missed messages
    /// and should reload its state.
    /// </summary>
    public async Task<RealtimeReplay> ReplayAsync(RealtimeConnection connection, string lastEventId, CancellationToken ct)
    {
        List<RealtimeMessage> candidates = [];
        var found = false;
        lock (_gate)
        {
            Trim();
            var firstLive = connection.FirstLiveId;
            for (var node = _buffer.First; node is not null; node = node.Next)
            {
                if (!found)
                {
                    found = string.Equals(node.Value.Id, lastEventId, StringComparison.Ordinal);
                    continue;
                }

                if (string.Equals(node.Value.Id, firstLive, StringComparison.Ordinal))
                    break;
                candidates.Add(node.Value);
            }
        }

        List<RealtimeMessage> visible = [];
        if (candidates.Count > 0)
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var message in candidates)
            {
                if (await IsForAsync(connection, message, message.Users is { } u ? new HashSet<string>(u, StringComparer.Ordinal) : null, scope.ServiceProvider, ct).ConfigureAwait(false))
                    visible.Add(message);
            }
        }

        return new RealtimeReplay(visible, found);
    }

    public async Task DispatchAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        RealtimeConnection[] targets;
        lock (_gate)
        {
            // A backplane may redeliver; every node records and delivers a message once.
            if (!_seen.Add(message.Id))
                return;
            if (options.Value.ReplayBufferSize > 0)
                _buffer.AddLast(message);
            Trim();
            foreach (var connection in _connections)
                connection.MarkDispatched(message.Id);
            targets = [.. _connections];
        }

        metrics.Published(message.Type);
        if (targets.Length == 0)
            return;

        // Built once per message: the audience check runs per connection and must not rescan the list each time.
        var users = message.Users is { } list ? new HashSet<string>(list, StringComparer.Ordinal) : null;
        await using var scope = scopes.CreateAsyncScope();
        foreach (var connection in targets)
        {
            try
            {
                if (!await IsForAsync(connection, message, users, scope.ServiceProvider, ct).ConfigureAwait(false))
                    continue;
                if (connection.TryEnqueue(message))
                {
                    metrics.Delivered(connection.Transport);
                }
                else if (connection.CloseReason == "overflow")
                {
                    metrics.Dropped(connection.Transport);
                    logger.LogInformation("Realtime connection {ConnectionId} fell {Capacity} messages behind and was closed.",
                        connection.Id, options.Value.MaxQueuedMessagesPerConnection);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One connection's failed permission check must not stop delivery to the others.
                logger.LogWarning(ex, "Realtime message {MessageId} could not be checked for connection {ConnectionId}.", message.Id, connection.Id);
            }
        }
    }

    private async Task<bool> IsForAsync(RealtimeConnection connection, RealtimeMessage message, HashSet<string>? users, IServiceProvider services, CancellationToken ct)
    {
        if (connection.IsClosed || connection.TenantId != message.TenantId || !connection.WantsType(message.Type))
            return false;
        if (users is not null && (connection.UserId is null || !users.Contains(connection.UserId)))
            return false;
        if (message.Topic is { } topic && !connection.Follows(topic))
            return false;
        if (message.Permission is not { } permission)
            return true;

        var now = clock.GetUtcNow();
        if (connection.TryGetPermission(permission, now, out var cached))
            return cached;

        ct.ThrowIfCancellationRequested();
        // Grants are tenant-scoped: evaluate in the connection's tenant.
        var tenant = services.GetService<ICurrentTenant>();
        using (tenant?.Change(connection.Tenant))
        {
            var allowed = await RealtimePermissions.HasAsync(services, connection.User, permission).ConfigureAwait(false);
            connection.RememberPermission(permission, allowed, now + options.Value.PermissionRecheckInterval);
            return allowed;
        }
    }

    // Caller holds _gate.
    private void Trim()
    {
        var size = options.Value.ReplayBufferSize;
        var oldest = clock.GetUtcNow() - options.Value.ReplayWindow;
        while (_buffer.First is { } first && (_buffer.Count > size || first.Value.Timestamp < oldest))
            _buffer.RemoveFirst();

        // Remember ids a little longer than the buffer, for redelivery dedupe (bounded).
        if (_seen.Count > Math.Max(1024, size * 4))
        {
            _seen.Clear();
            foreach (var message in _buffer)
                _seen.Add(message.Id);
        }
    }
}
