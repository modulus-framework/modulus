namespace Modulus.Realtime.Delivery;

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Modulus.Core.Abstractions;

/// <summary>
/// One open client connection on this node (an SSE stream or a SignalR connection). The dispatcher never writes to the
/// network: it queues into a bounded channel the transport drains, and a client that lets the queue fill up is closed
/// (it reconnects and resumes from its last event id).
/// </summary>
internal sealed class RealtimeConnection
{
    private readonly Channel<RealtimeMessage> _queue;
    private readonly ConcurrentDictionary<string, byte> _topics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (bool Allowed, DateTimeOffset Until)> _permissions = new(StringComparer.Ordinal);
    private string? _firstLiveId;
    private int _closed;

    public RealtimeConnection(
        string id,
        string transport,
        ClaimsPrincipal user,
        TenantInfo? tenant,
        IReadOnlyList<string>? typeFilters,
        int capacity)
    {
        Id = id;
        Transport = transport;
        User = user;
        Tenant = tenant;
        TypeFilters = typeFilters is { Count: > 0 } ? typeFilters : null;
        UserId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _queue = Channel.CreateBounded<RealtimeMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public string Id { get; }

    /// <summary><c>sse</c> or <c>signalr</c> (a metrics tag).</summary>
    public string Transport { get; }

    public ClaimsPrincipal User { get; }

    public string? UserId { get; }

    public TenantInfo? Tenant { get; }

    public Guid? TenantId => Tenant?.TenantId;

    /// <summary>Event names or <c>prefix.*</c> patterns the client asked for; null for all.</summary>
    public IReadOnlyList<string>? TypeFilters { get; }

    public ICollection<string> Topics => _topics.Keys;

    public int TopicCount => _topics.Count;

    public ChannelReader<RealtimeMessage> Reader => _queue.Reader;

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    /// <summary>Why the connection was closed by the server (<c>overflow</c>, <c>expired</c>), or null.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>Invoked once when the server closes the connection (the transport aborts its side).</summary>
    public Action? OnClosed { get; set; }

    public bool Follow(string topic) => _topics.TryAdd(topic, 0);

    public bool Unfollow(string topic) => _topics.TryRemove(topic, out _);

    public bool Follows(string topic) => _topics.ContainsKey(topic);

    public bool WantsType(string type)
    {
        if (TypeFilters is null)
            return true;
        foreach (var filter in TypeFilters)
        {
            if (filter.EndsWith(".*", StringComparison.Ordinal)
                ? type.StartsWith(filter[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(filter, type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetPermission(string permission, DateTimeOffset now, out bool allowed)
    {
        if (_permissions.TryGetValue(permission, out var entry) && entry.Until > now)
        {
            allowed = entry.Allowed;
            return true;
        }

        allowed = false;
        return false;
    }

    public void RememberPermission(string permission, bool allowed, DateTimeOffset until)
        => _permissions[permission] = (allowed, until);

    /// <summary>Queues a message; false when the connection is closed or its queue is full (then it is closed).</summary>
    /// <summary>
    /// The first message dispatched after this connection registered (whether or not it was addressed to it). A resume
    /// replays the buffer up to, not including, this message: everything from it on reaches the connection live.
    /// </summary>
    public string? FirstLiveId => Volatile.Read(ref _firstLiveId);

    public void MarkDispatched(string messageId) => Interlocked.CompareExchange(ref _firstLiveId, messageId, null);

    public bool TryEnqueue(RealtimeMessage message)
    {
        if (IsClosed)
            return false;
        if (_queue.Writer.TryWrite(message))
            return true;
        Close("overflow");
        return false;
    }

    public void Close(string? reason = null)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;
        CloseReason = reason;
        _queue.Writer.TryComplete();
        OnClosed?.Invoke();
    }
}
